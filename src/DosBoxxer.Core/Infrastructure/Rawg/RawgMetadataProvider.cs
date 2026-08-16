using System.Globalization;
using System.Text.RegularExpressions;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Infrastructure.Rawg.Dto;
using DosBoxxer.Core.Infrastructure.ScreenScraper;
using DosBoxxer.Core.Models.Metadata;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Rawg;

/// <summary>
/// Adapts RAWG to the launcher's <see cref="IGameMetadataProvider"/> contract.
///
/// RAWG's search response carries the genres, a background image and a few short screenshots;
/// the description, developers and publishers come from the detail endpoint. A game lookup
/// therefore makes one detail call and reuses the media from the cached search hit. Descriptions
/// are plain-text English, so the requested language is ignored for them.
/// </summary>
public sealed partial class RawgMetadataProvider : IGameMetadataProvider
{
    public const string Key = "rawg";

    private readonly RawgClient _client;
    private readonly ISettingsService _settings;
    private readonly IMetadataCache _cache;
    private readonly ILogger<RawgMetadataProvider> _logger;

    public RawgMetadataProvider(
        RawgClient client,
        ISettingsService settings,
        IMetadataCache cache,
        ILogger<RawgMetadataProvider> logger)
    {
        _client = client;
        _settings = settings;
        _cache = cache;
        _logger = logger;
    }

    public string ProviderKey => Key;

    public bool IsConfigured => _settings.Current.Rawg.HasApiKey;

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

        var settings = _settings.Current.Rawg;

        RawgCallResult<RawgGamesResponse> call;
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

        var games = call.Value!.Results ?? new List<RawgGame>();
        if (games.Count == 0)
        {
            return MetadataResult<IReadOnlyList<MetadataSearchResult>>.Failure(MetadataErrorKind.NoResults);
        }

        var results = games
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => new MetadataSearchResult
            {
                ProviderGameId = g.Id.ToString(CultureInfo.InvariantCulture),
                Title = g.Name!,
                ReleaseYear = ParseYear(g.Released),
                SystemName = g.Platforms?.FirstOrDefault()?.Platform?.Name,
                ThumbnailUrl = RawgClient.TryValidateMediaUrl(g.BackgroundImage, out _) ? g.BackgroundImage : null,
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

        RawgCallResult<RawgGame> call;
        try
        {
            call = await _client.GetGameAsync(_settings.Current.Rawg.ApiKey!, gameId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return MetadataResult<GameMetadata>.Failure(MetadataErrorKind.Cancelled);
        }

        if (!call.IsSuccess)
        {
            return MetadataResult<GameMetadata>.Failure(call.Error, call.Detail);
        }

        var metadata = MapGame(call.Value!, providerGameId, _settings.Current.Rawg.MaxScreenshots);
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

        // A tiny search validates the API key without needing a specific game id.
        RawgCallResult<RawgGamesResponse> call;
        try
        {
            call = await _client.SearchAsync(_settings.Current.Rawg.ApiKey!, "doom", null, cancellationToken).ConfigureAwait(false);
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

    internal static GameMetadata MapGame(RawgGame game, string providerGameId, int maxScreenshots)
    {
        var rawGenres = (game.Genres ?? new List<RawgNamed>())
            .Where(g => !string.IsNullOrWhiteSpace(g.Name))
            .Select(g => g.Name!)
            .ToList();

        MetadataMedia? cover = RawgClient.TryValidateMediaUrl(game.BackgroundImage, out _)
            ? new MetadataMedia { Kind = MediaKind.Cover, Url = game.BackgroundImage!, Format = ExtensionOf(game.BackgroundImage!) }
            : null;

        var screenshots = new List<MetadataMedia>();
        foreach (var shot in game.ShortScreenshots ?? new List<RawgScreenshot>())
        {
            if (screenshots.Count >= Math.Max(0, maxScreenshots))
            {
                break;
            }

            if (string.IsNullOrWhiteSpace(shot.Image) || !RawgClient.TryValidateMediaUrl(shot.Image, out _))
            {
                continue;
            }

            // RAWG includes the background image as the first "short screenshot" (id -1); skip it
            // so the cover is not duplicated in the gallery.
            if (shot.Id < 0 || string.Equals(shot.Image, game.BackgroundImage, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            screenshots.Add(new MetadataMedia
            {
                Kind = MediaKind.Screenshot,
                Url = shot.Image!,
                Format = ExtensionOf(shot.Image!),
            });
        }

        return new GameMetadata
        {
            ProviderGameId = providerGameId,
            Title = game.Name ?? providerGameId,
            AlternateTitles = System.Array.Empty<string>(),
            Description = Clean(game.DescriptionRaw),
            Publisher = Clean(game.Publishers?.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Name))?.Name),
            Developer = Clean(game.Developers?.FirstOrDefault(d => !string.IsNullOrWhiteSpace(d.Name))?.Name),
            ReleaseYear = ParseYear(game.Released),
            Players = null,
            SystemName = game.Platforms?.FirstOrDefault()?.Platform?.Name,
            Genres = GenreMapper.MapAll(rawGenres),
            RawGenres = rawGenres,
            Cover = cover,
            Screenshots = screenshots,
        };
    }

    private static int? ParseYear(string? released)
    {
        if (string.IsNullOrWhiteSpace(released))
        {
            return null;
        }

        var match = YearRegex().Match(released);
        return match.Success && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year)
            ? year
            : null;
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
