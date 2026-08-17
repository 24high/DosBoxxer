using DosBoxxer.Core.Models.Savegame;

namespace DosBoxxer.Core.Abstractions;

/// <summary>
/// Fault tolerant, phonetically oriented title matcher. Pure and side-effect free so it can be
/// unit tested without any catalog or file access.
/// </summary>
public interface ITitleMatcher
{
    /// <summary>
    /// Scores <paramref name="query"/> against every candidate and returns the ranked matches,
    /// most similar first, limited to <paramref name="maxResults"/> and to a minimum score.
    /// </summary>
    IReadOnlyList<TitleMatch> Rank(string query, IReadOnlyList<CatalogEntry> candidates, int maxResults = 8);
}
