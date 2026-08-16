using System.Globalization;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Infrastructure.Igdb.Dto;
using DosBoxxer.Core.Infrastructure.ScreenScraper;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Metadata;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Igdb;

/// <summary>
/// Adapts IGDB to the launcher's <see cref="IGameMetadataProvider"/> contract.
///
/// The search response already carries the cover, screenshots, genres and companies (IGDB
/// expands them in one request), so a game lookup needs a single API call. Descriptions
/// (<c>summary</c>) are plain-text English, so the requested language is ignored for them.
/// </summary>
public sealed class IgdbMetadataProvider : IGameMetadataProvider
{
    public const string Key = "igdb";

    private const string CoverSize = "t_cover_big";
    private const string ScreenshotSize = "t_screenshot_big";

    private readonly IgdbClient _client;
    private readonly ISettingsService _settings;
    private readonly IMetadataCache _cache;
    private readonly ILogger<IgdbMetadataProvider> _logger;

    public IgdbMetadataProvider(
        IgdbClient client,
        ISettingsService settings,
        IMetadataCache cache,
        ILogger<IgdbMetadataProvider> logger)
    {
        _client = client;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public string ProviderKey => Key;

    public bool IsConfigured => _settings.Current.Igdb.HasCredentials;

    public async Task<MetadataResult<IReadOnlyList<MetadataSearchResult>>> SearchAsync(
        string searchTerm,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return MetadataResult<IReadOnlyList<MetadataSearchResult>>.Failure(MetadataErrorKind.NoResults);
        }

        if (!IsConfigured)
        {
            return MetadataResult<IReadOnlyList<MetadataSearchResult>>.Failure(MetadataErrorKind.NotConfigured);
        }

        var term = searchTerm.Trim();

        var cached = await _cache.GetSearchAsync(Key, term, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogInformation("Search for '{Term}' served from the local cache", term);
            return MetadataResult<IReadOnlyList<MetadataSearchResult>>.Success(cached);
        }

        var settings = _settings.Current.Igdb;

        IgdbCallResult call;
        try
        {
            call = await _client.SearchAsync(BuildCredentials(), term, settings.PlatformId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return MetadataResult<IReadOnlyList<MetadataSearchResult>>.Failure(MetadataErrorKind.Cancelled);
        }

        if (!call.IsSuccess)
        {
            return MetadataResult<IReadOnlyList<MetadataSearchResult>>.Failure(call.Error, call.Detail);
        }

        var results = call.Games!
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => new MetadataSearchResult
            {
                ProviderGameId = g.Id.ToString(CultureInfo.InvariantCulture),
                Title = g.Name!,
                ReleaseYear = YearFromUnix(g.FirstReleaseDate),
                Developer = PickCompany(g, developer: true),
                Publisher = PickCompany(g, developer: false),
                ThumbnailUrl = g.Cover?.ImageId is { Length: > 0 } id ? IgdbClient.BuildImageUrl(id, "t_cover_small") : null,
            })
            .ToList();

        await _cache.SetSearchAsync(Key, term, results, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Search for '{Term}' returned {Count} result(s)", term, results.Count);
        return MetadataResult<IReadOnlyList<MetadataSearchResult>>.Success(results);
    }

    public async Task<MetadataResult<GameMetadata>> GetGameAsync(
        string providerGameId,
        string languageCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(providerGameId) ||
            !long.TryParse(providerGameId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var gameId))
        {
            return MetadataResult<GameMetadata>.Failure(MetadataErrorKind.NoResults);
        }

        if (!IsConfigured)
        {
            return MetadataResult<GameMetadata>.Failure(MetadataErrorKind.NotConfigured);
        }

        const string cacheLanguage = "en";

        var cached = await _cache.GetAsync(Key, providerGameId, cacheLanguage, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogInformation("Metadata for game {Id} served from the local cache", providerGameId);
            return MetadataResult<GameMetadata>.Success(cached);
        }

        IgdbCallResult call;
        try
        {
            call = await _client.GetGameAsync(BuildCredentials(), gameId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return MetadataResult<GameMetadata>.Failure(MetadataErrorKind.Cancelled);
        }

        if (!call.IsSuccess)
        {
            return MetadataResult<GameMetadata>.Failure(call.Error, call.Detail);
        }

        var game = call.Games!.FirstOrDefault();
        if (game is null)
        {
            return MetadataResult<GameMetadata>.Failure(MetadataErrorKind.NoResults);
        }

        var metadata = MapGame(game, providerGameId, _settings.Current.Igdb.MaxScreenshots);
        await _cache.SetAsync(Key, providerGameId, cacheLanguage, metadata, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Fetched metadata for '{Title}' ({Id}): {GenreCount} genre(s), cover={HasCover}, {ScreenshotCount} screenshot(s)",
            metadata.Title,
            providerGameId,
            metadata.Genres.Count,
            metadata.Cover is not null,
            metadata.Screenshots.Count);

        return MetadataResult<GameMetadata>.Success(metadata);
    }

    public async Task<MetadataResult<string>> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return MetadataResult<string>.Failure(MetadataErrorKind.NotConfigured);
        }

        // A minimal search validates that the token can be obtained and the credentials work.
        IgdbCallResult call;
        try
        {
            call = await _client.GetGameAsync(BuildCredentials(), 1, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return MetadataResult<string>.Failure(MetadataErrorKind.Cancelled);
        }

        if (call.IsSuccess || call.Error == MetadataErrorKind.NoResults)
        {
            return MetadataResult<string>.Success("ok");
        }

        return MetadataResult<string>.Failure(call.Error, call.Detail);
    }

    // ---- mapping --------------------------------------------------------------------------

    internal static GameMetadata MapGame(IgdbGame game, string providerGameId, int maxScreenshots)
    {
        var rawGenres = (game.Genres ?? new List<IgdbGenre>())
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => g.Name!)
            .ToList();

        var screenshots = new List<MetadataMedia>();
        foreach (var shot in game.Screenshots ?? new List<IgdbImage>())
        {
            if (screenshots.Count >= Math.Max(0, maxScreenshots))
            {
                break;
            }

            if (shot.ImageId is { Length: > 0 } imageId)
            {
                screenshots.Add(new MetadataMedia
                {
                    Kind = MediaKind.Screenshot,
                    Url = IgdbClient.BuildImageUrl(imageId, ScreenshotSize),
                    Format = "jpg",
                });
            }
        }

        MetadataMedia? cover = game.Cover?.ImageId is { Length: > 0 } coverId
            ? new MetadataMedia { Kind = MediaKind.Cover, Url = IgdbClient.BuildImageUrl(coverId, CoverSize), Format = "jpg" }
            : null;

        return new GameMetadata
        {
            ProviderGameId = providerGameId,
            Title = game.Name ?? providerGameId,
            AlternateTitles = System.Array.Empty<string>(),
            Description = Clean(game.Summary),
            Publisher = PickCompany(game, developer: false),
            Developer = PickCompany(game, developer: true),
            ReleaseYear = YearFromUnix(game.FirstReleaseDate),
            Players = null,
            SystemName = game.Platforms?.FirstOrDefault()?.Name,
            Genres = GenreMapper.MapAll(rawGenres),
            RawGenres = rawGenres,
            Cover = cover,
            Screenshots = screenshots,
        };
    }

    private static string? PickCompany(IgdbGame game, bool developer)
    {
        var match = game.InvolvedCompanies?
            .FirstOrDefault(c => (developer ? c.Developer : c.Publisher) && c.Company?.Name is not null);

        return Clean(match?.Company?.Name);
    }

    private static int? YearFromUnix(long? seconds)
    {
        if (!seconds.HasValue || seconds.Value <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds.Value).Year;
    }

    private static string? Clean(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private IgdbCredentials BuildCredentials()
    {
        var settings = _settings.Current.Igdb;
        return new IgdbCredentials
        {
            ClientId = settings.ClientId ?? string.Empty,
            ClientSecret = settings.ClientSecret ?? string.Empty,
        };
    }
}
