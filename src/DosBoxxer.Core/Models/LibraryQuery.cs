namespace DosBoxxer.Core.Models;

public enum GameSortOrder
{
    TitleAscending = 0,
    TitleDescending = 1,
    YearAscending = 2,
    YearDescending = 3,
    RecentlyAdded = 4,
    LastPlayed = 5,
    PlayTime = 6,
}

public static class GameSortOrders
{
    public static IReadOnlyList<GameSortOrder> All { get; } = new[]
    {
        GameSortOrder.TitleAscending,
        GameSortOrder.TitleDescending,
        GameSortOrder.YearAscending,
        GameSortOrder.YearDescending,
        GameSortOrder.RecentlyAdded,
        GameSortOrder.LastPlayed,
        GameSortOrder.PlayTime,
    };

    public static string ResourceKey(GameSortOrder order) => "Sort." + order;
}

/// <summary>Virtual library categories that are not backed by a genre.</summary>
public enum LibraryScope
{
    AllGames = 0,
    Favorites = 1,
    RecentlyPlayed = 2,
    Genre = 3,
}

/// <summary>
/// Immutable description of the currently visible slice of the library.
/// Additional filters (year, publisher, installed state) can be added here without touching
/// the repository contract.
/// </summary>
public sealed record LibraryQuery
{
    public LibraryScope Scope { get; init; } = LibraryScope.AllGames;

    public GenreKey? Genre { get; init; }

    public string? SearchText { get; init; }

    public GameSortOrder SortOrder { get; init; } = GameSortOrder.TitleAscending;

    public int? ReleaseYear { get; init; }

    public string? Publisher { get; init; }
}
