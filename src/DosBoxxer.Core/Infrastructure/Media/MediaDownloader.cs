using System.Security.Cryptography;
using System.Text;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models.Metadata;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Media;

/// <summary>
/// Downloads and imports cover art and screenshots.
///
/// Security notes:
/// <list type="bullet">
/// <item>The target file name is <c>{gameId}-{kind}-{index}-{urlHash}.{ext}</c>. Nothing from
/// the API influences the directory, and the extension is taken from a fixed whitelist.</item>
/// <item>Only URLs the active provider's <see cref="IMediaHttpClient"/> accepts are fetched;
/// each provider validates the host itself.</item>
/// <item>Deletion is restricted to files inside the launcher's own cache directories.</item>
/// </list>
/// </summary>
public sealed class MediaDownloader : IMediaDownloader
{
    private const long MaxMediaBytes = 20 * 1024 * 1024;

    private static readonly string[] AllowedExtensions = { "png", "jpg", "jpeg", "webp", "gif", "bmp" };

    private readonly IMediaHttpClient _client;
    private readonly IAppPaths _paths;
    private readonly ILogger<MediaDownloader> _logger;

    public MediaDownloader(IMediaHttpClient client, IAppPaths paths, ILogger<MediaDownloader> logger)
    {
        _client = client;
        _paths = paths;
        _logger = logger;
    }

    public async Task<DownloadedMedia?> DownloadAsync(
        Guid gameId,
        MetadataMedia media,
        int index,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(media);

        var directory = media.Kind == MediaKind.Cover ? _paths.CoversDirectory : _paths.ScreenshotsDirectory;
        Directory.CreateDirectory(directory);

        var extension = NormalizeExtension(media.Format) ?? NormalizeExtension(GuessExtensionFromUrl(media.Url)) ?? "png";
        var fileName = BuildFileName(gameId, media.Kind, index, media.Url, extension);
        var target = Path.Combine(directory, fileName);

        if (File.Exists(target))
        {
            _logger.LogDebug("Media already cached, skipping download");
            return new DownloadedMedia { LocalPath = target, SourceUrl = media.Url };
        }

        try
        {
            await using var source = await _client.DownloadMediaAsync(media.Url, cancellationToken).ConfigureAwait(false);
            if (source is null)
            {
                return null;
            }

            if (source.CanSeek && source.Length > MaxMediaBytes)
            {
                _logger.LogWarning("Refusing to store a media file larger than {Limit} bytes", MaxMediaBytes);
                return null;
            }

            var temp = target + ".part";

            await using (var destination = File.Create(temp))
            {
                await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
            }

            File.Move(temp, target, overwrite: true);

            _logger.LogInformation("Downloaded {Kind} for game {GameId}", media.Kind, gameId);
            return new DownloadedMedia { LocalPath = target, SourceUrl = media.Url };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or HttpRequestException)
        {
            _logger.LogWarning("Media download failed ({Type})", ex.GetType().Name);
            return null;
        }
    }

    public Task<string?> ImportLocalCoverAsync(Guid gameId, string sourcePath, CancellationToken cancellationToken = default) =>
        ImportAsync(gameId, sourcePath, MediaKind.Cover, 0, cancellationToken);

    public Task<string?> ImportLocalScreenshotAsync(Guid gameId, string sourcePath, int index, CancellationToken cancellationToken = default) =>
        ImportAsync(gameId, sourcePath, MediaKind.Screenshot, index, cancellationToken);

    private async Task<string?> ImportAsync(
        Guid gameId,
        string sourcePath,
        MediaKind kind,
        int index,
        CancellationToken cancellationToken)
    {
        if (!PathHelper.FileExistsSafe(sourcePath))
        {
            return null;
        }

        var extension = NormalizeExtension(Path.GetExtension(sourcePath).TrimStart('.'));
        if (extension is null)
        {
            _logger.LogWarning("Unsupported image format selected");
            return null;
        }

        var directory = kind == MediaKind.Cover ? _paths.CoversDirectory : _paths.ScreenshotsDirectory;
        Directory.CreateDirectory(directory);

        // The hash makes re-importing a different file produce a different name, which avoids
        // stale images being served from an in-memory bitmap cache.
        var fileName = BuildFileName(gameId, kind, index, sourcePath + File.GetLastWriteTimeUtc(sourcePath).Ticks, extension);
        var target = Path.Combine(directory, fileName);

        try
        {
            await using var source = File.OpenRead(sourcePath);

            if (source.Length > MaxMediaBytes)
            {
                _logger.LogWarning("Selected image is larger than {Limit} bytes", MaxMediaBytes);
                return null;
            }

            await using var destination = File.Create(target);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Imported local {Kind} for game {GameId}", kind, gameId);
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Local image could not be imported ({Type})", ex.GetType().Name);
            return null;
        }
    }

    public Task DeleteMediaAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        var prefix = gameId.ToString("N") + "-";

        foreach (var directory in new[] { _paths.CoversDirectory, _paths.ScreenshotsDirectory })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, prefix + "*"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("Cached media for game {GameId} could not be deleted completely", gameId);
            }
        }

        return Task.CompletedTask;
    }

    public Task ClearCacheAsync(CancellationToken cancellationToken = default)
    {
        foreach (var directory in new[] { _paths.CoversDirectory, _paths.ScreenshotsDirectory })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(file);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning("Media cache could not be cleared completely");
            }
        }

        _logger.LogInformation("Media cache cleared");
        return Task.CompletedTask;
    }

    public Task<long> GetCacheSizeAsync(CancellationToken cancellationToken = default)
    {
        long total = 0;

        foreach (var directory in new[] { _paths.CoversDirectory, _paths.ScreenshotsDirectory, _paths.MetadataCacheDirectory })
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    total += new FileInfo(file).Length;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Reporting an approximate size is fine.
            }
        }

        return Task.FromResult(total);
    }

    internal static string BuildFileName(Guid gameId, MediaKind kind, int index, string seed, string extension)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..12].ToLowerInvariant();
        var kindPart = kind == MediaKind.Cover ? "cover" : "shot";
        return $"{gameId:N}-{kindPart}-{index:D2}-{hash}.{extension}";
    }

    internal static string? NormalizeExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return null;
        }

        var value = extension.Trim().TrimStart('.').ToLowerInvariant();
        return AllowedExtensions.Contains(value) ? value : null;
    }

    private static string? GuessExtensionFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        // ScreenScraper media URLs carry the format in a query parameter rather than in the
        // path, so this is only a best effort fallback.
        var extension = Path.GetExtension(uri.AbsolutePath);
        return string.IsNullOrEmpty(extension) ? null : extension.TrimStart('.');
    }
}
