using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Infrastructure;
using DosBoxxer.Core.Infrastructure.Database;
using DosBoxxer.Core.Infrastructure.Repositories;
using DosBoxxer.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DosBoxxer.Tests;

public sealed class LibraryFilterTests
{
    /// <summary>
    /// The filter is a pure function on <see cref="GameLibraryService"/>, so it can be tested
    /// without a database, a provider or any file system access.
    /// </summary>
    private static GameLibraryService CreateService() => new(
        new FakeRepository(),
        new FakeMetadataProvider(),
        new MetadataMerger(),
        new FakeMediaDownloader(),
        new FakeSettingsService(),
        NullLogger<GameLibraryService>.Instance);

    private static IReadOnlyList<Game> CreateLibrary()
    {
        var doom = NewGame("Doom", 1993, "id Software", "GT Interactive", GenreKey.Shooter);
        doom.IsFavorite = true;
        doom.LastPlayed = DateTimeOffset.UtcNow.AddDays(-1);
        doom.TotalPlayTimeSeconds = 7200;
        doom.DateAdded = DateTimeOffset.UtcNow.AddDays(-10);

        var keen = NewGame("Commander Keen", 1990, "id Software", "Apogee", GenreKey.Platformer);
        keen.TotalPlayTimeSeconds = 600;
        keen.DateAdded = DateTimeOffset.UtcNow.AddDays(-2);

        var civ = NewGame("The Civilization", 1991, "MicroProse", "MicroProse", GenreKey.Strategy);
        civ.LastPlayed = DateTimeOffset.UtcNow.AddDays(-5);
        civ.TotalPlayTimeSeconds = 36000;
        civ.DateAdded = DateTimeOffset.UtcNow.AddDays(-30);

        return new[] { doom, keen, civ };
    }

    private static Game NewGame(string title, int year, string developer, string publisher, GenreKey genre)
    {
        var game = new Game
        {
            Title = title,
            SortTitle = TitleCleaner.ToSortTitle(title),
            ReleaseYear = year,
            Developer = developer,
            Publisher = publisher,
        };

        game.Genres.Add(genre);
        return game;
    }

    [Fact]
    public void Filter_AllGames_ReturnsEverythingSortedByTitle()
    {
        var result = CreateService().Filter(CreateLibrary(), new LibraryQuery());

        Assert.Equal(3, result.Count);

        // "The Civilization" sorts under C because the leading article is stripped.
        Assert.Equal(new[] { "The Civilization", "Commander Keen", "Doom" }, result.Select(g => g.Title));
    }

    [Fact]
    public void Filter_ByGenre()
    {
        var result = CreateService().Filter(
            CreateLibrary(),
            new LibraryQuery { Scope = LibraryScope.Genre, Genre = GenreKey.Shooter });

        Assert.Single(result);
        Assert.Equal("Doom", result[0].Title);
    }

    [Fact]
    public void Filter_Favorites()
    {
        var result = CreateService().Filter(CreateLibrary(), new LibraryQuery { Scope = LibraryScope.Favorites });

        Assert.Single(result);
        Assert.True(result[0].IsFavorite);
    }

    [Fact]
    public void Filter_RecentlyPlayed_OrdersByLastPlayedDescending()
    {
        var result = CreateService().Filter(CreateLibrary(), new LibraryQuery { Scope = LibraryScope.RecentlyPlayed });

        Assert.Equal(2, result.Count);
        Assert.Equal("Doom", result[0].Title);
    }

    [Theory]
    [InlineData("doom", 1)]
    [InlineData("id Software", 2)]
    [InlineData("MicroProse", 1)]
    [InlineData("1993", 1)]
    [InlineData("nothingmatches", 0)]
    public void Filter_SearchesTitlePublisherDeveloperAndYear(string term, int expected)
    {
        var result = CreateService().Filter(CreateLibrary(), new LibraryQuery { SearchText = term });

        Assert.Equal(expected, result.Count);
    }

    [Fact]
    public void Filter_SearchRequiresAllTerms()
    {
        var result = CreateService().Filter(CreateLibrary(), new LibraryQuery { SearchText = "id 1990" });

        Assert.Single(result);
        Assert.Equal("Commander Keen", result[0].Title);
    }

    [Theory]
    [InlineData(GameSortOrder.TitleAscending, "The Civilization")]
    [InlineData(GameSortOrder.TitleDescending, "Doom")]
    [InlineData(GameSortOrder.YearAscending, "Commander Keen")]
    [InlineData(GameSortOrder.YearDescending, "Doom")]
    [InlineData(GameSortOrder.RecentlyAdded, "Commander Keen")]
    [InlineData(GameSortOrder.LastPlayed, "Doom")]
    [InlineData(GameSortOrder.PlayTime, "The Civilization")]
    public void Filter_AppliesEverySortOrder(GameSortOrder order, string expectedFirst)
    {
        var result = CreateService().Filter(CreateLibrary(), new LibraryQuery { SortOrder = order });

        Assert.Equal(expectedFirst, result[0].Title);
    }

    [Fact]
    public void Filter_GamesWithoutAYearSortLast()
    {
        var library = CreateLibrary().ToList();
        library.Add(NewGame("Unknown Year", 0, "x", "y", GenreKey.Other));
        library[^1].ReleaseYear = null;

        var result = CreateService().Filter(library, new LibraryQuery { SortOrder = GameSortOrder.YearAscending });

        Assert.Equal("Unknown Year", result[^1].Title);
    }

    // ---- minimal fakes --------------------------------------------------------------------

    private sealed class FakeRepository : IGameRepository
    {
        public Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Game>>(Array.Empty<Game>());

        public Task<Game?> GetAsync(Guid id, CancellationToken cancellationToken = default) => Task.FromResult<Game?>(null);

        public Task<Game?> FindByLaunchFileAsync(string launchFile, CancellationToken cancellationToken = default) =>
            Task.FromResult<Game?>(null);

        public Task AddAsync(Game game, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdateAsync(Game game, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RecordPlaySessionAsync(Guid id, DateTimeOffset startedAt, long durationSeconds, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IReadOnlyDictionary<GenreKey, int>> GetGenreCountsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<GenreKey, int>>(new Dictionary<GenreKey, int>());
    }

    private sealed class FakeMetadataProvider : IGameMetadataProvider
    {
        public string ProviderKey => "fake";

        public bool IsConfigured => false;

        public Task<MetadataResult<IReadOnlyList<Core.Models.Metadata.MetadataSearchResult>>> SearchAsync(
            string searchTerm,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(MetadataResult<IReadOnlyList<Core.Models.Metadata.MetadataSearchResult>>
                .Failure(MetadataErrorKind.NotConfigured));

        public Task<MetadataResult<Core.Models.Metadata.GameMetadata>> GetGameAsync(
            string providerGameId,
            string languageCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(MetadataResult<Core.Models.Metadata.GameMetadata>.Failure(MetadataErrorKind.NotConfigured));

        public Task<MetadataResult<string>> TestConnectionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(MetadataResult<string>.Failure(MetadataErrorKind.NotConfigured));
    }

    private sealed class FakeMediaDownloader : IMediaDownloader
    {
        public Task<DownloadedMedia?> DownloadAsync(Guid gameId, Core.Models.Metadata.MetadataMedia media, int index, CancellationToken cancellationToken = default) =>
            Task.FromResult<DownloadedMedia?>(null);

        public Task<string?> ImportLocalCoverAsync(Guid gameId, string sourcePath, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<string?> ImportLocalScreenshotAsync(Guid gameId, string sourcePath, int index, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task DeleteMediaAsync(Guid gameId, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ClearCacheAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<long> GetCacheSizeAsync(CancellationToken cancellationToken = default) => Task.FromResult(0L);
    }

    private sealed class FakeSettingsService : ISettingsService
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
}

public sealed class GameRepositoryTests
{
    private static async Task<(GameRepository Repository, TempDirectory Temp)> CreateRepositoryAsync()
    {
        var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();

        var factory = new SqliteConnectionFactory(paths);
        var initializer = new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitializeAsync();

        return (new GameRepository(factory, NullLogger<GameRepository>.Instance), temp);
    }

    private static Game SampleGame()
    {
        var game = new Game
        {
            Title = "Doom",
            SortTitle = "DOOM",
            GameDirectory = "/games/doom",
            LaunchFile = "/games/doom/BIN/DOOM.EXE",
            RelativeLaunchFile = "BIN/DOOM.EXE",
            Publisher = "GT Interactive",
            Developer = "id Software",
            ReleaseYear = 1993,
            Description = "Demons on Phobos",
            Players = "1-4",
            ScreenScraperId = "3474",
            ManualFields = GameField.Title,
        };

        game.Genres.Add(GenreKey.Shooter);
        game.Genres.Add(GenreKey.Action);
        game.Screenshots.Add(new Screenshot { GameId = game.Id, LocalPath = "/cache/a.png", RemoteUrl = "https://x/a", SortOrder = 0 });
        game.DosBoxSettings = new GameDosBoxSettings { GameId = game.Id, Cycles = "20000", Fullscreen = true };

        return game;
    }

    [Fact]
    public async Task AddAndReadBack_PreservesEveryField()
    {
        var (repository, temp) = await CreateRepositoryAsync();
        using var _ = temp;

        var game = SampleGame();
        await repository.AddAsync(game);

        var loaded = await repository.GetAsync(game.Id);

        Assert.NotNull(loaded);
        Assert.Equal("Doom", loaded!.Title);
        Assert.Equal("/games/doom/BIN/DOOM.EXE", loaded.LaunchFile);
        Assert.Equal(1993, loaded.ReleaseYear);
        Assert.Equal("3474", loaded.ScreenScraperId);
        Assert.Equal(GameField.Title, loaded.ManualFields);
        Assert.Equal(2, loaded.Genres.Count);
        Assert.Single(loaded.Screenshots);
        Assert.Equal("20000", loaded.DosBoxSettings.Cycles);
        Assert.True(loaded.DosBoxSettings.Fullscreen);
    }

    [Fact]
    public async Task Update_ReplacesGenresAndScreenshots()
    {
        var (repository, temp) = await CreateRepositoryAsync();
        using var _ = temp;

        var game = SampleGame();
        await repository.AddAsync(game);

        game.Title = "Doom II";
        game.Genres.Clear();
        game.Genres.Add(GenreKey.Strategy);
        game.Screenshots.Clear();

        await repository.UpdateAsync(game);

        var loaded = await repository.GetAsync(game.Id);

        Assert.Equal("Doom II", loaded!.Title);
        Assert.Equal(new[] { GenreKey.Strategy }, loaded.Genres);
        Assert.Empty(loaded.Screenshots);
    }

    [Fact]
    public async Task RecordPlaySession_AccumulatesStatistics()
    {
        var (repository, temp) = await CreateRepositoryAsync();
        using var _ = temp;

        var game = SampleGame();
        await repository.AddAsync(game);

        var start = DateTimeOffset.UtcNow;
        await repository.RecordPlaySessionAsync(game.Id, start, 120);
        await repository.RecordPlaySessionAsync(game.Id, start.AddMinutes(5), 60);

        var loaded = await repository.GetAsync(game.Id);

        Assert.Equal(2, loaded!.PlayCount);
        Assert.Equal(180, loaded.TotalPlayTimeSeconds);
        Assert.NotNull(loaded.LastPlayed);
    }

    [Fact]
    public async Task Delete_RemovesTheGameAndItsChildRows()
    {
        var (repository, temp) = await CreateRepositoryAsync();
        using var _ = temp;

        var game = SampleGame();
        await repository.AddAsync(game);
        await repository.DeleteAsync(game.Id);

        Assert.Null(await repository.GetAsync(game.Id));
        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task FindByLaunchFile_LocatesDuplicates()
    {
        var (repository, temp) = await CreateRepositoryAsync();
        using var _ = temp;

        var game = SampleGame();
        await repository.AddAsync(game);

        Assert.NotNull(await repository.FindByLaunchFileAsync("/games/doom/BIN/DOOM.EXE"));
        Assert.Null(await repository.FindByLaunchFileAsync("/games/other/GAME.EXE"));
    }

    [Fact]
    public async Task GenreCounts_AreAggregated()
    {
        var (repository, temp) = await CreateRepositoryAsync();
        using var _ = temp;

        await repository.AddAsync(SampleGame());

        var second = SampleGame();
        second.Id = Guid.NewGuid();
        second.LaunchFile = "/games/other/GAME.EXE";
        second.Genres.Clear();
        second.Genres.Add(GenreKey.Shooter);
        await repository.AddAsync(second);

        var counts = await repository.GetGenreCountsAsync();

        Assert.Equal(2, counts[GenreKey.Shooter]);
        Assert.Equal(1, counts[GenreKey.Action]);
    }

    [Fact]
    public async Task Initializer_IsIdempotent()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();

        var factory = new SqliteConnectionFactory(paths);
        var initializer = new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance);

        await initializer.InitializeAsync();
        await initializer.InitializeAsync();

        var repository = new GameRepository(factory, NullLogger<GameRepository>.Instance);
        Assert.Empty(await repository.GetAllAsync());
    }

    [Fact]
    public async Task LibrarySurvivesAReopen()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();

        var game = SampleGame();

        var firstFactory = new SqliteConnectionFactory(paths);
        await new DatabaseInitializer(firstFactory, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        await new GameRepository(firstFactory, NullLogger<GameRepository>.Instance).AddAsync(game);

        // Simulates restarting the application against the same data directory.
        var secondFactory = new SqliteConnectionFactory(paths);
        await new DatabaseInitializer(secondFactory, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        var reloaded = await new GameRepository(secondFactory, NullLogger<GameRepository>.Instance).GetAllAsync();

        Assert.Single(reloaded);
        Assert.Equal("Doom", reloaded[0].Title);
        Assert.Equal(2, reloaded[0].Genres.Count);
    }
}

public sealed class TitleCleanerTests
{
    [Theory]
    [InlineData("DOOM", "Doom")]
    [InlineData("doom_2", "Doom 2")]
    [InlineData("Commander-Keen", "Commander Keen")]
    [InlineData("DUKE3D.EXE", "Duke3d")]
    [InlineData("Doom_v1.9_[CD]", "Doom")]
    [InlineData("prince_of_persia", "Prince Of Persia")]
    public void FromDirectoryName_ProducesAReadableTitle(string input, string expected) =>
        Assert.Equal(expected, TitleCleaner.FromDirectoryName(input));

    [Fact]
    public void FromDirectoryName_HandlesEmptyInput()
    {
        Assert.Equal(string.Empty, TitleCleaner.FromDirectoryName(null));
        Assert.Equal(string.Empty, TitleCleaner.FromDirectoryName("   "));
    }

    [Theory]
    [InlineData("The Secret of Monkey Island", "SECRET OF MONKEY ISLAND")]
    [InlineData("A Mind Forever Voyaging", "MIND FOREVER VOYAGING")]
    [InlineData("Der Clou", "CLOU")]
    [InlineData("Doom", "DOOM")]
    public void ToSortTitle_StripsLeadingArticles(string input, string expected) =>
        Assert.Equal(expected, TitleCleaner.ToSortTitle(input));
}
