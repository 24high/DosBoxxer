using System.Globalization;
using System.Text.RegularExpressions;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Infrastructure.ScreenScraper.Dto;
using DosBoxxer.Core.Models.Metadata;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.ScreenScraper;

/// <summary>
/// Adapts the ScreenScraper API to the launcher's <see cref="IGameMetadataProvider"/> contract
/// and normalises everything (titles, regions, languages, genres, media) to the launcher model.
/// </summary>
public sealed partial class ScreenScraperMetadataProvider : IGameMetadataProvider
{
    public const string Key = "screenscraper";

    /// <summary>Media types considered a box cover, in order of preference.</summary>
    private static readonly string[] CoverTypes = { "box-2D", "box-3D", "box-texture" };

    /// <summary>Media types considered a screenshot, in order of preference.</summary>
    private static readonly string[] ScreenshotTypes = { "ss", "sstitle" };

    private readonly ScreenScraperClient _client;
    private readonly ISettingsService _settings;
    private readonly IMetadataCache _cache;
    private readonly ILogger<ScreenScraperMetadataProvider> _logger;

    public ScreenScraperMetadataProvider(
        ScreenScraperClient client,
        ISettingsService settings,
        IMetadataCache cache,
        ILogger<ScreenScraperMetadataProvider> logger)
    {
        _client = client;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public string ProviderKey => Key;

    /// <summary>
    /// The provider is considered ready whenever it can form a request at all, which only needs
    /// a software name (always defaulted). Developer access and a user account are optional and
    /// simply raise the quota — metadata lookups therefore work out of the box, anonymously.
    /// </summary>
    public bool IsConfigured => _settings.Current.ScreenScraper.CanQuery;

    [GeneratedRegex(@"(19|20)\d{2}")]
    private static partial Regex YearRegex();

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

        var settings = _settings.Current.ScreenScraper;

        ScreenScraperCallResult call;
        try
        {
            call = await _client
                .SearchAsync(BuildCredentials(), term, settings.SystemId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return MetadataResult<IReadOnlyList<MetadataSearchResult>>.Failure(MetadataErrorKind.Cancelled);
        }

        if (!call.IsSuccess)
        {
            return MetadataResult<IReadOnlyList<MetadataSearchResult>>.Failure(call.Error, call.Detail);
        }

        var games = call.Envelope?.Response?.Games ?? new List<SsGame>();
        if (games.Count == 0)
        {
            return MetadataResult<IReadOnlyList<MetadataSearchResult>>.Failure(MetadataErrorKind.NoResults);
        }

        var results = games
            .Where(g => !string.IsNullOrWhiteSpace(g.Id))
            .Select(g => new MetadataSearchResult
            {
                ProviderGameId = g.Id!,
                Title = PickTitle(g, settings.PreferredRegion) ?? term,
                ReleaseYear = PickYear(g, settings.PreferredRegion),
                Publisher = Clean(g.Publisher?.Text),
                Developer = Clean(g.Developer?.Text),
                SystemName = Clean(g.System?.Text),
                ThumbnailUrl = PickMedia(g, CoverTypes, settings.PreferredRegion)?.Url,
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
        if (string.IsNullOrWhiteSpace(providerGameId))
        {
            return MetadataResult<GameMetadata>.Failure(MetadataErrorKind.NoResults);
        }

        if (!IsConfigured)
        {
            return MetadataResult<GameMetadata>.Failure(MetadataErrorKind.NotConfigured);
        }

        var language = NormalizeLanguage(languageCode);

        var cached = await _cache.GetAsync(Key, providerGameId, language, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogInformation("Metadata for game {Id} served from the local cache", providerGameId);
            return MetadataResult<GameMetadata>.Success(cached);
        }

        var settings = _settings.Current.ScreenScraper;

        ScreenScraperCallResult call;
        try
        {
            call = await _client
                .GetGameAsync(BuildCredentials(), providerGameId, settings.SystemId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return MetadataResult<GameMetadata>.Failure(MetadataErrorKind.Cancelled);
        }

        if (!call.IsSuccess)
        {
            return MetadataResult<GameMetadata>.Failure(call.Error, call.Detail);
        }

        var game = call.Envelope?.Response?.Game;
        if (game is null)
        {
            return MetadataResult<GameMetadata>.Failure(MetadataErrorKind.NoResults);
        }

        var metadata = MapGame(game, providerGameId, language, settings.PreferredRegion, settings.MaxScreenshots);
        await _cache.SetAsync(Key, providerGameId, language, metadata, cancellationToken).ConfigureAwait(false);

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

        var credentials = BuildCredentials();

        // The account-info endpoint requires a user login, so it can only validate a configured
        // user account. When only developer credentials are present (guest scraping), validate
        // them against a scraping endpoint instead — otherwise the test would always fail even
        // though metadata lookups work fine.
        if (_settings.Current.ScreenScraper.HasUserAccount)
        {
            ScreenScraperCallResult userCall;
            try
            {
                userCall = await _client.GetUserInfoAsync(credentials, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return MetadataResult<string>.Failure(MetadataErrorKind.Cancelled);
            }

            if (!userCall.IsSuccess)
            {
                return MetadataResult<string>.Failure(userCall.Error, userCall.Detail);
            }

            var user = userCall.Envelope?.Response?.User;
            var description = user is null
                ? "ok"
                : $"{user.RequestsToday ?? "?"}/{user.MaxRequestsPerDay ?? "?"} requests today";

            return MetadataResult<string>.Success(description);
        }

        // Developer-only validation: any game lookup authenticates the application before it
        // looks the game up, so even a "no results" answer proves the credentials are accepted.
        // A wrong or missing devid comes back as InvalidCredentials instead.
        ScreenScraperCallResult probe;
        try
        {
            probe = await _client
                .GetGameAsync(credentials, ValidationGameId, _settings.Current.ScreenScraper.SystemId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return MetadataResult<string>.Failure(MetadataErrorKind.Cancelled);
        }

        if (probe.IsSuccess || probe.Error == MetadataErrorKind.NoResults)
        {
            return MetadataResult<string>.Success("ok (guest)");
        }

        return MetadataResult<string>.Failure(probe.Error, probe.Detail);
    }

    /// <summary>A stable game id used only to validate developer credentials.</summary>
    private const string ValidationGameId = "3";

    // ---- mapping --------------------------------------------------------------------------

    internal static GameMetadata MapGame(
        SsGame game,
        string providerGameId,
        string language,
        string preferredRegion,
        int maxScreenshots)
    {
        var titles = (game.Names ?? new List<SsRegionText>())
            .Select(n => Clean(n.Text))
            .Where(t => t is not null)
            .Select(t => t!)
            .ToList();

        var title = PickTitle(game, preferredRegion) ?? titles.FirstOrDefault() ?? providerGameId;

        var rawGenres = (game.Genres ?? new List<SsGenre>())
            .Select(g => PickLanguageText(g.Names, language))
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Select(g => g!)
            .ToList();

        var screenshots = new List<MetadataMedia>();
        foreach (var type in ScreenshotTypes)
        {
            foreach (var media in EnumerateMedia(game, type, preferredRegion))
            {
                if (screenshots.Count >= Math.Max(0, maxScreenshots))
                {
                    break;
                }

                if (screenshots.Any(s => string.Equals(s.Url, media.Url, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                screenshots.Add(media);
            }
        }

        return new GameMetadata
        {
            ProviderGameId = providerGameId,
            Title = title,
            AlternateTitles = titles.Where(t => !string.Equals(t, title, StringComparison.OrdinalIgnoreCase)).Distinct().ToList(),
            Description = PickLanguageText(game.Synopsis, language),
            Publisher = Clean(game.Publisher?.Text),
            Developer = Clean(game.Developer?.Text),
            ReleaseYear = PickYear(game, preferredRegion),
            Players = Clean(game.Players?.Text),
            SystemName = Clean(game.System?.Text),
            Genres = GenreMapper.MapAll(rawGenres),
            RawGenres = rawGenres,
            Cover = PickMedia(game, CoverTypes, preferredRegion),
            Screenshots = screenshots,
        };
    }

    /// <summary>
    /// Language fallback required by the specification:
    /// 1. currently selected application language, 2. English, 3. any available text.
    /// </summary>
    internal static string? PickLanguageText(IReadOnlyList<SsLanguageText>? entries, string language)
    {
        if (entries is null || entries.Count == 0)
        {
            return null;
        }

        var exact = entries.FirstOrDefault(e =>
            string.Equals(e.Language, language, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(e.Text));

        if (exact is not null)
        {
            return Clean(exact.Text);
        }

        var english = entries.FirstOrDefault(e =>
            string.Equals(e.Language, "en", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(e.Text));

        if (english is not null)
        {
            return Clean(english.Text);
        }

        return Clean(entries.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Text))?.Text);
    }

    /// <summary>Region fallback: preferred region, then world/us/eu/jp, then anything.</summary>
    internal static string? PickRegionText(IReadOnlyList<SsRegionText>? entries, string preferredRegion)
    {
        if (entries is null || entries.Count == 0)
        {
            return null;
        }

        foreach (var region in RegionPreference(preferredRegion))
        {
            var match = entries.FirstOrDefault(e =>
                string.Equals(e.Region, region, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(e.Text));

            if (match is not null)
            {
                return Clean(match.Text);
            }
        }

        return Clean(entries.FirstOrDefault(e => !string.IsNullOrWhiteSpace(e.Text))?.Text);
    }

    internal static string? PickTitle(SsGame game, string preferredRegion) => PickRegionText(game.Names, preferredRegion);

    internal static int? PickYear(SsGame game, string preferredRegion)
    {
        var text = PickRegionText(game.ReleaseDates, preferredRegion);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = YearRegex().Match(text);
        if (!match.Success)
        {
            return null;
        }

        return int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
    }

    internal static MetadataMedia? PickMedia(SsGame game, IReadOnlyList<string> types, string preferredRegion)
    {
        foreach (var type in types)
        {
            var media = EnumerateMedia(game, type, preferredRegion).FirstOrDefault();
            if (media is not null)
            {
                return media;
            }
        }

        return null;
    }

    private static IEnumerable<MetadataMedia> EnumerateMedia(SsGame game, string type, string preferredRegion)
    {
        if (game.Medias is null)
        {
            yield break;
        }

        var candidates = game.Medias
            .Where(m =>
                string.Equals(m.Type, type, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(m.Url) &&
                ScreenScraperClient.TryValidateMediaUrl(m.Url, out _))
            .ToList();

        if (candidates.Count == 0)
        {
            yield break;
        }

        var kind = ScreenshotTypes.Contains(type, StringComparer.OrdinalIgnoreCase)
            ? MediaKind.Screenshot
            : MediaKind.Cover;

        var ordered = new List<SsMedia>();

        foreach (var region in RegionPreference(preferredRegion))
        {
            ordered.AddRange(candidates.Where(c =>
                string.Equals(c.Region, region, StringComparison.OrdinalIgnoreCase) && !ordered.Contains(c)));
        }

        ordered.AddRange(candidates.Where(c => !ordered.Contains(c)));

        foreach (var media in ordered)
        {
            yield return new MetadataMedia
            {
                Kind = kind,
                Url = media.Url!,
                Format = Clean(media.Format),
                Region = Clean(media.Region),
            };
        }
    }

    private static IEnumerable<string> RegionPreference(string preferredRegion)
    {
        if (!string.IsNullOrWhiteSpace(preferredRegion))
        {
            yield return preferredRegion;
        }

        foreach (var region in new[] { "wor", "us", "eu", "ss", "jp" })
        {
            if (!string.Equals(region, preferredRegion, StringComparison.OrdinalIgnoreCase))
            {
                yield return region;
            }
        }
    }

    /// <summary>
    /// ScreenScraper identifies languages with two letter codes. Application languages such as
    /// <c>zh-Hans</c> are reduced to their primary subtag; if the service has no text in that
    /// language, <see cref="PickLanguageText"/> falls back to English.
    /// </summary>
    internal static string NormalizeLanguage(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return "en";
        }

        var primary = languageCode.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(primary) ? "en" : primary.ToLowerInvariant();
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

    private ScreenScraperCredentials BuildCredentials()
    {
        var settings = _settings.Current.ScreenScraper;

        return new ScreenScraperCredentials
        {
            DeveloperId = settings.DeveloperId ?? string.Empty,
            DeveloperPassword = settings.DeveloperPassword ?? string.Empty,
            SoftwareName = settings.SoftwareName,
            UserName = settings.UserName,
            UserPassword = settings.UserPassword,
        };
    }
}
