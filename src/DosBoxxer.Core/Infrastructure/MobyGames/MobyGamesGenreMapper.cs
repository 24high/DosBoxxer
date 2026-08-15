using DosBoxxer.Core.Infrastructure.MobyGames.Dto;
using DosBoxxer.Core.Infrastructure.ScreenScraper;
using DosBoxxer.Core.Models;

namespace DosBoxxer.Core.Infrastructure.MobyGames;

/// <summary>
/// Maps MobyGames genres onto the launcher's normalised <see cref="GenreKey"/> values.
///
/// MobyGames splits genres into categories (Basic Genres, Perspective, Setting, …). Only the
/// gameplay-defining ones are useful for our buckets, so mapping prefers the
/// <c>Basic Genres</c> category (<c>genre_category_id == 1</c>) and the <c>Gameplay</c> category.
/// The actual label matching reuses the keyword based <see cref="GenreMapper"/>, because the
/// English MobyGames names ("Action", "Role-Playing (RPG)", "Racing / Driving", …) are exactly
/// what that mapper already understands.
/// </summary>
public static class MobyGamesGenreMapper
{
    private const int BasicGenresCategoryId = 1;

    public static IReadOnlyList<GenreKey> MapAll(IEnumerable<MobyGenre>? genres)
    {
        var list = genres?.ToList() ?? new List<MobyGenre>();
        if (list.Count == 0)
        {
            return System.Array.Empty<GenreKey>();
        }

        // First pass: only the categories that describe what the game *is*.
        var gameplayNames = list
            .Where(g => g.GenreCategoryId == BasicGenresCategoryId ||
                        string.Equals(g.GenreCategory, "Gameplay", System.StringComparison.OrdinalIgnoreCase))
            .Select(g => g.GenreName)
            .ToList();

        var mapped = GenreMapper.MapAll(gameplayNames);
        if (mapped.Count > 0)
        {
            return mapped;
        }

        // Fallback: try every reported label before giving up.
        return GenreMapper.MapAll(list.Select(g => g.GenreName));
    }
}
