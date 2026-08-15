using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Infrastructure.MobyGames.Dto;
using DosBoxxer.Core.Models.Metadata;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.MobyGames;

/// <summary>
/// Adapts the MobyGames API to the launcher's <see cref="IGameMetadataProvider"/> contract.
///
/// Because MobyGames is rate limited to one request every ten seconds, this provider keeps the
/// number of calls low: the search response already carries the cover and screenshots, so a
/// game lookup needs at most two requests (the full game record plus one platform-detail call
/// for developer / publisher / players).
///
/// MobyGames descriptions are English HTML; they are stripped to plain text and the requested
/// language is ignored for them (there is no per-language text in the API).
/// </summary>
public sealed partial class MobyGamesMetadataProvider : IGameMetadataProvider
{
    public const string Key = "mobygames";

    private readonly MobyGamesClient _client;
    private readonly ISettingsService _settings;
    private readonly IMetadataCache _cache;
    private readonly ILogger<MobyGamesMetadataProvider> _logger;

    public MobyGamesMetadataProvider(
        MobyGamesClient client,
        ISettingsService settings,
        IMetadataCache cache,
        ILogger<MobyGamesMetadataProvider> logger)
    {
        _client = client;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public string ProviderKey => Key;

    public bool IsConfigured => _settings.Current.MobyGames.HasApiKey;

    [GeneratedRegex(@"(19|20)\d{2}")]
    private static partial Regex YearRegex();

    [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRegex();

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

        var settings = _settings.Current.MobyGames;

        MobyGamesCallResult<MobyGamesGamesResponse> call;
        try
        {
            call = await _client.SearchAsync(settings.ApiKey!, term, settings.PlatformId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return MetadataResult<IReadOnlyList<MetadataSearchResult>>.Failure(MetadataErrorKind.Cancelled);
        }

        if (!call.IsSuccess)
        {
            return MetadataResult<IReadOnlyList<MetadataSearchResult>>.Failure(call.Error, call.Detail);
        }

        var games = call.Value!.Games ?? new List<MobyGame>();
        if (games.Count == 0)
        {
            return MetadataResult<IReadOnlyList<MetadataSearchResult>>.Failure(MetadataErrorKind.NoResults);
        }

        var results = games
            .Where(g => !string.IsNullOrWhiteSpace(g.Title))
            .Select(g => new MetadataSearchResult
            {
                ProviderGameId = g.GameId.ToString(CultureInfo.InvariantCulture),
                Title = g.Title!,
                ReleaseYear = PickYear(g, settings.PlatformId),
                SystemName = PickPlatformName(g, settings.PlatformId),
                ThumbnailUrl = g.SampleCover?.ThumbnailImage ?? g.SampleCover?.Image,
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

        // MobyGames has no per-language text, so everything is cached under a single language.
        const string cacheLanguage = "en";

        var cached = await _cache.GetAsync(Key, providerGameId, cacheLanguage, cancellationToken).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogInformation("Metadata for game {Id} served from the local cache", providerGameId);
            return MetadataResult<GameMetadata>.Success(cached);
        }

        var settings = _settings.Current.MobyGames;

        MobyGamesCallResult<MobyGamesGamesResponse> gameCall;
        MobyGamesCallResult<MobyPlatformDetail> detailCall;
        try
        {
            gameCall = await _client.GetGameAsync(settings.ApiKey!, gameId, cancellationToken).ConfigureAwait(false);
            if (!gameCall.IsSuccess)
            {
                return MetadataResult<GameMetadata>.Failure(gameCall.Error, gameCall.Detail);
            }

            // Developer / publisher / players live on the platform release, not the game record.
            detailCall = await _client
                .GetPlatformDetailAsync(settings.ApiKey!, gameId, settings.PlatformId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return MetadataResult<GameMetadata>.Failure(MetadataErrorKind.Cancelled);
        }

        var game = gameCall.Value!.Games?.FirstOrDefault();
        if (game is null)
        {
            return MetadataResult<GameMetadata>.Failure(MetadataErrorKind.NoResults);
        }

        // The detail call is best-effort: a game without a DOS release still yields a usable
        // record from the game data alone.
        var detail = detailCall.IsSuccess ? detailCall.Value : null;

        var metadata = MapGame(game, detail, providerGameId, settings.PlatformId, settings.MaxScreenshots);
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

        // A lookup for a stable game id validates the key: success or "no results" both prove the
        // key was accepted, whereas a bad key comes back as InvalidCredentials.
        MobyGamesCallResult<MobyGamesGamesResponse> call;
        try
        {
            call = await _client.GetGameAsync(_settings.Current.MobyGames.ApiKey!, 1, cancellationToken).ConfigureAwait(false);
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

    internal static GameMetadata MapGame(
        MobyGame game,
        MobyPlatformDetail? detail,
        string providerGameId,
        int platformId,
        int maxScreenshots)
    {
        var genres = MobyGamesGenreMapper.MapAll(game.Genres);
        var rawGenres = (game.Genres ?? new List<MobyGenre>())
            .Where(g => !string.IsNullOrWhiteSpace(g.GenreName))
            .Select(g => g.GenreName!)
            .ToList();

        var (developer, publisher) = PickCompanies(detail);

        var cover = BuildCover(game.SampleCover);
        var screenshots = BuildScreenshots(game.SampleScreenshots, maxScreenshots);

        var year = PickYear(game, platformId)
                   ?? ParseYear(detail?.FirstReleaseDate)
                   ?? ParseYear(detail?.Releases?.Select(r => r.ReleaseDate).FirstOrDefault(d => d is not null));

        return new GameMetadata
        {
            ProviderGameId = providerGameId,
            Title = game.Title ?? providerGameId,
            AlternateTitles = System.Array.Empty<string>(),
            Description = StripHtml(game.Description),
            Publisher = publisher,
            Developer = developer,
            ReleaseYear = year,
            Players = PickPlayers(detail),
            SystemName = PickPlatformName(game, platformId) ?? detail?.PlatformName,
            Genres = genres,
            RawGenres = rawGenres,
            Cover = cover,
            Screenshots = screenshots,
        };
    }

    private static (string? Developer, string? Publisher) PickCompanies(MobyPlatformDetail? detail)
    {
        if (detail?.Releases is null)
        {
            return (null, null);
        }

        // Prefer a US release, then the first release with company data.
        var release = detail.Releases.FirstOrDefault(r =>
                          r.Countries is not null &&
                          r.Countries.Any(c => c.Contains("United States", StringComparison.OrdinalIgnoreCase)) &&
                          r.Companies is { Count: > 0 })
                      ?? detail.Releases.FirstOrDefault(r => r.Companies is { Count: > 0 });

        if (release?.Companies is null)
        {
            return (null, null);
        }

        var developer = release.Companies
            .FirstOrDefault(c => c.Role is not null && c.Role.Contains("Develop", StringComparison.OrdinalIgnoreCase))
            ?.CompanyName;

        var publisher = release.Companies
            .FirstOrDefault(c => c.Role is not null && c.Role.Contains("Publish", StringComparison.OrdinalIgnoreCase))
            ?.CompanyName;

        return (Clean(developer), Clean(publisher));
    }

    private static string? PickPlayers(MobyPlatformDetail? detail)
    {
        var players = detail?.Attributes?
            .FirstOrDefault(a => a.AttributeCategoryName is not null &&
                                 a.AttributeCategoryName.Contains("Number of Players", StringComparison.OrdinalIgnoreCase))
            ?.AttributeName;

        return Clean(players);
    }

    private static MetadataMedia? BuildCover(MobyCover? cover)
    {
        var url = cover?.Image ?? cover?.ThumbnailImage;
        if (string.IsNullOrWhiteSpace(url) || !MobyGamesClient.TryValidateMediaUrl(url, out _))
        {
            return null;
        }

        return new MetadataMedia
        {
            Kind = MediaKind.Cover,
            Url = url,
            Format = ExtensionOf(url),
        };
    }

    private static IReadOnlyList<MetadataMedia> BuildScreenshots(List<MobyScreenshot>? screenshots, int maxScreenshots)
    {
        if (screenshots is null || maxScreenshots <= 0)
        {
            return System.Array.Empty<MetadataMedia>();
        }

        var result = new List<MetadataMedia>();

        foreach (var shot in screenshots)
        {
            if (result.Count >= maxScreenshots)
            {
                break;
            }

            var url = shot.Image ?? shot.ThumbnailImage;
            if (string.IsNullOrWhiteSpace(url) || !MobyGamesClient.TryValidateMediaUrl(url, out _))
            {
                continue;
            }

            if (result.Any(s => string.Equals(s.Url, url, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            result.Add(new MetadataMedia
            {
                Kind = MediaKind.Screenshot,
                Url = url,
                Format = ExtensionOf(url),
            });
        }

        return result;
    }

    internal static int? PickYear(MobyGame game, int platformId)
    {
        var platform = game.Platforms?.FirstOrDefault(p => p.PlatformId == platformId)
                       ?? game.Platforms?.FirstOrDefault();
        return ParseYear(platform?.FirstReleaseDate);
    }

    private static string? PickPlatformName(MobyGame game, int platformId)
    {
        var platform = game.Platforms?.FirstOrDefault(p => p.PlatformId == platformId);
        return Clean(platform?.PlatformName);
    }

    private static int? ParseYear(string? date)
    {
        if (string.IsNullOrWhiteSpace(date))
        {
            return null;
        }

        var match = YearRegex().Match(date);
        return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
    }

    /// <summary>Strips HTML tags and decodes entities so a description reads as plain text.</summary>
    internal static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        // Turn block boundaries into line breaks before removing the tags.
        var withBreaks = html
            .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase);

        var stripped = HtmlTagRegex().Replace(withBreaks, string.Empty);
        var decoded = WebUtility.HtmlDecode(stripped);

        var builder = new StringBuilder(decoded.Length);
        foreach (var line in decoded.Split('\n'))
        {
            var trimmed = WhitespaceRegex().Replace(line, " ").Trim();
            builder.Append(trimmed).Append('\n');
        }

        var result = builder.ToString().Trim();
        return result.Length == 0 ? null : result;
    }

    private static string? ExtensionOf(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return null;
        }

        var extension = System.IO.Path.GetExtension(uri.AbsolutePath).TrimStart('.').ToLowerInvariant();
        return string.IsNullOrEmpty(extension) ? null : extension;
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
}
