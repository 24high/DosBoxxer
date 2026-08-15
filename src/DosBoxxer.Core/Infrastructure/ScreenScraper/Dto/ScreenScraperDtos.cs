using System.Text.Json.Serialization;

namespace DosBoxxer.Core.Infrastructure.ScreenScraper.Dto;

/// <summary>
/// Data transfer objects that mirror the ScreenScraper API v2 JSON payloads
/// (<c>https://api.screenscraper.fr/api2/</c>, documented at
/// <c>https://api.screenscraper.fr/webapi2.php</c>).
///
/// These types exist only to parse the wire format. They are never used by the UI and never
/// stored — <see cref="ScreenScraperMetadataProvider"/> maps them onto the launcher's own
/// <c>GameMetadata</c> model.
///
/// Every property is nullable on purpose: the service omits fields it has no data for.
/// </summary>
public sealed class SsEnvelope
{
    [JsonPropertyName("header")]
    public SsHeader? Header { get; set; }

    [JsonPropertyName("response")]
    public SsResponse? Response { get; set; }
}

public sealed class SsHeader
{
    [JsonPropertyName("APIversion")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? ApiVersion { get; set; }

    [JsonPropertyName("commandRequested")]
    public string? CommandRequested { get; set; }

    [JsonPropertyName("success")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Success { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

public sealed class SsResponse
{
    /// <summary>Result list of <c>jeuRecherche.php</c>.</summary>
    [JsonPropertyName("jeux")]
    [JsonConverter(typeof(FlexibleListConverter<SsGame>))]
    public List<SsGame>? Games { get; set; }

    /// <summary>Single game returned by <c>jeuInfos.php</c>.</summary>
    [JsonPropertyName("jeu")]
    public SsGame? Game { get; set; }

    /// <summary>User block returned by <c>ssuserInfos.php</c>.</summary>
    [JsonPropertyName("ssuser")]
    public SsUser? User { get; set; }
}

public sealed class SsUser
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Id { get; set; }

    [JsonPropertyName("niveau")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Level { get; set; }

    [JsonPropertyName("maxrequestsperday")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? MaxRequestsPerDay { get; set; }

    [JsonPropertyName("requeststoday")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? RequestsToday { get; set; }

    [JsonPropertyName("maxthreads")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? MaxThreads { get; set; }
}

public sealed class SsGame
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Id { get; set; }

    /// <summary>Localised/regional titles.</summary>
    [JsonPropertyName("noms")]
    [JsonConverter(typeof(FlexibleListConverter<SsRegionText>))]
    public List<SsRegionText>? Names { get; set; }

    [JsonPropertyName("systeme")]
    public SsIdText? System { get; set; }

    [JsonPropertyName("editeur")]
    public SsIdText? Publisher { get; set; }

    [JsonPropertyName("developpeur")]
    public SsIdText? Developer { get; set; }

    [JsonPropertyName("joueurs")]
    public SsTextValue? Players { get; set; }

    [JsonPropertyName("dates")]
    [JsonConverter(typeof(FlexibleListConverter<SsRegionText>))]
    public List<SsRegionText>? ReleaseDates { get; set; }

    [JsonPropertyName("genres")]
    [JsonConverter(typeof(FlexibleListConverter<SsGenre>))]
    public List<SsGenre>? Genres { get; set; }

    [JsonPropertyName("synopsis")]
    [JsonConverter(typeof(FlexibleListConverter<SsLanguageText>))]
    public List<SsLanguageText>? Synopsis { get; set; }

    [JsonPropertyName("medias")]
    [JsonConverter(typeof(FlexibleListConverter<SsMedia>))]
    public List<SsMedia>? Medias { get; set; }
}

public sealed class SsIdText
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Id { get; set; }

    [JsonPropertyName("text")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Text { get; set; }
}

public sealed class SsTextValue
{
    [JsonPropertyName("text")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Text { get; set; }
}

public sealed class SsRegionText
{
    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("text")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Text { get; set; }
}

public sealed class SsLanguageText
{
    [JsonPropertyName("langue")]
    public string? Language { get; set; }

    [JsonPropertyName("text")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Text { get; set; }
}

public sealed class SsGenre
{
    [JsonPropertyName("id")]
    [JsonConverter(typeof(FlexibleStringConverter))]
    public string? Id { get; set; }

    [JsonPropertyName("noms")]
    [JsonConverter(typeof(FlexibleListConverter<SsLanguageText>))]
    public List<SsLanguageText>? Names { get; set; }
}

public sealed class SsMedia
{
    /// <summary>Media type, e.g. <c>box-2D</c>, <c>ss</c>, <c>sstitle</c>, <c>wheel</c>, <c>fanart</c>.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("parent")]
    public string? Parent { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }
}
