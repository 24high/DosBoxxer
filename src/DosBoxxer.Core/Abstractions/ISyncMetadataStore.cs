using DosBoxxer.Core.Models.Cloud;

namespace DosBoxxer.Core.Abstractions;

/// <summary>Persists per-game sync metadata (last common state, Drive folder id) across runs.</summary>
public interface ISyncMetadataStore
{
    Task<GameSyncMetadata> LoadAsync(Guid gameId, CancellationToken cancellationToken = default);

    Task SaveAsync(GameSyncMetadata metadata, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid gameId, CancellationToken cancellationToken = default);
}
