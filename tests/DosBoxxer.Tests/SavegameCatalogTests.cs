using System.Linq;
using DosBoxxer.Core.Infrastructure.Savegame;
using DosBoxxer.Core.Models.Savegame;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DosBoxxer.Tests;

/// <summary>
/// Verifies the bundled <c>dos_savegame_pfade.json</c> is actually read and searchable, and that
/// the fuzzy search surfaces catalog entries for imperfect launcher titles.
/// </summary>
public sealed class SavegameCatalogTests
{
    private static SavegameCatalog Catalog() =>
        new(new PhoneticTitleMatcher(), NullLogger<SavegameCatalog>.Instance);

    [Fact]
    public void Catalog_LoadsManyEntriesFromEmbeddedJson()
    {
        var entries = Catalog().Entries;

        Assert.True(entries.Count > 400, $"expected the bundled catalog to load, got {entries.Count}");
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Title)));
        Assert.All(entries, e => Assert.False(string.IsNullOrWhiteSpace(e.Entry.RelativePattern)));
    }

    [Fact]
    public void FindMatches_ReturnsRankedCandidatesForImperfectTitle()
    {
        var catalog = Catalog();

        // A misspelled, article-shifted query still finds the real entry near the top.
        var matches = catalog.FindMatches("Alien Legasy");

        Assert.NotEmpty(matches);
        Assert.Contains(matches.Take(3), m => m.Title.Contains("Alien Legacy", System.StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FindByExactTitle_IgnoresCasing()
    {
        var catalog = Catalog();
        if (catalog.Entries.Count == 0)
        {
            return;
        }

        var sample = catalog.Entries[0].Title;
        Assert.NotNull(catalog.FindByExactTitle(sample.ToUpperInvariant()));
    }

    [Fact]
    public void CatalogEntries_AreEitherPathOrGlob()
    {
        var entries = Catalog().Entries;
        Assert.All(entries, e => Assert.True(e.Entry.Kind is SavegameEntryKind.Path or SavegameEntryKind.Glob));
    }
}
