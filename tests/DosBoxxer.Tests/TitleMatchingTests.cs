using System.Collections.Generic;
using System.Linq;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Infrastructure.Savegame;
using DosBoxxer.Core.Models.Savegame;
using Xunit;

namespace DosBoxxer.Tests;

/// <summary>
/// Coverage of the fault tolerant, phonetically oriented title matcher: exact names, casing,
/// punctuation, typos, phonetic similarity, Roman/Arabic numerals, "The" prefixes, several close
/// candidates and clearly different titles.
/// </summary>
public sealed class TitleMatchingTests
{
    private static readonly string[] Catalog =
    {
        "Doom", "Doom II", "Duke Nukem 3D", "The Secret of Monkey Island",
        "Monkey Island 2: LeChuck's Revenge", "Wing Commander III", "Prince of Persia",
        "Command & Conquer", "SimCity 2000", "X-Wing", "Warcraft II: Tides of Darkness",
        "Quest for Glory", "Leisure Suit Larry", "Transport Tycoon",
    };

    private static readonly PhoneticTitleMatcher Matcher = new();

    private static IReadOnlyList<CatalogEntry> Entries() =>
        Catalog.Select(t => new CatalogEntry
        {
            Title = t,
            Entry = new SavegameEntry("SAVE", SavegameEntryKind.Path),
        }).ToList();

    private static string? Best(string query) =>
        Matcher.Rank(query, Entries()).FirstOrDefault()?.Title;

    [Fact]
    public void ExactName_RanksFirstWithFullScore()
    {
        var matches = Matcher.Rank("Doom", Entries());

        Assert.Equal("Doom", matches[0].Title);
        Assert.Equal(1.0, matches[0].Score, 3);
        Assert.Equal(MatchConfidence.VeryLikely, matches[0].Confidence);
    }

    [Theory]
    [InlineData("DOOM")]
    [InlineData("doom")]
    [InlineData("  Doom  ")]
    public void CasingAndWhitespace_DoNotMatter(string query) =>
        Assert.Equal("Doom", Best(query));

    [Theory]
    [InlineData("Command and Conquer")]
    [InlineData("Command & Conquer")]
    [InlineData("command-and-conquer")]
    public void Punctuation_AmpersandAndHyphens_AreNormalised(string query) =>
        Assert.Equal("Command & Conquer", Best(query));

    [Theory]
    [InlineData("Dooom")]      // extra letter
    [InlineData("Doon")]       // single substitution
    public void Typos_StillMatchTheIntendedTitle(string query) =>
        Assert.Equal("Doom", Best(query));

    [Fact]
    public void PhoneticallySimilar_NameMatches()
    {
        // "Prynce of Persha" sounds like the real title but is spelled wrong throughout.
        Assert.Equal("Prince of Persia", Best("Prynce of Persha"));
    }

    [Theory]
    [InlineData("Doom 2", "Doom II")]
    [InlineData("Wing Commander 3", "Wing Commander III")]
    [InlineData("Warcraft 2", "Warcraft II: Tides of Darkness")]
    public void RomanAndArabicNumerals_AreEquivalent(string query, string expected) =>
        Assert.Equal(expected, Best(query));

    [Fact]
    public void LeadingArticle_IsIgnored()
    {
        Assert.Equal("The Secret of Monkey Island", Best("Secret of Monkey Island"));
        Assert.Equal("The Secret of Monkey Island", Best("Monkey Island The Secret of"));
    }

    [Fact]
    public void CompletelyDifferentTitle_IsNotConfidentlyMatched()
    {
        var matches = Matcher.Rank("Microsoft Excel Spreadsheet", Entries());

        // Either nothing is returned, or only a weak candidate — never a confident match.
        Assert.True(matches.Count == 0 || matches[0].Confidence >= MatchConfidence.Similar);
        Assert.DoesNotContain(matches, m => m.Score >= 0.72);
    }

    [Fact]
    public void SeveralCloseCandidates_AreAllRankedAndOrdered()
    {
        var matches = Matcher.Rank("Monkey Island", Entries());

        var titles = matches.Select(m => m.Title).ToList();
        Assert.Contains("The Secret of Monkey Island", titles);
        Assert.Contains("Monkey Island 2: LeChuck's Revenge", titles);

        // Scores are sorted in descending order.
        for (var i = 1; i < matches.Count; i++)
        {
            Assert.True(matches[i - 1].Score >= matches[i].Score);
        }
    }

    [Fact]
    public void NumberOfResults_IsCapped()
    {
        var matches = Matcher.Rank("a", Entries(), maxResults: 3);
        Assert.True(matches.Count <= 3);
    }

    [Fact]
    public void EmptyQuery_ReturnsNothing() =>
        Assert.Empty(Matcher.Rank("   ", Entries()));

    // ---- normaliser / helper units --------------------------------------------------------

    [Theory]
    [InlineData("III", 3)]
    [InlineData("iv", 4)]
    [InlineData("XII", 12)]
    public void RomanNumerals_Convert(string token, int expected)
    {
        Assert.True(RomanNumerals.TryToArabic(token, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("iiii")]    // non-canonical (should be IV)
    [InlineData("vv")]      // non-canonical (should be X)
    [InlineData("ic")]      // non-canonical (should be XCIX)
    [InlineData("hello")]   // contains non-Roman letters
    public void RomanNumerals_RejectNonCanonicalWords(string token) =>
        Assert.False(RomanNumerals.TryToArabic(token, out _));

    [Fact]
    public void Normalizer_DoesNotConvertWordLikeRomanTokens()
    {
        // "civ" is technically Roman for 104, but must not become a number in a title.
        Assert.Equal("civ", TitleNormalizer.NormalizeCompact("Civ"));
    }

    [Fact]
    public void Normalizer_FoldsDiacriticsAndCompacts()
    {
        Assert.Equal("wolfenstein 3", TitleNormalizer.NormalizeCompact("Wölfenstein III"));
        Assert.Equal("secret of monkey island", TitleNormalizer.NormalizeCompact("The Secret of Monkey Island"));
    }
}
