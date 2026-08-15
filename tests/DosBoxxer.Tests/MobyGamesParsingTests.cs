using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using DosBoxxer.Core.Infrastructure.MobyGames;
using DosBoxxer.Core.Infrastructure.MobyGames.Dto;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Metadata;
using Xunit;

namespace DosBoxxer.Tests;

/// <summary>
/// Parsing and mapping tests for the MobyGames provider. They run entirely against recorded
/// fixtures — no API key and no network access are required.
/// </summary>
public sealed class MobyGamesParsingTests
{
    private const int DosPlatformId = 2;

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static T Load<T>(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Options);
        Assert.NotNull(value);
        return value!;
    }

    private static MobyGame FirstGame() => Load<MobyGamesGamesResponse>("moby-games-search.json").Games!.First();

    private static MobyPlatformDetail Detail() => Load<MobyPlatformDetail>("moby-platform-detail.json");

    [Fact]
    public void SearchResponse_IsParsed()
    {
        var response = Load<MobyGamesGamesResponse>("moby-games-search.json");

        Assert.Equal(2, response.Games!.Count);
        Assert.Equal(1069, response.Games[0].GameId);
        Assert.Equal("Doom", response.Games[0].Title);
        Assert.Equal("The Ultimate Doom", response.Games[1].Title);
    }

    [Fact]
    public void PickYear_UsesTheConfiguredPlatformRelease()
    {
        var game = FirstGame();

        // DOS release is 1993 even though the Windows port is 1995.
        Assert.Equal(1993, MobyGamesMetadataProvider.PickYear(game, DosPlatformId));
        Assert.Equal(1995, MobyGamesMetadataProvider.PickYear(game, 3));
    }

    [Fact]
    public void MapGame_ProducesTheDomainModel()
    {
        var metadata = MobyGamesMetadataProvider.MapGame(FirstGame(), Detail(), "1069", DosPlatformId, 6);

        Assert.Equal("1069", metadata.ProviderGameId);
        Assert.Equal("Doom", metadata.Title);
        Assert.Equal(1993, metadata.ReleaseYear);
        Assert.Equal("DOS", metadata.SystemName);
        Assert.Equal("1-4 Players", metadata.Players);
    }

    [Fact]
    public void MapGame_StripsHtmlFromTheDescription()
    {
        var metadata = MobyGamesMetadataProvider.MapGame(FirstGame(), Detail(), "1069", DosPlatformId, 6);

        Assert.NotNull(metadata.Description);
        Assert.DoesNotContain("<p>", metadata.Description);
        Assert.DoesNotContain("</p>", metadata.Description);
        Assert.StartsWith("You are a space marine", metadata.Description);
        Assert.Contains("first-person shooter", metadata.Description);
    }

    [Fact]
    public void MapGame_PicksDeveloperAndPublisherFromTheUsRelease()
    {
        var metadata = MobyGamesMetadataProvider.MapGame(FirstGame(), Detail(), "1069", DosPlatformId, 6);

        Assert.Equal("id Software, Inc.", metadata.Developer);
        Assert.Equal("id Software, Inc.", metadata.Publisher);
    }

    [Fact]
    public void MapGame_MapsGenresToNormalisedKeys()
    {
        var metadata = MobyGamesMetadataProvider.MapGame(FirstGame(), Detail(), "1069", DosPlatformId, 6);

        Assert.Contains(GenreKey.Shooter, metadata.Genres);
        Assert.Contains(GenreKey.Action, metadata.Genres);

        // Perspective / setting labels must not leak in as genres.
        Assert.DoesNotContain(GenreKey.Other, metadata.Genres);
        Assert.Contains("Action", metadata.RawGenres);
    }

    [Fact]
    public void MapGame_TakesCoverAndScreenshotsFromTheSampleData()
    {
        var metadata = MobyGamesMetadataProvider.MapGame(FirstGame(), Detail(), "1069", DosPlatformId, 6);

        Assert.NotNull(metadata.Cover);
        Assert.Equal(MediaKind.Cover, metadata.Cover!.Kind);
        Assert.Contains("1069-doom-dos-front-cover", metadata.Cover.Url);
        Assert.Equal("jpg", metadata.Cover.Format);

        Assert.Equal(2, metadata.Screenshots.Count);
        Assert.All(metadata.Screenshots, s => Assert.Equal(MediaKind.Screenshot, s.Kind));
    }

    [Fact]
    public void MapGame_RejectsScreenshotsFromForeignHosts()
    {
        var metadata = MobyGamesMetadataProvider.MapGame(FirstGame(), Detail(), "1069", DosPlatformId, 6);

        Assert.DoesNotContain(metadata.Screenshots, s => s.Url.Contains("evil.example.com"));
    }

    [Fact]
    public void MapGame_HonoursTheScreenshotLimit()
    {
        var one = MobyGamesMetadataProvider.MapGame(FirstGame(), Detail(), "1069", DosPlatformId, 1);
        var none = MobyGamesMetadataProvider.MapGame(FirstGame(), Detail(), "1069", DosPlatformId, 0);

        Assert.Single(one.Screenshots);
        Assert.Empty(none.Screenshots);
    }

    [Fact]
    public void MapGame_WorksWithoutPlatformDetail()
    {
        // A game with no DOS release detail still yields a usable record from the game data.
        var metadata = MobyGamesMetadataProvider.MapGame(FirstGame(), detail: null, "1069", DosPlatformId, 6);

        Assert.Equal("Doom", metadata.Title);
        Assert.Equal(1993, metadata.ReleaseYear);
        Assert.Null(metadata.Developer);
        Assert.Null(metadata.Players);
    }

    [Theory]
    [InlineData("https://cdn.mobygames.com/covers/x.jpg", true)]
    [InlineData("http://www.mobygames.com/images/covers/x.jpg", true)]
    [InlineData("https://mobygames.com/x.jpg", true)]
    [InlineData("https://mobygames.com.evil.com/x.jpg", false)]
    [InlineData("https://cdn.example.com/x.jpg", false)]
    [InlineData("ftp://cdn.mobygames.com/x.jpg", false)]
    [InlineData("not a url", false)]
    public void MediaUrlValidation_OnlyAcceptsMobyGamesHosts(string url, bool expected) =>
        Assert.Equal(expected, MobyGamesClient.TryValidateMediaUrl(url, out _));

    [Fact]
    public void StripHtml_HandlesNullAndPlainText()
    {
        Assert.Null(MobyGamesMetadataProvider.StripHtml(null));
        Assert.Null(MobyGamesMetadataProvider.StripHtml("   "));
        Assert.Equal("Plain text", MobyGamesMetadataProvider.StripHtml("Plain text"));
    }

    [Fact]
    public void StripHtml_DecodesEntities()
    {
        Assert.Equal("Tom & Jerry", MobyGamesMetadataProvider.StripHtml("Tom &amp; Jerry"));
    }

    [Fact]
    public void GenreMapper_PrefersGameplayCategories()
    {
        var genres = new[]
        {
            new MobyGenre { GenreCategory = "Perspective", GenreCategoryId = 2, GenreName = "1st-person" },
            new MobyGenre { GenreCategory = "Basic Genres", GenreCategoryId = 1, GenreName = "Racing / Driving" },
        };

        var mapped = MobyGamesGenreMapper.MapAll(genres);

        Assert.Contains(GenreKey.Racing, mapped);
    }

    [Fact]
    public void GenreMapper_ReturnsEmptyForNoGenres() =>
        Assert.Empty(MobyGamesGenreMapper.MapAll(Array.Empty<MobyGenre>()));
}
