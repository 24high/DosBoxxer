namespace DosBoxxer.Core.Models.Metadata;

/// <summary>
/// Provider independent representation of a metadata search hit. Deliberately separate from
/// both the API DTOs and the persisted <see cref="Game"/> entity.
/// </summary>
public sealed class MetadataSearchResult
{
    public required string ProviderGameId { get; init; }

    public required string Title { get; init; }

    public int? ReleaseYear { get; init; }

    public string? Publisher { get; init; }

    public string? Developer { get; init; }

    public string? SystemName { get; init; }

    /// <summary>Small preview image URL (may be <c>null</c>); used only for the result list.</summary>
    public string? ThumbnailUrl { get; init; }

    public string DisplayLine =>
        ReleaseYear.HasValue ? $"{Title} ({ReleaseYear.Value})" : Title;
}

public enum MediaKind
{
    Cover,
    Screenshot,
}

public sealed class MetadataMedia
{
    public required MediaKind Kind { get; init; }

    public required string Url { get; init; }

    /// <summary>File extension without dot as reported by the provider, e.g. <c>png</c>.</summary>
    public string? Format { get; init; }

    public string? Region { get; init; }
}

/// <summary>Full metadata record for a single game, normalised to the launcher's own model.</summary>
public sealed class GameMetadata
{
    public required string ProviderGameId { get; init; }

    public required string Title { get; init; }

    public IReadOnlyList<string> AlternateTitles { get; init; } = Array.Empty<string>();

    public string? Description { get; init; }

    public string? Publisher { get; init; }

    public string? Developer { get; init; }

    public int? ReleaseYear { get; init; }

    public string? Players { get; init; }

    public string? SystemName { get; init; }

    public IReadOnlyList<GenreKey> Genres { get; init; } = Array.Empty<GenreKey>();

    /// <summary>Raw genre labels from the provider, kept for diagnostics and manual mapping.</summary>
    public IReadOnlyList<string> RawGenres { get; init; } = Array.Empty<string>();

    public MetadataMedia? Cover { get; init; }

    public IReadOnlyList<MetadataMedia> Screenshots { get; init; } = Array.Empty<MetadataMedia>();
}
