using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models.Metadata;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Media;

/// <summary>
/// Stores provider responses as JSON files below <c>cache/metadata</c>. File names are derived
/// from a hash of the cache key, so nothing from the network ever influences the path.
/// </summary>
public sealed class FileMetadataCache : IMetadataCache
{
    private static readonly TimeSpan GameTtl = TimeSpan.FromDays(30);
    private static readonly TimeSpan SearchTtl = TimeSpan.FromDays(2);

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<FileMetadataCache> _logger;

    public FileMetadataCache(IAppPaths paths, ILogger<FileMetadataCache> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public Task<GameMetadata?> GetAsync(string providerKey, string providerGameId, string languageCode, CancellationToken cancellationToken = default) =>
        ReadAsync<GameMetadata>(BuildPath("game", providerKey, providerGameId, languageCode), GameTtl, cancellationToken);

    public Task SetAsync(string providerKey, string providerGameId, string languageCode, GameMetadata metadata, CancellationToken cancellationToken = default) =>
        WriteAsync(BuildPath("game", providerKey, providerGameId, languageCode), metadata, cancellationToken);

    public async Task<IReadOnlyList<MetadataSearchResult>?> GetSearchAsync(string providerKey, string searchTerm, CancellationToken cancellationToken = default)
    {
        var list = await ReadAsync<List<MetadataSearchResult>>(
            BuildPath("search", providerKey, searchTerm.ToLowerInvariant()),
            SearchTtl,
            cancellationToken).ConfigureAwait(false);

        return list;
    }

    public Task SetSearchAsync(string providerKey, string searchTerm, IReadOnlyList<MetadataSearchResult> results, CancellationToken cancellationToken = default) =>
        WriteAsync(BuildPath("search", providerKey, searchTerm.ToLowerInvariant()), results.ToList(), cancellationToken);

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (Directory.Exists(_paths.MetadataCacheDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(_paths.MetadataCacheDirectory, "*.json"))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Delete(file);
                }
            }

            _logger.LogInformation("Metadata cache cleared");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Metadata cache could not be cleared completely");
        }

        return Task.CompletedTask;
    }

    private async Task<T?> ReadAsync<T>(string path, TimeSpan ttl, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            if (DateTimeOffset.UtcNow - new FileInfo(path).LastWriteTimeUtc > ttl)
            {
                _logger.LogDebug("Cache entry expired, deleting it");
                File.Delete(path);
                return null;
            }

            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Cache entry could not be read ({Type})", ex.GetType().Name);
            return null;
        }
    }

    private async Task WriteAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_paths.MetadataCacheDirectory);

            await using var stream = File.Create(path);
            await JsonSerializer.SerializeAsync(stream, value, SerializerOptions, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Cache entry could not be written ({Type})", ex.GetType().Name);
        }
    }

    private string BuildPath(params string[] keyParts)
    {
        var key = string.Join('|', keyParts);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..24].ToLowerInvariant();
        var fileName = PathHelper.SanitizeFileName($"{keyParts[0]}-{hash}.json");
        return Path.Combine(_paths.MetadataCacheDirectory, fileName);
    }
}
