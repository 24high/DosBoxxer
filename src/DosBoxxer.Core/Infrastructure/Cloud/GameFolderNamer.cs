using DosBoxxer.Core.Helpers;

namespace DosBoxxer.Core.Infrastructure.Cloud;

/// <summary>
/// Naming scheme for the per-game cloud folder: <c>&lt;Title&gt;-&lt;id8&gt;</c>, where
/// <c>id8</c> is the first 8 hex characters of the game id (e.g. <c>Dreamweb-bbcee5a7</c>).
/// The title makes the folder recognisable in the Drive UI and — more importantly — enables
/// recovery after a reinstall: a fresh library assigns new game ids, but an existing folder can
/// be adopted again by its unambiguous title prefix. The id suffix keeps same-titled games
/// apart, so two different games can never share one folder.
/// </summary>
public static class GameFolderNamer
{
    /// <summary>The canonical folder name for a game.</summary>
    public static string FolderName(Guid gameId, string title) =>
        $"{TitlePrefix(title)}{gameId.ToString("N")[..8]}";

    /// <summary>The prefix (<c>&lt;Title&gt;-</c>) that identifies all folder generations of a game.</summary>
    public static string TitlePrefix(string title) =>
        PathHelper.SanitizeFileName(title) + "-";

    /// <summary>
    /// Picks the folder to adopt for a fresh (re)installation: exactly one folder whose name
    /// starts with the title prefix. Returns <c>null</c> when there is no match — or several,
    /// because guessing between ambiguous candidates could mix the savegames of two games.
    /// </summary>
    public static string? PickAdoptableFolder(string title, IReadOnlyList<(string Id, string Name)> folders)
    {
        var prefix = TitlePrefix(title);
        string? found = null;

        foreach (var (id, name) in folders)
        {
            if (!name.StartsWith(prefix, StringComparison.Ordinal))
            {
                continue;
            }

            if (found is not null)
            {
                return null; // Ambiguous — never guess.
            }

            found = id;
        }

        return found;
    }
}
