using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Metadata;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure;

/// <summary>
/// Orchestrates repository, metadata provider, merger and media cache. This is the only service
/// the ViewModels use for library operations.
/// </summary>
public sealed class GameLibraryService : IGameLibraryService
{
    private readonly IGameRepository _repository;
    private readonly IGameMetadataProvider _metadataProvider;
    private readonly IMetadataMerger _merger;
    private readonly IMediaDownloader _mediaDownloader;
    private readonly ISettingsService _settings;
    private readonly ILogger<GameLibraryService> _logger;

    public GameLibraryService(
        IGameRepository repository,
        IGameMetadataProvider metadataProvider,
        IMetadataMerger merger,
        IMediaDownloader mediaDownloader,
        ISettingsService settings,
        ILogger<GameLibraryService> logger)
    {
        _repository = repository;
        _metadataProvider = metadataProvider;
        _merger = merger;
        _mediaDownloader = mediaDownloader;
        _settings = settings;
        _logger = logger;
    }

    public Task<IReadOnlyList<Game>> GetLibraryAsync(CancellationToken cancellationToken = default) =>
        _repository.GetAllAsync(cancellationToken);

    public IReadOnlyList<Game> Filter(IReadOnlyList<Game> source, LibraryQuery query)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<Game> result = source;

        result = query.Scope switch
        {
            LibraryScope.Favorites => result.Where(g => g.IsFavorite),
            LibraryScope.RecentlyPlayed => result.Where(g => g.LastPlayed.HasValue),
            LibraryScope.Genre when query.Genre.HasValue => result.Where(g => g.Genres.Contains(query.Genre.Value)),
            _ => result,
        };

        if (query.ReleaseYear.HasValue)
        {
            result = result.Where(g => g.ReleaseYear == query.ReleaseYear.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Publisher))
        {
            result = result.Where(g =>
                g.Publisher is not null &&
                g.Publisher.Contains(query.Publisher, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var terms = query.SearchText
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            result = result.Where(g => terms.All(term => MatchesSearch(g, term)));
        }

        // RecentlyPlayed is a curated view, so it always uses its own ordering.
        var sortOrder = query.Scope == LibraryScope.RecentlyPlayed ? GameSortOrder.LastPlayed : query.SortOrder;

        result = sortOrder switch
        {
            GameSortOrder.TitleAscending => result.OrderBy(g => g.SortTitle, StringComparer.OrdinalIgnoreCase),
            GameSortOrder.TitleDescending => result.OrderByDescending(g => g.SortTitle, StringComparer.OrdinalIgnoreCase),
            GameSortOrder.YearAscending => result
                .OrderBy(g => g.ReleaseYear ?? int.MaxValue)
                .ThenBy(g => g.SortTitle, StringComparer.OrdinalIgnoreCase),
            GameSortOrder.YearDescending => result
                .OrderByDescending(g => g.ReleaseYear ?? int.MinValue)
                .ThenBy(g => g.SortTitle, StringComparer.OrdinalIgnoreCase),
            GameSortOrder.RecentlyAdded => result
                .OrderByDescending(g => g.DateAdded)
                .ThenBy(g => g.SortTitle, StringComparer.OrdinalIgnoreCase),
            GameSortOrder.LastPlayed => result
                .OrderByDescending(g => g.LastPlayed ?? DateTimeOffset.MinValue)
                .ThenBy(g => g.SortTitle, StringComparer.OrdinalIgnoreCase),
            GameSortOrder.PlayTime => result
                .OrderByDescending(g => g.TotalPlayTimeSeconds)
                .ThenBy(g => g.SortTitle, StringComparer.OrdinalIgnoreCase),
            _ => result.OrderBy(g => g.SortTitle, StringComparer.OrdinalIgnoreCase),
        };

        return result.ToList();
    }

    private static bool MatchesSearch(Game game, string term)
    {
        if (game.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (game.Publisher is not null && game.Publisher.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (game.Developer is not null && game.Developer.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (game.ReleaseYear.HasValue &&
            game.ReleaseYear.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                .Contains(term, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    public async Task<Game> AddGameAsync(
        AddGameRequest request,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var directory = PathHelper.Normalize(request.GameDirectory);
        var launchFile = PathHelper.Normalize(request.LaunchFile);

        var game = new Game
        {
            GameDirectory = directory,
            LaunchFile = launchFile,
            RelativeLaunchFile = PathHelper.GetRelativePathWithin(directory, launchFile) ?? Path.GetFileName(launchFile),
            Title = string.IsNullOrWhiteSpace(request.Title) ? Path.GetFileName(directory) : request.Title.Trim(),
            DateAdded = DateTimeOffset.UtcNow,
        };

        game.SortTitle = TitleCleaner.ToSortTitle(game.Title);

        if (request.SavegameConfig is not null)
        {
            var config = request.SavegameConfig.Clone();
            config.GameId = game.Id;
            game.SavegameConfig = config;
        }

        if (request.Metadata is not null)
        {
            _merger.Merge(game, request.Metadata, MetadataMergeOptions.Initial);
            await DownloadMediaAsync(game, request.Metadata, progress, cancellationToken).ConfigureAwait(false);
        }

        if (game.Genres.Count == 0)
        {
            game.Genres.Add(GenreKey.Other);
        }

        await _repository.AddAsync(game, cancellationToken).ConfigureAwait(false);
        return game;
    }

    public Task UpdateGameAsync(Game game, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);

        game.GameDirectory = PathHelper.Normalize(game.GameDirectory);
        game.LaunchFile = PathHelper.Normalize(game.LaunchFile);
        game.RelativeLaunchFile = PathHelper.GetRelativePathWithin(game.GameDirectory, game.LaunchFile)
                                  ?? Path.GetFileName(game.LaunchFile);
        game.SortTitle = TitleCleaner.ToSortTitle(game.Title);

        return _repository.UpdateAsync(game, cancellationToken);
    }

    public async Task RemoveGameAsync(Guid gameId, bool deleteCachedMedia, CancellationToken cancellationToken = default)
    {
        // The game's own files are never touched — only launcher owned data is removed.
        await _repository.DeleteAsync(gameId, cancellationToken).ConfigureAwait(false);

        if (deleteCachedMedia)
        {
            await _mediaDownloader.DeleteMediaAsync(gameId, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<MetadataResult<MetadataMergeResult>> RefreshMetadataAsync(
        Game game,
        string providerGameId,
        MetadataMergeOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(providerGameId))
        {
            return MetadataResult<MetadataMergeResult>.Failure(MetadataErrorKind.NoResults);
        }

        var language = _settings.Current.Language;

        var metadataResult = await _metadataProvider
            .GetGameAsync(providerGameId, language, cancellationToken)
            .ConfigureAwait(false);

        if (!metadataResult.IsSuccess || metadataResult.Value is null)
        {
            return MetadataResult<MetadataMergeResult>.Failure(metadataResult.Error, metadataResult.Detail);
        }

        var metadata = metadataResult.Value;
        var mergeResult = _merger.Merge(game, metadata, options);

        var mediaAllowed = options.OverwriteManualEdits || !game.ManualFields.HasFlag(GameField.Cover);
        if (options.Fields.HasFlag(GameField.Cover) && mediaAllowed)
        {
            await DownloadCoverAsync(game, metadata, progress, cancellationToken).ConfigureAwait(false);
        }

        var screenshotsAllowed = options.OverwriteManualEdits || !game.ManualFields.HasFlag(GameField.Screenshots);
        if (options.Fields.HasFlag(GameField.Screenshots) && screenshotsAllowed)
        {
            await DownloadScreenshotsAsync(game, metadata, progress, cancellationToken).ConfigureAwait(false);
        }

        await UpdateGameAsync(game, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Metadata refresh for '{Title}': updated {Updated}, kept manual {Skipped}",
            game.Title,
            mergeResult.UpdatedFields,
            mergeResult.SkippedManualFields);

        return MetadataResult<MetadataMergeResult>.Success(mergeResult);
    }

    public async Task<string?> SetCoverFromFileAsync(Game game, string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);

        var path = await _mediaDownloader.ImportLocalCoverAsync(game.Id, imagePath, cancellationToken).ConfigureAwait(false);
        if (path is null)
        {
            return null;
        }

        game.CoverImagePath = path;
        game.ManualFields |= GameField.Cover;

        await UpdateGameAsync(game, cancellationToken).ConfigureAwait(false);
        return path;
    }

    public async Task<Screenshot?> AddScreenshotFromFileAsync(Game game, string imagePath, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);

        var index = game.Screenshots.Count;
        var path = await _mediaDownloader
            .ImportLocalScreenshotAsync(game.Id, imagePath, index, cancellationToken)
            .ConfigureAwait(false);

        if (path is null)
        {
            return null;
        }

        var screenshot = new Screenshot
        {
            GameId = game.Id,
            LocalPath = path,
            SortOrder = index,
        };

        game.Screenshots.Add(screenshot);
        game.ManualFields |= GameField.Screenshots;

        await UpdateGameAsync(game, cancellationToken).ConfigureAwait(false);
        return screenshot;
    }

    public async Task SetFavoriteAsync(Game game, bool isFavorite, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);

        game.IsFavorite = isFavorite;
        await _repository.SetFavoriteAsync(game.Id, isFavorite, cancellationToken).ConfigureAwait(false);
    }

    public async Task RecordPlaySessionAsync(
        Game game,
        DateTimeOffset startedAt,
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);

        var seconds = (long)Math.Max(0, duration.TotalSeconds);

        await _repository.RecordPlaySessionAsync(game.Id, startedAt, seconds, cancellationToken).ConfigureAwait(false);

        game.LastPlayed = startedAt;
        game.PlayCount += 1;
        game.TotalPlayTimeSeconds += seconds;

        _logger.LogInformation(
            "Recorded play session for '{Title}': {Seconds}s, total {Total}s",
            game.Title,
            seconds,
            game.TotalPlayTimeSeconds);
    }

    public Task<IReadOnlyDictionary<GenreKey, int>> GetGenreCountsAsync(CancellationToken cancellationToken = default) =>
        _repository.GetGenreCountsAsync(cancellationToken);

    // ---- media ----------------------------------------------------------------------------

    private async Task DownloadMediaAsync(
        Game game,
        GameMetadata metadata,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await DownloadCoverAsync(game, metadata, progress, cancellationToken).ConfigureAwait(false);
        await DownloadScreenshotsAsync(game, metadata, progress, cancellationToken).ConfigureAwait(false);
    }

    private async Task DownloadCoverAsync(
        Game game,
        GameMetadata metadata,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (metadata.Cover is null)
        {
            return;
        }

        progress?.Report("cover");

        var downloaded = await _mediaDownloader
            .DownloadAsync(game.Id, metadata.Cover, 0, cancellationToken)
            .ConfigureAwait(false);

        if (downloaded is not null)
        {
            game.CoverImagePath = downloaded.LocalPath;
        }
    }

    private async Task DownloadScreenshotsAsync(
        Game game,
        GameMetadata metadata,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (!_settings.Current.CacheScreenshots || metadata.Screenshots.Count == 0)
        {
            return;
        }

        var imported = new List<Screenshot>();
        var index = 0;

        // Sequential on purpose: the provider limits parallel requests per account.
        foreach (var media in metadata.Screenshots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report("screenshot");

            var downloaded = await _mediaDownloader
                .DownloadAsync(game.Id, media, index, cancellationToken)
                .ConfigureAwait(false);

            if (downloaded is null)
            {
                continue;
            }

            imported.Add(new Screenshot
            {
                GameId = game.Id,
                LocalPath = downloaded.LocalPath,
                RemoteUrl = downloaded.SourceUrl,
                SortOrder = index,
            });

            index++;
        }

        if (imported.Count > 0)
        {
            game.Screenshots.Clear();
            game.Screenshots.AddRange(imported);
        }
    }
}
