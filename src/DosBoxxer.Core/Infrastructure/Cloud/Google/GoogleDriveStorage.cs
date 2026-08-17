using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models.Cloud;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Cloud.Google;

/// <summary>
/// Google Drive storage backend built directly on the Drive v3 REST API (no SDK dependency, in
/// keeping with the other metadata clients). All access is scoped to <c>drive.file</c>, i.e. only
/// the folders and files this application creates. Folder ids are cached by the caller so repeated
/// syncs do not recreate the <c>dosboxxer/&lt;game-id&gt;</c> hierarchy.
/// </summary>
public sealed class GoogleDriveStorage : ICloudStorage
{
    public const string ApiBase = "https://www.googleapis.com/drive/v3";
    public const string UploadBase = "https://www.googleapis.com/upload/drive/v3";
    private const string FolderMimeType = "application/vnd.google-apps.folder";
    public const string RootFolderName = "dosboxxer";

    private readonly ICloudAuthService _auth;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<GoogleDriveStorage> _logger;

    public GoogleDriveStorage(ICloudAuthService auth, IHttpClientFactory httpFactory, ILogger<GoogleDriveStorage> logger)
    {
        _auth = auth;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<string> EnsureGameFolderAsync(Guid gameId, string? knownFolderId, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(knownFolderId) && await FolderExistsAsync(knownFolderId, cancellationToken).ConfigureAwait(false))
        {
            return knownFolderId;
        }

        var rootId = await EnsureFolderAsync(RootFolderName, parentId: null, cancellationToken).ConfigureAwait(false);
        return await EnsureFolderAsync(gameId.ToString("N"), rootId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CloudFile>> ListFilesAsync(string gameFolderId, CancellationToken cancellationToken = default)
    {
        var results = new List<CloudFile>();
        await ListRecursiveAsync(gameFolderId, prefix: string.Empty, results, cancellationToken).ConfigureAwait(false);
        return results;
    }

    private async Task ListRecursiveAsync(string folderId, string prefix, List<CloudFile> results, CancellationToken cancellationToken)
    {
        string? pageToken = null;
        do
        {
            var query = Uri.EscapeDataString($"'{folderId}' in parents and trashed=false");
            var url = $"{ApiBase}/files?q={query}&fields=nextPageToken,files(id,name,mimeType,modifiedTime,size,md5Checksum)&pageSize=1000&spaces=drive";
            if (pageToken is not null)
            {
                url += $"&pageToken={pageToken}";
            }

            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false);
            using var doc = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;

            if (root.TryGetProperty("files", out var files))
            {
                foreach (var file in files.EnumerateArray())
                {
                    var id = file.GetProperty("id").GetString()!;
                    var name = file.GetProperty("name").GetString()!;
                    var mime = file.TryGetProperty("mimeType", out var m) ? m.GetString() : null;
                    var relativePath = prefix.Length == 0 ? name : $"{prefix}/{name}";

                    if (mime == FolderMimeType)
                    {
                        await ListRecursiveAsync(id, relativePath, results, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    results.Add(new CloudFile
                    {
                        Id = id,
                        RelativePath = relativePath,
                        Size = file.TryGetProperty("size", out var size) && long.TryParse(size.GetString(), out var s) ? s : 0,
                        ModifiedUtc = ParseTime(file),
                        Md5 = file.TryGetProperty("md5Checksum", out var md5) ? md5.GetString() : null,
                    });
                }
            }

            pageToken = root.TryGetProperty("nextPageToken", out var next) ? next.GetString() : null;
        }
        while (pageToken is not null);
    }

    public async Task DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken = default)
    {
        var url = $"{ApiBase}/files/{fileId}?alt=media";
        using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, SyncErrorKind.DownloadFailed);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await stream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CloudFile> UploadAsync(
        string gameFolderId,
        string relativePath,
        Stream content,
        DateTimeOffset modifiedUtc,
        string? existingFileId,
        CancellationToken cancellationToken = default)
    {
        var segments = relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var fileName = segments[^1];

        // Ensure the sub folder chain exists so the relative structure is mirrored.
        var parentId = gameFolderId;
        for (var i = 0; i < segments.Length - 1; i++)
        {
            parentId = await EnsureFolderAsync(segments[i], parentId, cancellationToken).ConfigureAwait(false);
        }

        var metadata = new Dictionary<string, object>
        {
            ["name"] = fileName,
            ["modifiedTime"] = modifiedUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
        };

        if (existingFileId is null)
        {
            metadata["parents"] = new[] { parentId };
        }

        using var multipart = new MultipartContent("related");
        var metadataJson = JsonSerializer.Serialize(metadata);
        var metadataPart = new StringContent(metadataJson, Encoding.UTF8);
        metadataPart.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "UTF-8" };
        multipart.Add(metadataPart);

        var mediaPart = new StreamContent(content);
        mediaPart.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(mediaPart);

        var method = existingFileId is null ? HttpMethod.Post : HttpMethod.Patch;
        var url = existingFileId is null
            ? $"{UploadBase}/files?uploadType=multipart&fields=id,name,modifiedTime,size,md5Checksum"
            : $"{UploadBase}/files/{existingFileId}?uploadType=multipart&fields=id,name,modifiedTime,size,md5Checksum";

        using var response = await SendAsync(method, url, multipart, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(response, SyncErrorKind.UploadFailed);

        using var doc = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
        var element = doc.RootElement;

        return new CloudFile
        {
            Id = element.GetProperty("id").GetString()!,
            RelativePath = relativePath,
            Size = element.TryGetProperty("size", out var size) && long.TryParse(size.GetString(), out var s) ? s : 0,
            ModifiedUtc = ParseTime(element),
            Md5 = element.TryGetProperty("md5Checksum", out var md5) ? md5.GetString() : null,
        };
    }

    public async Task DeleteAsync(string fileId, CancellationToken cancellationToken = default)
    {
        // Move to trash rather than hard-delete: conservative handling of removals.
        var url = $"{ApiBase}/files/{fileId}";
        using var body = new StringContent("{\"trashed\":true}", Encoding.UTF8, "application/json");
        using var response = await SendAsync(HttpMethod.Patch, url, body, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return; // Already gone — not an error.
        }

        EnsureSuccess(response, SyncErrorKind.Unexpected);
    }

    // ---- folder helpers -------------------------------------------------------------------

    private async Task<bool> FolderExistsAsync(string folderId, CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{ApiBase}/files/{folderId}?fields=id,trashed,mimeType";
            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            EnsureSuccess(response, SyncErrorKind.Unexpected);
            using var doc = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            var root = doc.RootElement;
            var trashed = root.TryGetProperty("trashed", out var t) && t.GetBoolean();
            return !trashed;
        }
        catch (CloudStorageException ex) when (ex.Kind == SyncErrorKind.Unexpected)
        {
            return false;
        }
    }

    private async Task<string> EnsureFolderAsync(string name, string? parentId, CancellationToken cancellationToken)
    {
        var parentClause = parentId is null ? "'root' in parents" : $"'{parentId}' in parents";
        var q = $"name='{name.Replace("'", "\\'", StringComparison.Ordinal)}' and mimeType='{FolderMimeType}' and trashed=false and {parentClause}";
        var url = $"{ApiBase}/files?q={Uri.EscapeDataString(q)}&fields=files(id,name)&spaces=drive&pageSize=1";

        using (var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken).ConfigureAwait(false))
        {
            EnsureSuccess(response, SyncErrorKind.Unexpected);
            using var doc = await ReadJsonAsync(response, cancellationToken).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("files", out var files) && files.GetArrayLength() > 0)
            {
                return files[0].GetProperty("id").GetString()!;
            }
        }

        // Create it.
        var metadata = new Dictionary<string, object> { ["name"] = name, ["mimeType"] = FolderMimeType };
        if (parentId is not null)
        {
            metadata["parents"] = new[] { parentId };
        }

        using var createBody = new StringContent(JsonSerializer.Serialize(metadata), Encoding.UTF8, "application/json");
        using var createResponse = await SendAsync(HttpMethod.Post, $"{ApiBase}/files?fields=id", createBody, cancellationToken).ConfigureAwait(false);
        EnsureSuccess(createResponse, SyncErrorKind.Unexpected);

        using var created = await ReadJsonAsync(createResponse, cancellationToken).ConfigureAwait(false);
        return created.RootElement.GetProperty("id").GetString()!;
    }

    // ---- HTTP plumbing --------------------------------------------------------------------

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken)
    {
        var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(token))
        {
            throw new CloudStorageException(SyncErrorKind.NotAuthenticated, "No valid access token");
        }

        var client = _httpFactory.CreateClient(GoogleOAuthService.HttpClientName);
        using var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new CloudStorageException(SyncErrorKind.Network, ex.Message, ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CloudStorageException(SyncErrorKind.Network, "The request timed out", ex);
        }
    }

    private static void EnsureSuccess(HttpResponseMessage response, SyncErrorKind failureKind)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        throw response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => new CloudStorageException(SyncErrorKind.AuthRevoked, "Google rejected the access token"),
            HttpStatusCode.Forbidden => new CloudStorageException(SyncErrorKind.AuthRevoked, "Access to Google Drive was denied"),
            _ => new CloudStorageException(failureKind, $"Google Drive returned {(int)response.StatusCode}"),
        };
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        EnsureSuccess(response, SyncErrorKind.Unexpected);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private static DateTimeOffset ParseTime(JsonElement element)
    {
        if (element.TryGetProperty("modifiedTime", out var time) &&
            DateTimeOffset.TryParse(time.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return DateTimeOffset.UtcNow;
    }
}
