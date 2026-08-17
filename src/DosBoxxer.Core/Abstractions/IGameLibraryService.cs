using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Metadata;
using DosBoxxer.Core.Models.Savegame;

namespace DosBoxxer.Core.Abstractions;

public sealed class AddGameRequest
{
    public required string GameDirectory { get; init; }

    public required string LaunchFile { get; init; }

    public required string Title { get; init; }

    /// <summary>Metadata chosen in the wizard. <c>null</c> when the user skipped the lookup.</summary>
    public GameMetadata? Metadata { get; init; }

    /// <summary>Savegame configuration chosen in the wizard. <c>null</c> means "not configured".</summary>
    public SavegameConfig? SavegameConfig { get; init; }
}

/// <summary>
/// Application service that orchestrates repository, metadata provider and media cache.
/// ViewModels talk to this, never to the repository directly.
/// </summary>
public interface IGameLibraryService
{
    Task<IReadOnlyList<Game>> GetLibraryAsync(CancellationToken cancellationToken = default);

    /// <summary>Applies scope, text search and sorting in memory. Pure function — used in tests.</summary>
    IReadOnlyList<Game> Filter(IReadOnlyList<Game> source, LibraryQuery query);

    Task<Game> AddGameAsync(AddGameRequest request, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    Task UpdateGameAsync(Game game, CancellationToken cancellationToken = default);

    /// <summary>Removes the entry from the database. The game's own files are never touched.</summary>
    Task RemoveGameAsync(Guid gameId, bool deleteCachedMedia, CancellationToken cancellationToken = default);

    Task<MetadataResult<MetadataMergeResult>> RefreshMetadataAsync(
        Game game,
        string providerGameId,
        MetadataMergeOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);

    Task<string?> SetCoverFromFileAsync(Game game, string imagePath, CancellationToken cancellationToken = default);

    Task<Screenshot?> AddScreenshotFromFileAsync(Game game, string imagePath, CancellationToken cancellationToken = default);

    Task SetFavoriteAsync(Game game, bool isFavorite, CancellationToken cancellationToken = default);

    Task RecordPlaySessionAsync(Game game, DateTimeOffset startedAt, TimeSpan duration, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<GenreKey, int>> GetGenreCountsAsync(CancellationToken cancellationToken = default);
}
