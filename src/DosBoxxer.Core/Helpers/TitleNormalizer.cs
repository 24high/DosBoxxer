using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DosBoxxer.Core.Helpers;

/// <summary>
/// Turns a raw game title into a normalised, comparable form and its tokens. This is the shared
/// pre-processing step that makes the fuzzy/phonetic matcher tolerant of spelling, punctuation,
/// hyphenation, spacing, Roman vs. Arabic numerals and leading articles such as "The".
/// </summary>
public static partial class TitleNormalizer
{
    private static readonly string[] LeadingArticles = { "the", "a", "an", "der", "die", "das", "le", "la", "les", "el", "los", "las", "il", "lo" };

    // Very common edition/noise words that should not dominate a match.
    private static readonly HashSet<string> NoiseTokens = new(StringComparer.Ordinal)
    {
        "cd", "cdrom", "floppy", "disk", "dos", "msdos", "edition", "deluxe", "gold",
        "special", "collectors", "collector", "remastered", "hd", "the", "vga", "ega",
    };

    [GeneratedRegex(@"[^a-z0-9]+")]
    private static partial Regex NonAlnumRegex();

    /// <summary>
    /// Full normalisation: diacritics folded, ampersands expanded, Roman numerals converted to
    /// Arabic, punctuation collapsed to single spaces, lower-cased and trimmed. Leading articles
    /// are NOT removed here (that happens per-token) so the raw normalised string stays faithful.
    /// </summary>
    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var value = title.Trim().ToLowerInvariant();
        value = value.Replace("&", " and ", StringComparison.Ordinal);
        value = value.Replace("+", " and ", StringComparison.Ordinal);

        value = FoldDiacritics(value);
        value = NonAlnumRegex().Replace(value, " ").Trim();

        // Convert stand-alone Roman numerals token by token.
        var tokens = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var builder = new StringBuilder();
        foreach (var token in tokens)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            // Only convert small Roman numerals (<= 39). This captures game sequels (II..XXXIX)
            // while avoiding false positives on word-like tokens that happen to be valid Roman
            // numbers ("civ" = 104, "mix" = 1009).
            builder.Append(RomanNumerals.TryToArabic(token, out var arabic) && arabic <= 39
                ? arabic.ToString(CultureInfo.InvariantCulture)
                : token);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Significant tokens of the normalised title: leading articles and common noise words are
    /// dropped so "The Secret of Monkey Island" and "Secret of Monkey Island" compare equal on
    /// the token axis. If every token is noise, the original tokens are kept.
    /// </summary>
    public static IReadOnlyList<string> Tokenize(string? title)
    {
        var normalized = Normalize(title);
        if (normalized.Length == 0)
        {
            return Array.Empty<string>();
        }

        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var significant = new List<string>(tokens.Length);
        for (var i = 0; i < tokens.Length; i++)
        {
            var token = tokens[i];

            // Drop a leading article only in first position.
            if (i == 0 && LeadingArticles.Contains(token, StringComparer.Ordinal))
            {
                continue;
            }

            if (NoiseTokens.Contains(token))
            {
                continue;
            }

            significant.Add(token);
        }

        return significant.Count > 0 ? significant : tokens;
    }

    /// <summary>The significant tokens joined by single spaces — the "compact" comparison key.</summary>
    public static string NormalizeCompact(string? title) => string.Join(' ', Tokenize(title));

    private static string FoldDiacritics(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var c in decomposed)
        {
            switch (CharUnicodeInfo.GetUnicodeCategory(c))
            {
                case UnicodeCategory.NonSpacingMark:
                    continue;
                default:
                    // Common German transliterations that FormD does not split.
                    builder.Append(c switch
                    {
                        'ß' => "ss",
                        'ø' => "o",
                        'æ' => "ae",
                        'œ' => "oe",
                        'đ' => "d",
                        'ł' => "l",
                        _ => c.ToString(),
                    });
                    break;
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
