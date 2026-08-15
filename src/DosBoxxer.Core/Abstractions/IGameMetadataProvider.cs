using DosBoxxer.Core.Models.Metadata;

namespace DosBoxxer.Core.Abstractions;

public enum MetadataErrorKind
{
    None,
    NotConfigured,
    InvalidCredentials,
    RateLimited,
    NoResults,
    NetworkUnavailable,
    ProviderUnavailable,
    InvalidResponse,
    Cancelled,
    Unknown,
}

/// <summary>
/// Result wrapper so the UI can render a localised error without knowing the provider or
/// having to catch provider specific exceptions.
/// </summary>
public sealed class MetadataResult<T>
{
    private MetadataResult(T? value, MetadataErrorKind error, string? detail)
    {
        Value = value;
        Error = error;
        Detail = detail;
    }

    public T? Value { get; }

    public MetadataErrorKind Error { get; }

    /// <summary>Technical detail for the log only.</summary>
    public string? Detail { get; }

    public bool IsSuccess => Error == MetadataErrorKind.None;

    public static MetadataResult<T> Success(T value) => new(value, MetadataErrorKind.None, null);

    public static MetadataResult<T> Failure(MetadataErrorKind error, string? detail = null) =>
        new(default, error, detail);
}

/// <summary>
/// Abstraction over an online metadata source. The UI layer must not reference ScreenScraper
/// types directly — only this interface and the models in <c>DosBoxxer.Core.Models.Metadata</c>.
/// </summary>
public interface IGameMetadataProvider
{
    /// <summary>Stable provider key, persisted with the game (e.g. <c>screenscraper</c>).</summary>
    string ProviderKey { get; }

    /// <summary>True when the provider has everything it needs to perform requests.</summary>
    bool IsConfigured { get; }

    Task<MetadataResult<IReadOnlyList<MetadataSearchResult>>> SearchAsync(
        string searchTerm,
        CancellationToken cancellationToken = default);

    Task<MetadataResult<GameMetadata>> GetGameAsync(
        string providerGameId,
        string languageCode,
        CancellationToken cancellationToken = default);

    /// <summary>Verifies the configured credentials against the provider.</summary>
    Task<MetadataResult<string>> TestConnectionAsync(CancellationToken cancellationToken = default);
}
