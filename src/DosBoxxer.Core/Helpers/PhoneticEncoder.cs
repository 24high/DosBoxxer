using System.Text;

namespace DosBoxxer.Core.Helpers;

/// <summary>
/// Compact Metaphone-style phonetic encoder. It maps a word to a consonant-skeleton key so that
/// similarly sounding titles collide: "Fallout"/"Falout", "Prince"/"Prynce", "Kwest"/"Quest".
/// It is intentionally lenient — a phonetic key match is only one of several signals combined by
/// <c>PhoneticTitleMatcher</c>, never a decision on its own.
/// </summary>
public static class PhoneticEncoder
{
    /// <summary>Encodes a single word to its phonetic key. Non-letters are ignored.</summary>
    public static string Encode(string? word)
    {
        if (string.IsNullOrWhiteSpace(word))
        {
            return string.Empty;
        }

        var letters = new StringBuilder(word.Length);
        foreach (var c in word.ToUpperInvariant())
        {
            if (c is >= 'A' and <= 'Z')
            {
                letters.Append(c);
            }
        }

        var s = letters.ToString();
        if (s.Length == 0)
        {
            return string.Empty;
        }

        var result = new StringBuilder(s.Length);
        var length = s.Length;

        // Skip silent leading pairs.
        var start = 0;
        if (length >= 2)
        {
            var lead = s[..2];
            if (lead is "AE" or "GN" or "KN" or "PN" or "WR")
            {
                start = 1;
            }
            else if (s[0] == 'X')
            {
                result.Append('S');
                start = 1;
            }
        }

        for (var i = start; i < length; i++)
        {
            var c = s[i];
            var prev = i > 0 ? s[i - 1] : '\0';
            var next = i + 1 < length ? s[i + 1] : '\0';

            // Collapse doubled letters (except C).
            if (c == prev && c != 'C')
            {
                continue;
            }

            switch (c)
            {
                case 'A' or 'E' or 'I' or 'O' or 'U':
                    // Vowels only encoded when leading.
                    if (i == start)
                    {
                        result.Append('A');
                    }

                    break;
                case 'B':
                    // Silent B at end after M ("dumb").
                    if (!(i == length - 1 && prev == 'M'))
                    {
                        result.Append('B');
                    }

                    break;
                case 'C':
                    if (next == 'H')
                    {
                        result.Append('X');
                    }
                    else if (next is 'I' or 'E' or 'Y')
                    {
                        result.Append('S');
                    }
                    else
                    {
                        result.Append('K');
                    }

                    break;
                case 'D':
                    result.Append('T');
                    break;
                case 'G':
                    if (next == 'H')
                    {
                        // Often silent ("night"); skip.
                        break;
                    }

                    result.Append(next is 'I' or 'E' or 'Y' ? 'J' : 'K');
                    break;
                case 'H':
                    // Only keep H when it is heard (vowel before and after, not part of digraph).
                    if (IsVowel(prev) && IsVowel(next))
                    {
                        result.Append('H');
                    }

                    break;
                case 'J':
                    result.Append('J');
                    break;
                case 'K':
                    if (prev != 'C')
                    {
                        result.Append('K');
                    }

                    break;
                case 'P':
                    result.Append(next == 'H' ? 'F' : 'P');
                    break;
                case 'Q':
                    result.Append('K');
                    break;
                case 'S':
                    result.Append(next == 'H' ? 'X' : 'S');
                    break;
                case 'T':
                    result.Append(next == 'H' ? '0' : 'T');
                    break;
                case 'V':
                    result.Append('F');
                    break;
                case 'W' or 'Y':
                    if (IsVowel(next))
                    {
                        result.Append(c);
                    }

                    break;
                case 'X':
                    result.Append('K');
                    result.Append('S');
                    break;
                case 'Z':
                    result.Append('S');
                    break;
                case 'F' or 'L' or 'M' or 'N' or 'R':
                    result.Append(c);
                    break;
            }
        }

        return result.ToString();
    }

    /// <summary>Encodes each token and joins the keys with a space.</summary>
    public static string EncodePhrase(IEnumerable<string> tokens) =>
        string.Join(' ', tokens.Select(Encode).Where(k => k.Length > 0));

    private static bool IsVowel(char c) => c is 'A' or 'E' or 'I' or 'O' or 'U';
}
