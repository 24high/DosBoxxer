using System.Text.Json;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models.Cloud;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Cloud;

/// <summary>
/// Stores per-game sync metadata as one JSON file per game under the launcher data directory.
/// Reads degrade to a fresh, empty record so a corrupt metadata file can never block a sync or
/// endanger local savegames — it only means the engine falls back to conservative comparison.
/// </summary>
public sealed class SyncMetadataStore : ISyncMetadataStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<SyncMetadataStore> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SyncMetadataStore(IAppPaths paths, ILogger<SyncMetadataStore> logger)
    {
        _paths = paths;
        _logger = logger;
    }

    public async Task<GameSyncMetadata> LoadAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        var file = FileFor(gameId);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(file))
            {
                return new GameSyncMetadata { GameId = gameId };
            }

            await using var stream = File.OpenRead(file);
            var metadata = await JsonSerializer.DeserializeAsync<GameSyncMetadata>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);

            if (metadata is null)
            {
                return new GameSyncMetadata { GameId = gameId };
            }

            metadata.GameId = gameId;
            metadata.Files ??= new Dictionary<string, FileSyncRecord>(StringComparer.Ordinal);
            return metadata;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Sync metadata for {GameId} could not be read; using an empty record", gameId);
            return new GameSyncMetadata { GameId = gameId };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(GameSyncMetadata metadata, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        var file = FileFor(metadata.GameId);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_paths.SyncMetadataDirectory);

            var tempFile = file + ".tmp";
            await using (var stream = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(stream, metadata, SerializerOptions, cancellationToken).ConfigureAwait(false);
            }

            File.Move(tempFile, file, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to write sync metadata for {GameId}", metadata.GameId);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        var file = FileFor(gameId);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to delete sync metadata for {GameId}", gameId);
        }
        finally
        {
            _lock.Release();
        }
    }

    private string FileFor(Guid gameId) =>
        Path.Combine(_paths.SyncMetadataDirectory, $"{gameId:N}.json");
}
