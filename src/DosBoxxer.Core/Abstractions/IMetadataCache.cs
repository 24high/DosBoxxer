using DosBoxxer.Core.Models.Metadata;

namespace DosBoxxer.Core.Abstractions;

/// <summary>
/// On-disk cache for provider responses so restarting the launcher does not trigger new API
/// requests. Entries expire after a configurable time to allow eventual refresh.
/// </summary>
public interface IMetadataCache
{
    Task<GameMetadata?> GetAsync(string providerKey, string providerGameId, string languageCode, CancellationToken cancellationToken = default);

    Task SetAsync(string providerKey, string providerGameId, string languageCode, GameMetadata metadata, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MetadataSearchResult>?> GetSearchAsync(string providerKey, string searchTerm, CancellationToken cancellationToken = default);

    Task SetSearchAsync(string providerKey, string searchTerm, IReadOnlyList<MetadataSearchResult> results, CancellationToken cancellationToken = default);

    Task ClearAsync(CancellationToken cancellationToken = default);
}
