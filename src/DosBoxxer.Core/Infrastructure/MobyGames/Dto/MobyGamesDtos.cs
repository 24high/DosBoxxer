using System.Text.Json.Serialization;

namespace DosBoxxer.Core.Infrastructure.MobyGames.Dto;

/// <summary>
/// DTOs that mirror the MobyGames API v1 JSON payloads
/// (<c>https://api.mobygames.com/v1/</c>, documented at
/// <c>https://www.mobygames.com/info/api/</c>).
///
/// These types only parse the wire format; they are never stored and never reach the UI.
/// <see cref="MobyGames.MobyGamesMetadataProvider"/> maps them onto the launcher's own
/// <c>GameMetadata</c> model. Every property is nullable because the API documents that
/// "some properties might be null, and lists might be empty".
/// </summary>
public sealed class MobyGamesGamesResponse
{
    [JsonPropertyName("games")]
    public List<MobyGame>? Games { get; set; }
}

public sealed class MobyGame
{
    [JsonPropertyName("game_id")]
    public long GameId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("moby_url")]
    public string? MobyUrl { get; set; }

    [JsonPropertyName("official_url")]
    public string? OfficialUrl { get; set; }

    [JsonPropertyName("genres")]
    public List<MobyGenre>? Genres { get; set; }

    [JsonPropertyName("platforms")]
    public List<MobyPlatformRelease>? Platforms { get; set; }

    [JsonPropertyName("sample_cover")]
    public MobyCover? SampleCover { get; set; }

    [JsonPropertyName("sample_screenshots")]
    public List<MobyScreenshot>? SampleScreenshots { get; set; }
}

public sealed class MobyGenre
{
    [JsonPropertyName("genre_category")]
    public string? GenreCategory { get; set; }

    [JsonPropertyName("genre_category_id")]
    public int GenreCategoryId { get; set; }

    [JsonPropertyName("genre_id")]
    public int GenreId { get; set; }

    [JsonPropertyName("genre_name")]
    public string? GenreName { get; set; }
}

/// <summary>Abbreviated release info that appears inside the <c>/games</c> response.</summary>
public sealed class MobyPlatformRelease
{
    [JsonPropertyName("platform_id")]
    public int PlatformId { get; set; }

    [JsonPropertyName("platform_name")]
    public string? PlatformName { get; set; }

    /// <summary>Free-form date: may be <c>1993</c>, <c>1993-12</c> or <c>1993-12-10</c>.</summary>
    [JsonPropertyName("first_release_date")]
    public string? FirstReleaseDate { get; set; }
}

public sealed class MobyCover
{
    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("thumbnail_image")]
    public string? ThumbnailImage { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }

    [JsonPropertyName("platforms")]
    public List<string>? Platforms { get; set; }
}

public sealed class MobyScreenshot
{
    [JsonPropertyName("caption")]
    public string? Caption { get; set; }

    [JsonPropertyName("image")]
    public string? Image { get; set; }

    [JsonPropertyName("thumbnail_image")]
    public string? ThumbnailImage { get; set; }

    [JsonPropertyName("width")]
    public int? Width { get; set; }

    [JsonPropertyName("height")]
    public int? Height { get; set; }
}

/// <summary>Response of <c>/games/{game_id}/platforms/{platform_id}</c>.</summary>
public sealed class MobyPlatformDetail
{
    [JsonPropertyName("game_id")]
    public long GameId { get; set; }

    [JsonPropertyName("platform_id")]
    public int PlatformId { get; set; }

    [JsonPropertyName("platform_name")]
    public string? PlatformName { get; set; }

    [JsonPropertyName("first_release_date")]
    public string? FirstReleaseDate { get; set; }

    [JsonPropertyName("releases")]
    public List<MobyRelease>? Releases { get; set; }

    [JsonPropertyName("attributes")]
    public List<MobyAttribute>? Attributes { get; set; }
}

public sealed class MobyRelease
{
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    [JsonPropertyName("countries")]
    public List<string>? Countries { get; set; }

    [JsonPropertyName("companies")]
    public List<MobyCompany>? Companies { get; set; }
}

public sealed class MobyCompany
{
    [JsonPropertyName("company_id")]
    public long CompanyId { get; set; }

    [JsonPropertyName("company_name")]
    public string? CompanyName { get; set; }

    /// <summary>Role such as <c>Developed by</c> or <c>Published by</c>.</summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }
}

public sealed class MobyAttribute
{
    [JsonPropertyName("attribute_category_id")]
    public int AttributeCategoryId { get; set; }

    [JsonPropertyName("attribute_category_name")]
    public string? AttributeCategoryName { get; set; }

    [JsonPropertyName("attribute_id")]
    public int AttributeId { get; set; }

    [JsonPropertyName("attribute_name")]
    public string? AttributeName { get; set; }
}

/// <summary>Error envelope, e.g. <c>{"code":401,"error":"...","message":"..."}</c>.</summary>
public sealed class MobyError
{
    [JsonPropertyName("code")]
    public int Code { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }
}
