using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models.Savegame;

namespace DosBoxxer.Core.Infrastructure.Savegame;

/// <summary>
/// Fault tolerant title matcher combining several independent signals into one ranking:
/// <list type="bullet">
/// <item>normalised string similarity (Jaro-Winkler + Levenshtein ratio),</item>
/// <item>token set overlap (order independent, article/noise tolerant),</item>
/// <item>a phonetic key comparison (similar sounding names).</item>
/// </list>
/// The weighted blend, plus a small exact/prefix bonus, yields a stable score in <c>[0,1]</c>.
/// No candidate is ever selected automatically; the score only drives the ordering the user sees.
/// </summary>
public sealed class PhoneticTitleMatcher : ITitleMatcher
{
    /// <summary>Candidates below this blended score are not reported at all.</summary>
    private const double MinimumScore = 0.34;

    public IReadOnlyList<TitleMatch> Rank(string query, IReadOnlyList<CatalogEntry> candidates, int maxResults = 8)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (string.IsNullOrWhiteSpace(query) || candidates.Count == 0)
        {
            return Array.Empty<TitleMatch>();
        }

        var queryCompact = TitleNormalizer.NormalizeCompact(query);
        var queryTokens = TitleNormalizer.Tokenize(query);
        var queryPhonetic = PhoneticEncoder.EncodePhrase(queryTokens);

        if (queryCompact.Length == 0)
        {
            return Array.Empty<TitleMatch>();
        }

        var scored = new List<TitleMatch>(candidates.Count);

        foreach (var candidate in candidates)
        {
            var score = Score(candidate.Title, queryCompact, queryTokens, queryPhonetic);
            if (score >= MinimumScore)
            {
                scored.Add(new TitleMatch
                {
                    Catalog = candidate,
                    Score = score,
                    Confidence = Classify(score),
                });
            }
        }

        return scored
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Title, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, maxResults))
            .ToList();
    }

    /// <summary>Scores one candidate title against the pre-processed query. Exposed for testing.</summary>
    public static double Score(string candidateTitle, string queryCompact, IReadOnlyList<string> queryTokens, string queryPhonetic)
    {
        var candidateCompact = TitleNormalizer.NormalizeCompact(candidateTitle);
        if (candidateCompact.Length == 0)
        {
            return 0.0;
        }

        // Exact normalised equality is the strongest possible signal.
        if (string.Equals(candidateCompact, queryCompact, StringComparison.Ordinal))
        {
            return 1.0;
        }

        var candidateTokens = TitleNormalizer.Tokenize(candidateTitle);
        var candidatePhonetic = PhoneticEncoder.EncodePhrase(candidateTokens);

        var jaroWinkler = StringSimilarity.JaroWinkler(candidateCompact, queryCompact);
        var levenshtein = StringSimilarity.LevenshteinRatio(candidateCompact, queryCompact);
        var stringScore = 0.6 * jaroWinkler + 0.4 * levenshtein;

        var tokenScore = StringSimilarity.TokenSetJaccard(candidateTokens, queryTokens);

        var phoneticScore = candidatePhonetic.Length == 0 || queryPhonetic.Length == 0
            ? 0.0
            : StringSimilarity.JaroWinkler(candidatePhonetic, queryPhonetic);

        // Weighted blend: string form leads, tokens and phonetics reinforce it.
        var blended = 0.5 * stringScore + 0.3 * tokenScore + 0.2 * phoneticScore;

        // Containment bonus: one title fully contains the other (e.g. subtitle differences).
        if (candidateCompact.Contains(queryCompact, StringComparison.Ordinal) ||
            queryCompact.Contains(candidateCompact, StringComparison.Ordinal))
        {
            blended = Math.Min(1.0, blended + 0.1);
        }

        return blended;
    }

    private static MatchConfidence Classify(double score) => score switch
    {
        >= 0.88 => MatchConfidence.VeryLikely,
        >= 0.72 => MatchConfidence.Likely,
        >= 0.50 => MatchConfidence.Similar,
        _ => MatchConfidence.Weak,
    };
}
