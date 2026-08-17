using DosBoxxer.Core.Models.Savegame;

namespace DosBoxxer.Core.Abstractions;

/// <summary>
/// Read-only access to the bundled savegame path catalog (<c>dos_savegame_pfade.json</c>) and
/// fuzzy title search over it. Loading is lazy and cached; failures degrade to an empty catalog
/// so the rest of the launcher keeps working.
/// </summary>
public interface ISavegameCatalog
{
    /// <summary>All parsed catalog entries. Never <c>null</c>; empty when the file could not be read.</summary>
    IReadOnlyList<CatalogEntry> Entries { get; }

    /// <summary>
    /// Ranks catalog titles against <paramref name="query"/> and returns the best
    /// <paramref name="maxResults"/> candidates, most likely first. The launcher title need not
    /// match the catalog title exactly.
    /// </summary>
    IReadOnlyList<TitleMatch> FindMatches(string query, int maxResults = 8);

    /// <summary>Returns the entry whose title equals <paramref name="title"/> (case/whitespace-insensitive), if any.</summary>
    CatalogEntry? FindByExactTitle(string title);
}
