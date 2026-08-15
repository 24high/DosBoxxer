using System;
using System.Linq;
using DosBoxxer.Core.Infrastructure;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Metadata;
using Xunit;

namespace DosBoxxer.Tests;

public sealed class MetadataMergeTests
{
    private static Game CreateGame()
    {
        var game = new Game
        {
            Title = "Old Title",
            SortTitle = "OLD TITLE",
            Publisher = "Old Publisher",
            Developer = "Old Developer",
            ReleaseYear = 1990,
            Description = "Old description",
            Players = "1",
        };

        game.Genres.Add(GenreKey.Other);
        return game;
    }

    private static GameMetadata CreateMetadata() => new()
    {
        ProviderGameId = "3474",
        Title = "New Title",
        Publisher = "New Publisher",
        Developer = "New Developer",
        ReleaseYear = 1993,
        Description = "New description",
        Players = "1-4",
        Genres = new[] { GenreKey.Shooter, GenreKey.Action },
        SystemName = "PC Dos",
    };

    [Fact]
    public void Merge_WritesEveryFieldWhenNothingIsProtected()
    {
        var game = CreateGame();

        var result = new MetadataMerger().Merge(game, CreateMetadata(), MetadataMergeOptions.Default);

        Assert.Equal("New Title", game.Title);
        Assert.Equal("NEW TITLE", game.SortTitle);
        Assert.Equal("New Publisher", game.Publisher);
        Assert.Equal("New Developer", game.Developer);
        Assert.Equal(1993, game.ReleaseYear);
        Assert.Equal("New description", game.Description);
        Assert.Equal("1-4", game.Players);
        Assert.Equal(new[] { GenreKey.Shooter, GenreKey.Action }, game.Genres);
        Assert.Equal("3474", game.ScreenScraperId);
        Assert.True(result.AnyChange);
    }

    [Fact]
    public void Merge_KeepsManuallyEditedFields()
    {
        var game = CreateGame();
        game.ManualFields = GameField.Title | GameField.Description;

        var result = new MetadataMerger().Merge(game, CreateMetadata(), MetadataMergeOptions.Default);

        Assert.Equal("Old Title", game.Title);
        Assert.Equal("Old description", game.Description);
        Assert.Equal("New Publisher", game.Publisher);

        Assert.True(result.SkippedManualFields.HasFlag(GameField.Title));
        Assert.True(result.SkippedManualFields.HasFlag(GameField.Description));
        Assert.False(result.UpdatedFields.HasFlag(GameField.Title));
    }

    [Fact]
    public void Merge_OverwritesManualFieldsWhenExplicitlyAllowed()
    {
        var game = CreateGame();
        game.ManualFields = GameField.Title;

        new MetadataMerger().Merge(
            game,
            CreateMetadata(),
            new MetadataMergeOptions { Fields = GameField.All, OverwriteManualEdits = true });

        Assert.Equal("New Title", game.Title);
    }

    [Fact]
    public void Merge_OnlyTouchesTheSelectedFields()
    {
        var game = CreateGame();

        new MetadataMerger().Merge(
            game,
            CreateMetadata(),
            new MetadataMergeOptions { Fields = GameField.Publisher | GameField.ReleaseYear });

        Assert.Equal("Old Title", game.Title);
        Assert.Equal("Old description", game.Description);
        Assert.Equal("New Publisher", game.Publisher);
        Assert.Equal(1993, game.ReleaseYear);
    }

    [Fact]
    public void Merge_DoesNotClearExistingValuesWithEmptyMetadata()
    {
        var game = CreateGame();

        var sparse = new GameMetadata
        {
            ProviderGameId = "1",
            Title = "Only A Title",
        };

        new MetadataMerger().Merge(game, sparse, MetadataMergeOptions.Default);

        Assert.Equal("Only A Title", game.Title);
        Assert.Equal("Old Publisher", game.Publisher);
        Assert.Equal("Old Developer", game.Developer);
        Assert.Equal(1990, game.ReleaseYear);
        Assert.NotEmpty(game.Genres);
    }

    [Fact]
    public void Merge_WithInitialOptionsAllowsClearing()
    {
        var game = CreateGame();

        var sparse = new GameMetadata { ProviderGameId = "1", Title = "T" };

        new MetadataMerger().Merge(game, sparse, MetadataMergeOptions.Initial);

        Assert.Null(game.Publisher);
        Assert.Null(game.Developer);
        Assert.Null(game.ReleaseYear);
    }

    [Fact]
    public void Merge_StampsTheRefreshTimestampAndProviderId()
    {
        var game = CreateGame();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        new MetadataMerger().Merge(game, CreateMetadata(), MetadataMergeOptions.Default);

        Assert.NotNull(game.MetadataLastUpdated);
        Assert.True(game.MetadataLastUpdated > before);
        Assert.Equal("3474", game.ScreenScraperId);
        Assert.Equal("PC Dos", game.Platform);
    }

    [Fact]
    public void Merge_DeduplicatesGenres()
    {
        var game = CreateGame();

        var metadata = new GameMetadata
        {
            ProviderGameId = "1",
            Title = "T",
            Genres = new[] { GenreKey.Action, GenreKey.Action, GenreKey.Shooter },
        };

        new MetadataMerger().Merge(game, metadata, MetadataMergeOptions.Default);

        Assert.Equal(2, game.Genres.Count);
        Assert.Equal(game.Genres.Distinct().Count(), game.Genres.Count);
    }
}
