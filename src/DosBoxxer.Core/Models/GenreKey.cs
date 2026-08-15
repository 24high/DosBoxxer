namespace DosBoxxer.Core.Models;

/// <summary>
/// Normalised, stable genre identifiers. The numeric value is persisted in the database and
/// must never change. Display names are resolved through the localisation service using the
/// resource key <c>Genre.&lt;EnumName&gt;</c>.
/// </summary>
public enum GenreKey
{
    Action = 1,
    Adventure = 2,
    RolePlaying = 3,
    Strategy = 4,
    Shooter = 5,
    Platformer = 6,
    Simulation = 7,
    Racing = 8,
    Sports = 9,
    Puzzle = 10,
    Fighting = 11,
    Arcade = 12,
    Casual = 13,
    Educational = 14,
    Other = 99,
}

public static class GenreKeys
{
    /// <summary>All real genres in the order they should be presented in the UI.</summary>
    public static IReadOnlyList<GenreKey> All { get; } = new[]
    {
        GenreKey.Action,
        GenreKey.Adventure,
        GenreKey.RolePlaying,
        GenreKey.Strategy,
        GenreKey.Shooter,
        GenreKey.Platformer,
        GenreKey.Simulation,
        GenreKey.Racing,
        GenreKey.Sports,
        GenreKey.Puzzle,
        GenreKey.Fighting,
        GenreKey.Arcade,
        GenreKey.Casual,
        GenreKey.Educational,
        GenreKey.Other,
    };

    /// <summary>Resource key used by the localisation service for a genre.</summary>
    public static string ResourceKey(GenreKey genre) => "Genre." + genre;
}
