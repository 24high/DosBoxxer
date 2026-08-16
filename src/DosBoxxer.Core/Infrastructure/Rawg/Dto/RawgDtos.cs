using System.Text.Json.Serialization;

namespace DosBoxxer.Core.Infrastructure.Rawg.Dto;

/// <summary>
/// DTOs for the RAWG API (<c>https://api.rawg.io/api/</c>, documented at
/// <c>https://api.rawg.io/docs/</c>).
///
/// The search endpoint (<c>/games</c>) returns a paged list with a light record per game; the
/// full description, developers and publishers come from the detail endpoint
/// (<c>/games/{id}</c>). These types only parse the wire format.
/// </summary>
public sealed class RawgGamesResponse
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("results")]
    public List<RawgGame>? Results { get; set; }
}

public sealed class RawgGame
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Release date as <c>yyyy-MM-dd</c>.</summary>
    [JsonPropertyName("released")]
    public string? Released { get; set; }

    [JsonPropertyName("background_image")]
    public string? BackgroundImage { get; set; }

    [JsonPropertyName("genres")]
    public List<RawgNamed>? Genres { get; set; }

    [JsonPropertyName("short_screenshots")]
    public List<RawgScreenshot>? ShortScreenshots { get; set; }

    [JsonPropertyName("platforms")]
    public List<RawgPlatformWrapper>? Platforms { get; set; }

    // -- detail-only fields ---------------------------------------------------------
    [JsonPropertyName("description_raw")]
    public string? DescriptionRaw { get; set; }

    [JsonPropertyName("developers")]
    public List<RawgNamed>? Developers { get; set; }

    [JsonPropertyName("publishers")]
    public List<RawgNamed>? Publishers { get; set; }
}

public sealed class RawgNamed
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class RawgScreenshot
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }
}

public sealed class RawgPlatformWrapper
{
    [JsonPropertyName("platform")]
    public RawgNamed? Platform { get; set; }
}

/// <summary>Error envelope, e.g. <c>{"error":"Invalid API key."}</c> or <c>{"detail":"..."}</c>.</summary>
public sealed class RawgError
{
    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }
}
