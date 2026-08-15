using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Infrastructure.ScreenScraper;
using DosBoxxer.Core.Infrastructure.ScreenScraper.Dto;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Metadata;
using Xunit;

namespace DosBoxxer.Tests;

/// <summary>
/// Parsing and mapping tests run entirely against recorded fixtures — no ScreenScraper account
/// and no network access are required.
/// </summary>
public sealed class ScreenScraperParsingTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static SsEnvelope LoadFixture(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
        var envelope = JsonSerializer.Deserialize<SsEnvelope>(File.ReadAllText(path), Options);
        Assert.NotNull(envelope);
        return envelope!;
    }

    [Fact]
    public void GameInfo_IsParsedIntoTheDomainModel()
    {
        var envelope = LoadFixture("jeuInfos-sample.json");
        var game = envelope.Response?.Game;

        Assert.NotNull(game);

        var metadata = ScreenScraperMetadataProvider.MapGame(game!, "3474", "en", "wor", 6);

        Assert.Equal("3474", metadata.ProviderGameId);
        Assert.Equal("Doom", metadata.Title);
        Assert.Equal("GT Interactive", metadata.Publisher);
        Assert.Equal("id Software", metadata.Developer);
        Assert.Equal(1993, metadata.ReleaseYear);
        Assert.Equal("1-4", metadata.Players);
        Assert.Equal("PC Dos", metadata.SystemName);
    }

    [Fact]
    public void GameInfo_UsesTheRequestedLanguageForTheDescription()
    {
        var game = LoadFixture("jeuInfos-sample.json").Response!.Game!;

        var german = ScreenScraperMetadataProvider.MapGame(game, "3474", "de", "wor", 6);
        var french = ScreenScraperMetadataProvider.MapGame(game, "3474", "fr", "wor", 6);

        Assert.StartsWith("Du bist ein Space Marine", german.Description);
        Assert.StartsWith("Vous êtes un marine spatial", french.Description);
    }

    [Fact]
    public void GameInfo_FallsBackToEnglishForUnavailableLanguages()
    {
        var game = LoadFixture("jeuInfos-sample.json").Response!.Game!;

        // Hindi is not present in the fixture, so English must be used.
        var hindi = ScreenScraperMetadataProvider.MapGame(game, "3474", "hi", "wor", 6);

        Assert.StartsWith("You are a space marine", hindi.Description);
    }

    [Fact]
    public void GameInfo_MapsProviderGenresToNormalisedGenres()
    {
        var game = LoadFixture("jeuInfos-sample.json").Response!.Game!;
        var metadata = ScreenScraperMetadataProvider.MapGame(game, "3474", "en", "wor", 6);

        Assert.Contains(GenreKey.Shooter, metadata.Genres);
        Assert.Contains(GenreKey.Action, metadata.Genres);
        Assert.Contains("Shoot'em Up", metadata.RawGenres);
    }

    [Fact]
    public void GameInfo_PrefersTheConfiguredMediaRegionForTheCover()
    {
        var game = LoadFixture("jeuInfos-sample.json").Response!.Game!;

        var world = ScreenScraperMetadataProvider.MapGame(game, "3474", "en", "wor", 6);
        var us = ScreenScraperMetadataProvider.MapGame(game, "3474", "en", "us", 6);

        Assert.NotNull(world.Cover);
        Assert.Contains("box-2D(wor)", world.Cover!.Url);
        Assert.Contains("box-2D(us)", us.Cover!.Url);
    }

    [Fact]
    public void GameInfo_CollectsScreenshotsAndHonoursTheLimit()
    {
        var game = LoadFixture("jeuInfos-sample.json").Response!.Game!;

        var all = ScreenScraperMetadataProvider.MapGame(game, "3474", "en", "wor", 6);
        var limited = ScreenScraperMetadataProvider.MapGame(game, "3474", "en", "wor", 1);

        Assert.Equal(2, all.Screenshots.Count);
        Assert.All(all.Screenshots, s => Assert.Equal(MediaKind.Screenshot, s.Kind));
        Assert.Single(limited.Screenshots);
    }

    [Fact]
    public void GameInfo_KeepsAlternateTitles()
    {
        var game = LoadFixture("jeuInfos-sample.json").Response!.Game!;
        var metadata = ScreenScraperMetadataProvider.MapGame(game, "3474", "en", "wor", 6);

        Assert.Contains("ドゥーム", metadata.AlternateTitles);
    }

    [Fact]
    public void SearchResponse_ToleratesInconsistentShapes()
    {
        var envelope = LoadFixture("jeuRecherche-sample.json");
        var games = envelope.Response?.Games;

        Assert.NotNull(games);
        Assert.Equal(3, games!.Count);

        // "noms" as a single object instead of an array.
        Assert.Equal("3475", games[1].Id);
        Assert.Equal("The Ultimate Doom", games[1].Names![0].Text);

        // Numeric id instead of a string, and "dates" as an empty string.
        Assert.Equal("3476", games[2].Id);
    }

    [Fact]
    public void SearchResponse_ExtractsTheReleaseYear()
    {
        var games = LoadFixture("jeuRecherche-sample.json").Response!.Games!;

        Assert.Equal(1993, ScreenScraperMetadataProvider.PickYear(games[0], "wor"));
        Assert.Equal(1995, ScreenScraperMetadataProvider.PickYear(games[1], "wor"));
        Assert.Null(ScreenScraperMetadataProvider.PickYear(games[2], "wor"));
    }

    [Fact]
    public void MediaUrls_FromOtherHostsAreRejected()
    {
        var game = LoadFixture("jeuInfos-sample.json").Response!.Game!;

        // The fixture contains a "wheel" entry pointing at a foreign host.
        Assert.False(ScreenScraperClient.TryValidateMediaUrl("https://evil.example.com/steal?x=1", out _));
        Assert.True(ScreenScraperClient.TryValidateMediaUrl(
            "https://api.screenscraper.fr/api2/mediaJeu.php?jeuid=1", out _));

        var metadata = ScreenScraperMetadataProvider.MapGame(game, "3474", "en", "wor", 6);
        Assert.DoesNotContain(metadata.Screenshots, s => s.Url.Contains("evil.example.com"));
    }

    [Theory]
    [InlineData("http://api.screenscraper.fr/x", true)]
    [InlineData("https://www.screenscraper.fr/x", true)]
    [InlineData("https://screenscraper.fr.evil.com/x", false)]
    [InlineData("ftp://api.screenscraper.fr/x", false)]
    [InlineData("not a url", false)]
    [InlineData("", false)]
    public void MediaUrlValidation_OnlyAcceptsScreenScraperHosts(string url, bool expected) =>
        Assert.Equal(expected, ScreenScraperClient.TryValidateMediaUrl(url, out _));

    private static System.Collections.Generic.Dictionary<string, string> Parse(string query) =>
        query.Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => p.Length > 1 ? Uri.UnescapeDataString(p[1]) : string.Empty);

    [Fact]
    public void BuildQuery_Anonymous_SendsOnlySoftnameAndOutput()
    {
        var credentials = new ScreenScraperCredentials
        {
            DeveloperId = string.Empty,
            DeveloperPassword = string.Empty,
            SoftwareName = "DosBoxxer",
        };

        var query = Parse(ScreenScraperClient.BuildQuery(credentials, new System.Collections.Generic.Dictionary<string, string>()));

        Assert.Equal("DosBoxxer", query["softname"]);
        Assert.Equal("json", query["output"]);
        Assert.False(query.ContainsKey("devid"));
        Assert.False(query.ContainsKey("devpassword"));
        Assert.False(query.ContainsKey("ssid"));
        Assert.False(query.ContainsKey("sspassword"));
    }

    [Fact]
    public void BuildQuery_WithDeveloperAccess_AddsDevCredentials()
    {
        var credentials = new ScreenScraperCredentials
        {
            DeveloperId = "dev",
            DeveloperPassword = "devpass",
            SoftwareName = "DosBoxxer",
        };

        var query = Parse(ScreenScraperClient.BuildQuery(credentials, new System.Collections.Generic.Dictionary<string, string>()));

        Assert.Equal("dev", query["devid"]);
        Assert.Equal("devpass", query["devpassword"]);
        Assert.False(query.ContainsKey("ssid"));
    }

    [Fact]
    public void BuildQuery_WithUserAccountButNoDeveloperAccess_AddsOnlyUserCredentials()
    {
        var credentials = new ScreenScraperCredentials
        {
            DeveloperId = string.Empty,
            DeveloperPassword = string.Empty,
            SoftwareName = "DosBoxxer",
            UserName = "user",
            UserPassword = "userpass",
        };

        var query = Parse(ScreenScraperClient.BuildQuery(credentials, new System.Collections.Generic.Dictionary<string, string>()));

        Assert.Equal("user", query["ssid"]);
        Assert.Equal("userpass", query["sspassword"]);
        Assert.False(query.ContainsKey("devid"));
    }

    [Fact]
    public void BuildQuery_WithBothTiers_SendsEverything()
    {
        var credentials = new ScreenScraperCredentials
        {
            DeveloperId = "dev",
            DeveloperPassword = "devpass",
            SoftwareName = "DosBoxxer",
            UserName = "user",
            UserPassword = "userpass",
        };

        var query = Parse(ScreenScraperClient.BuildQuery(credentials, new System.Collections.Generic.Dictionary<string, string> { ["gameid"] = "3474" }));

        Assert.Equal("dev", query["devid"]);
        Assert.Equal("user", query["ssid"]);
        Assert.Equal("3474", query["gameid"]);
    }

    [Fact]
    public void BuildQuery_IncompleteUserAccountIsNotSent()
    {
        // A username without a password must not be sent as a half-authenticated request.
        var credentials = new ScreenScraperCredentials
        {
            DeveloperId = string.Empty,
            DeveloperPassword = string.Empty,
            SoftwareName = "DosBoxxer",
            UserName = "user",
            UserPassword = null,
        };

        var query = Parse(ScreenScraperClient.BuildQuery(credentials, new System.Collections.Generic.Dictionary<string, string>()));

        Assert.False(query.ContainsKey("ssid"));
    }

    [Fact]
    public void Credentials_AreUsableWithJustASoftwareName()
    {
        var anonymous = new ScreenScraperCredentials
        {
            DeveloperId = string.Empty,
            DeveloperPassword = string.Empty,
            SoftwareName = "DosBoxxer",
        };

        Assert.True(anonymous.IsUsable);
        Assert.False(anonymous.HasDeveloperCredentials);
        Assert.False(anonymous.HasUserCredentials);
    }

    [Fact]
    public void PlainTextErrors_AreClassified()
    {
        var loginError = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "error-response.txt"));

        Assert.Equal(MetadataErrorKind.InvalidCredentials, ScreenScraperClient.ClassifyTextError(loginError));
        Assert.Equal(MetadataErrorKind.RateLimited, ScreenScraperClient.ClassifyTextError("Quota de requetes depasse"));
        Assert.Equal(MetadataErrorKind.NoResults, ScreenScraperClient.ClassifyTextError("Jeu non trouve !"));
        Assert.Equal(MetadataErrorKind.Unknown, ScreenScraperClient.ClassifyTextError("something else entirely"));
        Assert.Equal(MetadataErrorKind.Unknown, ScreenScraperClient.ClassifyTextError(null));
    }

    [Theory]
    [InlineData("zh-Hans", "zh")]
    [InlineData("de", "de")]
    [InlineData("pt-BR", "pt")]
    [InlineData(null, "en")]
    [InlineData("", "en")]
    public void LanguageCodesAreReducedToThePrimarySubtag(string? input, string expected) =>
        Assert.Equal(expected, ScreenScraperMetadataProvider.NormalizeLanguage(input));
}

public sealed class GenreMapperTests
{
    [Theory]
    [InlineData("Shoot'em Up", GenreKey.Shooter)]
    [InlineData("First-Person Shooter", GenreKey.Shooter)]
    [InlineData("Platform", GenreKey.Platformer)]
    [InlineData("Plate-forme", GenreKey.Platformer)]
    [InlineData("Role playing games", GenreKey.RolePlaying)]
    [InlineData("Jeu de rôle", GenreKey.RolePlaying)]
    [InlineData("Rollenspiel", GenreKey.RolePlaying)]
    [InlineData("Strategy", GenreKey.Strategy)]
    [InlineData("Wargame", GenreKey.Strategy)]
    [InlineData("Point-and-Click", GenreKey.Adventure)]
    [InlineData("Racing, Driving", GenreKey.Racing)]
    [InlineData("Sports", GenreKey.Sports)]
    [InlineData("Puzzle-Game", GenreKey.Puzzle)]
    [InlineData("Versus Fighting", GenreKey.Fighting)]
    [InlineData("Flight Simulator", GenreKey.Simulation)]
    [InlineData("Educative", GenreKey.Educational)]
    [InlineData("Board game", GenreKey.Casual)]
    [InlineData("Arcade", GenreKey.Arcade)]
    [InlineData("Action", GenreKey.Action)]
    public void Map_RecognisesCommonProviderLabels(string label, GenreKey expected) =>
        Assert.Equal(expected, GenreMapper.Map(label));

    [Fact]
    public void Map_ReturnsNullForUnknownLabels()
    {
        Assert.Null(GenreMapper.Map("Totally Unknown Category"));
        Assert.Null(GenreMapper.Map(null));
        Assert.Null(GenreMapper.Map("   "));
    }

    [Fact]
    public void MapAll_SplitsCompoundLabels()
    {
        var result = GenreMapper.MapAll(new[] { "Action / Platform" });

        Assert.Contains(GenreKey.Action, result);
        Assert.Contains(GenreKey.Platformer, result);
    }

    [Fact]
    public void MapAll_RemovesDuplicates()
    {
        var result = GenreMapper.MapAll(new[] { "Action", "Action", "Aktion" });

        Assert.Single(result);
        Assert.Equal(GenreKey.Action, result[0]);
    }

    [Fact]
    public void MapAll_FallsBackToOtherWhenNothingMatches()
    {
        var result = GenreMapper.MapAll(new[] { "Totally Unknown Category" });

        Assert.Single(result);
        Assert.Equal(GenreKey.Other, result[0]);
    }

    [Fact]
    public void MapAll_ReturnsEmptyWhenThereWereNoLabels()
    {
        Assert.Empty(GenreMapper.MapAll(Array.Empty<string>()));
        Assert.Empty(GenreMapper.MapAll(new string?[] { null, "  " }));
    }
}
