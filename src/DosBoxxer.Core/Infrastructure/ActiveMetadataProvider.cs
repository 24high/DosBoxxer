using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models.Metadata;

namespace DosBoxxer.Core.Infrastructure;

/// <summary>
/// Facade over all registered metadata providers. It forwards every call to the one whose
/// <see cref="IGameMetadataProvider.ProviderKey"/> matches the user's choice in the settings,
/// so the rest of the application keeps depending on a single <see cref="IGameMetadataProvider"/>
/// while the active source can be switched at runtime.
/// </summary>
public sealed class ActiveMetadataProvider : IGameMetadataProvider
{
    private readonly IReadOnlyList<IGameMetadataProvider> _providers;
    private readonly ISettingsService _settings;

    public ActiveMetadataProvider(IEnumerable<IGameMetadataProvider> providers, ISettingsService settings)
    {
        _providers = providers.Where(p => p is not ActiveMetadataProvider).ToList();
        _settings = settings;

        if (_providers.Count == 0)
        {
            throw new InvalidOperationException("No metadata providers were registered.");
        }
    }

    /// <summary>Key of the provider currently selected in the settings.</summary>
    public string ProviderKey => Current.ProviderKey;

    public bool IsConfigured => Current.IsConfigured;

    /// <summary>The provider selected in the settings, falling back to the first registered one.</summary>
    private IGameMetadataProvider Current
    {
        get
        {
            var key = _settings.Current.MetadataProviderKey;
            return _providers.FirstOrDefault(p => string.Equals(p.ProviderKey, key, StringComparison.OrdinalIgnoreCase))
                   ?? _providers[0];
        }
    }

    public Task<MetadataResult<IReadOnlyList<MetadataSearchResult>>> SearchAsync(
        string searchTerm,
        CancellationToken cancellationToken = default) =>
        Current.SearchAsync(searchTerm, cancellationToken);

    public Task<MetadataResult<GameMetadata>> GetGameAsync(
        string providerGameId,
        string languageCode,
        CancellationToken cancellationToken = default) =>
        Current.GetGameAsync(providerGameId, languageCode, cancellationToken);

    public Task<MetadataResult<string>> TestConnectionAsync(CancellationToken cancellationToken = default) =>
        Current.TestConnectionAsync(cancellationToken);
}
