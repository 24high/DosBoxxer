namespace DosBoxxer.Core.Models;

/// <summary>
/// Identifies individual metadata fields of a <see cref="Game"/>.
/// Used to remember which fields the user edited manually so that a later metadata refresh
/// does not silently overwrite them, and to let the user pick the fields to refresh.
/// </summary>
[Flags]
public enum GameField
{
    None = 0,
    Title = 1 << 0,
    Publisher = 1 << 1,
    Developer = 1 << 2,
    ReleaseYear = 1 << 3,
    Description = 1 << 4,
    Genres = 1 << 5,
    Cover = 1 << 6,
    Screenshots = 1 << 7,
    Players = 1 << 8,

    All = Title | Publisher | Developer | ReleaseYear | Description | Genres | Cover | Screenshots | Players,
}

public static class GameFields
{
    public static IReadOnlyList<GameField> Individual { get; } = new[]
    {
        GameField.Title,
        GameField.Publisher,
        GameField.Developer,
        GameField.ReleaseYear,
        GameField.Description,
        GameField.Genres,
        GameField.Cover,
        GameField.Screenshots,
        GameField.Players,
    };

    public static string ResourceKey(GameField field) => "Field." + field;
}
