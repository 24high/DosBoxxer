using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Infrastructure;
using DosBoxxer.Core.Infrastructure.Igdb;
using DosBoxxer.Core.Infrastructure.Igdb.Dto;
using DosBoxxer.Core.Infrastructure.Rawg;
using DosBoxxer.Core.Infrastructure.Rawg.Dto;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Metadata;
using Xunit;

namespace DosBoxxer.Tests;

public sealed class IgdbParsingTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static IgdbGame LoadGame()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "igdb-game.json");
        var games = JsonSerializer.Deserialize<List<IgdbGame>>(File.ReadAllText(path), Options);
        Assert.NotNull(games);
        return games!.First();
    }

    [Fact]
    public void MapGame_ProducesTheDomainModel()
    {
        var metadata = IgdbMetadataProvider.MapGame(LoadGame(), "1069", 6);

        Assert.Equal("1069", metadata.ProviderGameId);
        Assert.Equal("Doom", metadata.Title);
        Assert.StartsWith("You are a space marine", metadata.Description);
        Assert.Equal("id Software", metadata.Developer);
        Assert.Equal("GT Interactive", metadata.Publisher);
        Assert.Equal(1993, metadata.ReleaseYear);
    }

    [Fact]
    public void MapGame_MapsGenresToNormalisedKeys()
    {
        var metadata = IgdbMetadataProvider.MapGame(LoadGame(), "1069", 6);

        Assert.Contains(GenreKey.Shooter, metadata.Genres);
        Assert.Contains(GenreKey.Adventure, metadata.Genres);
    }

    [Fact]
    public void MapGame_BuildsCdnUrlsForCoverAndScreenshots()
    {
        var metadata = IgdbMetadataProvider.MapGame(LoadGame(), "1069", 6);

        Assert.NotNull(metadata.Cover);
        Assert.Equal("https://images.igdb.com/igdb/image/upload/t_cover_big/co1n5f.jpg", metadata.Cover!.Url);
        Assert.Equal(3, metadata.Screenshots.Count);
        Assert.Equal("https://images.igdb.com/igdb/image/upload/t_screenshot_big/sc1aa.jpg", metadata.Screenshots[0].Url);
        Assert.All(metadata.Screenshots, s => Assert.True(IgdbClient.TryValidateMediaUrl(s.Url, out _)));
    }

    [Fact]
    public void MapGame_HonoursTheScreenshotLimit()
    {
        Assert.Single(IgdbMetadataProvider.MapGame(LoadGame(), "1069", 1).Screenshots);
        Assert.Empty(IgdbMetadataProvider.MapGame(LoadGame(), "1069", 0).Screenshots);
    }

    [Fact]
    public void BuildImageUrl_UsesTheGivenSize() =>
        Assert.Equal(
            "https://images.igdb.com/igdb/image/upload/t_cover_small/abc.jpg",
            IgdbClient.BuildImageUrl("abc", "t_cover_small"));

    [Theory]
    [InlineData("https://images.igdb.com/igdb/image/upload/t_cover_big/x.jpg", true)]
    [InlineData("https://media.rawg.io/x.jpg", false)]
    [InlineData("https://images.igdb.com.evil.com/x.jpg", false)]
    public void MediaUrlValidation_OnlyAcceptsIgdbHosts(string url, bool expected) =>
        Assert.Equal(expected, IgdbClient.TryValidateMediaUrl(url, out _));

    [Fact]
    public void YearConversion_HandlesUnixTimestamp()
    {
        // 755481600 = 1993-12-10 UTC.
        var metadata = IgdbMetadataProvider.MapGame(LoadGame(), "1069", 6);
        Assert.Equal(1993, metadata.ReleaseYear);
    }
}

public sealed class RawgParsingTests
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    private static RawgGame LoadGame()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "rawg-game-detail.json");
        var game = JsonSerializer.Deserialize<RawgGame>(File.ReadAllText(path), Options);
        Assert.NotNull(game);
        return game!;
    }

    [Fact]
    public void MapGame_ProducesTheDomainModel()
    {
        var metadata = RawgMetadataProvider.MapGame(LoadGame(), "9885", 6);

        Assert.Equal("Doom", metadata.Title);
        Assert.StartsWith("Doom is a landmark", metadata.Description);
        Assert.Equal("id Software", metadata.Developer);
        Assert.Equal("GT Interactive", metadata.Publisher);
        Assert.Equal(1993, metadata.ReleaseYear);
        Assert.Equal("PC", metadata.SystemName);
    }

    [Fact]
    public void MapGame_UsesBackgroundImageAsCover()
    {
        var metadata = RawgMetadataProvider.MapGame(LoadGame(), "9885", 6);

        Assert.NotNull(metadata.Cover);
        Assert.Equal("https://media.rawg.io/media/games/doom-front.jpg", metadata.Cover!.Url);
    }

    [Fact]
    public void MapGame_SkipsCoverDuplicateAndForeignHostsInScreenshots()
    {
        var metadata = RawgMetadataProvider.MapGame(LoadGame(), "9885", 6);

        // The background image (id -1) and the foreign-host image must not appear.
        Assert.Equal(2, metadata.Screenshots.Count);
        Assert.DoesNotContain(metadata.Screenshots, s => s.Url == metadata.Cover!.Url);
        Assert.DoesNotContain(metadata.Screenshots, s => s.Url.Contains("evil.example.com"));
    }

    [Fact]
    public void MapGame_MapsGenres()
    {
        var metadata = RawgMetadataProvider.MapGame(LoadGame(), "9885", 6);

        Assert.Contains(GenreKey.Shooter, metadata.Genres);
        Assert.Contains(GenreKey.Action, metadata.Genres);
    }

    [Theory]
    [InlineData("https://media.rawg.io/media/x.jpg", true)]
    [InlineData("https://rawg.io/x.jpg", true)]
    [InlineData("https://images.igdb.com/x.jpg", false)]
    [InlineData("https://media.rawg.io.evil.com/x.jpg", false)]
    public void MediaUrlValidation_OnlyAcceptsRawgHosts(string url, bool expected) =>
        Assert.Equal(expected, RawgClient.TryValidateMediaUrl(url, out _));
}

/// <summary>
/// Verifies the provider-selection facade and the media router that make the three providers
/// interchangeable.
/// </summary>
public sealed class ProviderSelectionTests
{
    private sealed class StubSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public event EventHandler<AppSettings>? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            SettingsChanged?.Invoke(this, settings);
            return Task.CompletedTask;
        }

        public Task SaveCurrentAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class StubProvider : IGameMetadataProvider
    {
        public StubProvider(string key) => ProviderKey = key;

        public string ProviderKey { get; }

        public bool IsConfigured => true;

        public Task<MetadataResult<IReadOnlyList<MetadataSearchResult>>> SearchAsync(string searchTerm, CancellationToken cancellationToken = default) =>
            Task.FromResult(MetadataResult<IReadOnlyList<MetadataSearchResult>>.Success(
                new List<MetadataSearchResult> { new() { ProviderGameId = ProviderKey, Title = ProviderKey } }));

        public Task<MetadataResult<GameMetadata>> GetGameAsync(string providerGameId, string languageCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(MetadataResult<GameMetadata>.Failure(MetadataErrorKind.NoResults));

        public Task<MetadataResult<string>> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(MetadataResult<string>.Success(ProviderKey));
    }

    [Fact]
    public async Task ActiveProvider_RoutesToTheConfiguredProvider()
    {
        var settings = new StubSettings();
        var providers = new IGameMetadataProvider[] { new StubProvider("mobygames"), new StubProvider("igdb"), new StubProvider("rawg") };
        var active = new ActiveMetadataProvider(providers, settings);

        settings.Current.MetadataProviderKey = "igdb";
        Assert.Equal("igdb", active.ProviderKey);
        Assert.Equal("igdb", (await active.TestConnectionAsync()).Value);

        settings.Current.MetadataProviderKey = "rawg";
        Assert.Equal("rawg", active.ProviderKey);
        var search = await active.SearchAsync("x");
        Assert.Equal("rawg", search.Value![0].Title);
    }

    [Fact]
    public void ActiveProvider_FallsBackToTheFirstProviderForUnknownKeys()
    {
        var settings = new StubSettings { };
        settings.Current.MetadataProviderKey = "does-not-exist";
        var providers = new IGameMetadataProvider[] { new StubProvider("mobygames"), new StubProvider("igdb") };

        var active = new ActiveMetadataProvider(providers, settings);
        Assert.Equal("mobygames", active.ProviderKey);
    }

    private sealed class StubMediaClient : IMediaHttpClient
    {
        private readonly string _host;

        public StubMediaClient(string host) => _host = host;

        public bool AcceptsUrl(string url) => url.Contains(_host, StringComparison.OrdinalIgnoreCase);

        public Task<Stream?> DownloadMediaAsync(string url, CancellationToken cancellationToken) =>
            Task.FromResult<Stream?>(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(_host)));
    }

    [Fact]
    public async Task CompositeMediaClient_RoutesByHost()
    {
        var composite = new CompositeMediaHttpClient(new IMediaHttpClient[]
        {
            new StubMediaClient("mobygames.com"),
            new StubMediaClient("igdb.com"),
            new StubMediaClient("rawg.io"),
        });

        Assert.True(composite.AcceptsUrl("https://images.igdb.com/x.jpg"));
        Assert.False(composite.AcceptsUrl("https://example.com/x.jpg"));

        await using var stream = await composite.DownloadMediaAsync("https://media.rawg.io/x.jpg", CancellationToken.None);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream!);
        Assert.Equal("rawg.io", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task CompositeMediaClient_ReturnsNullForUnknownHost()
    {
        var composite = new CompositeMediaHttpClient(new IMediaHttpClient[] { new StubMediaClient("mobygames.com") });
        Assert.Null(await composite.DownloadMediaAsync("https://example.com/x.jpg", CancellationToken.None));
    }
}
