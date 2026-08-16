using System.Text.Json.Serialization;

namespace DosBoxxer.Core.Infrastructure.Igdb.Dto;

/// <summary>
/// DTOs for the IGDB API v4 (<c>https://api.igdb.com/v4/</c>, documented at
/// <c>https://api-docs.igdb.com/</c>).
///
/// IGDB returns a bare JSON array of objects; the fields present depend on the Apicalypse
/// <c>fields</c> clause of the request. These types only parse the wire format and never reach
/// the UI. Everything is nullable because a field is absent unless requested and populated.
/// </summary>
public sealed class IgdbGame
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    /// <summary>Unix timestamp (seconds) of the earliest release date.</summary>
    [JsonPropertyName("first_release_date")]
    public long? FirstReleaseDate { get; set; }

    [JsonPropertyName("genres")]
    public List<IgdbGenre>? Genres { get; set; }

    [JsonPropertyName("cover")]
    public IgdbImage? Cover { get; set; }

    [JsonPropertyName("screenshots")]
    public List<IgdbImage>? Screenshots { get; set; }

    [JsonPropertyName("involved_companies")]
    public List<IgdbInvolvedCompany>? InvolvedCompanies { get; set; }

    [JsonPropertyName("platforms")]
    public List<IgdbPlatform>? Platforms { get; set; }
}

public sealed class IgdbGenre
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class IgdbImage
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    /// <summary>Opaque image id used to build a CDN URL, e.g. <c>co1n5f</c>.</summary>
    [JsonPropertyName("image_id")]
    public string? ImageId { get; set; }
}

public sealed class IgdbInvolvedCompany
{
    [JsonPropertyName("company")]
    public IgdbCompany? Company { get; set; }

    [JsonPropertyName("developer")]
    public bool Developer { get; set; }

    [JsonPropertyName("publisher")]
    public bool Publisher { get; set; }
}

public sealed class IgdbCompany
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed class IgdbPlatform
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>Twitch OAuth token response used to authenticate IGDB requests.</summary>
public sealed class IgdbTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("expires_in")]
    public long ExpiresIn { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }
}
