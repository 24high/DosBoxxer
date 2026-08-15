using DosBoxxer.Core.Models;

namespace DosBoxxer.Core.Infrastructure.ScreenScraper;

/// <summary>
/// Maps free-text genre labels (ScreenScraper delivers them per language, e.g. "Shoot'em Up",
/// "Jeu de rôle", "Plate-forme") onto the launcher's normalised <see cref="GenreKey"/> values.
///
/// Matching is keyword based and evaluated in order, because provider labels are compound
/// strings such as "Action / Platform" or "Racing, Driving". The first rule that matches wins,
/// so more specific rules are listed before broader ones.
/// </summary>
public static class GenreMapper
{
    private static readonly (string[] Keywords, GenreKey Genre)[] Rules =
    {
        // -- specific shooter variants before the generic "action" rule -------------------
        (new[] { "shoot", "shmup", "fps", "first-person shooter", "first person shooter", "run and gun", "tir" }, GenreKey.Shooter),

        (new[] { "platform", "plate-forme", "plateforme", "plataforma", "piattaforme", "jump" }, GenreKey.Platformer),

        (new[] { "fight", "versus fighting", "beat", "combat", "lucha", "prügel" }, GenreKey.Fighting),

        (new[] { "role playing", "role-playing", "rpg", "jeu de rôle", "jeu de role", "rol", "ruolo", "rollenspiel" }, GenreKey.RolePlaying),

        (new[] { "strategy", "stratégie", "strategie", "estrategia", "strategia", "wargame", "tactic", "tactique", "4x", "management", "gestion" }, GenreKey.Strategy),

        (new[] { "adventure", "aventure", "aventura", "avventura", "point-and-click", "point and click", "graphic adventure", "text adventure", "interactive fiction", "visual novel", "abenteuer" }, GenreKey.Adventure),

        (new[] { "racing", "driving", "course", "carrera", "corse", "rennspiel", "kart", "rally", "formula" }, GenreKey.Racing),

        (new[] { "sport", "deporte", "football", "soccer", "golf", "tennis", "basketball", "baseball", "hockey", "boxing", "skate", "ski" }, GenreKey.Sports),

        (new[] { "puzzle", "réflexion", "reflexion", "logic", "logik", "rompecabezas", "match-3", "sokoban", "tetris" }, GenreKey.Puzzle),

        (new[] { "simulation", "simulator", "flight", "vol", "vuelo", "simulazione", "life sim", "train", "truck", "farming" }, GenreKey.Simulation),

        (new[] { "educat", "éducat", "educa", "learning", "lernspiel", "edutainment", "typing" }, GenreKey.Educational),

        (new[] { "casual", "party", "board", "card", "casino", "quiz", "pinball", "trivia", "plateau", "cartes", "brett", "karten" }, GenreKey.Casual),

        (new[] { "arcade", "maze", "labyrinth", "breakout", "pac", "classic" }, GenreKey.Arcade),

        // -- generic bucket last ----------------------------------------------------------
        (new[] { "action", "acción", "azione", "aktion", "stealth", "survival horror", "hack" }, GenreKey.Action),
    };

    /// <summary>Maps one provider label; returns <c>null</c> when nothing matches.</summary>
    public static GenreKey? Map(string? providerGenre)
    {
        if (string.IsNullOrWhiteSpace(providerGenre))
        {
            return null;
        }

        var value = providerGenre.ToLowerInvariant();

        foreach (var (keywords, genre) in Rules)
        {
            foreach (var keyword in keywords)
            {
                if (value.Contains(keyword, StringComparison.Ordinal))
                {
                    return genre;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Maps a set of provider labels onto distinct normalised genres, preserving the order in
    /// which they were reported. Unmapped labels contribute <see cref="GenreKey.Other"/> only
    /// when nothing else could be resolved, so a game is never left without a genre.
    /// </summary>
    public static IReadOnlyList<GenreKey> MapAll(IEnumerable<string?> providerGenres)
    {
        var result = new List<GenreKey>();
        var sawAnyLabel = false;

        foreach (var label in providerGenres)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                continue;
            }

            sawAnyLabel = true;

            // A compound label such as "Action / Platform" should produce both genres.
            foreach (var part in SplitLabel(label))
            {
                var mapped = Map(part);
                if (mapped.HasValue && !result.Contains(mapped.Value))
                {
                    result.Add(mapped.Value);
                }
            }
        }

        if (result.Count == 0 && sawAnyLabel)
        {
            result.Add(GenreKey.Other);
        }

        return result;
    }

    private static IEnumerable<string> SplitLabel(string label)
    {
        var parts = label.Split(new[] { '/', ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? new[] { label } : parts;
    }
}
