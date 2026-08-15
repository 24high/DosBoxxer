using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DosBoxxer.Core.Helpers;

/// <summary>
/// Derives a human readable search title from a directory or file name, and computes the
/// sort title used by the library.
/// </summary>
public static partial class TitleCleaner
{
    private static readonly string[] LeadingArticles = { "the ", "a ", "an ", "der ", "die ", "das ", "le ", "la ", "les ", "el ", "los ", "las ", "il ", "lo ", "gli " };

    /// <summary>Tokens that frequently appear in scene/abandonware directory names.</summary>
    private static readonly string[] NoiseTokens =
    {
        "cd", "cdrom", "cd-rom", "floppy", "disk", "disks", "dos", "msdos", "ms-dos",
        "gog", "abandonware", "eng", "english", "german", "repack", "iso", "full",
        "install", "installed", "setup", "rip", "v1", "v2", "final",
    };

    [GeneratedRegex(@"[_\-]+")]
    private static partial Regex WordSeparatorRegex();

    [GeneratedRegex(@"\.+")]
    private static partial Regex DotRegex();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"[\(\[\{][^\)\]\}]*[\)\]\}]")]
    private static partial Regex BracketRegex();

    [GeneratedRegex(@"^(19|20)\d{2}$")]
    private static partial Regex YearRegex();

    /// <summary>Matches version markers such as <c>v1.9</c>, <c>1.02</c> or <c>v2</c>.</summary>
    [GeneratedRegex(@"\bv?\d+(\.\d+)+\b|\bv\d+\b", RegexOptions.IgnoreCase)]
    private static partial Regex VersionRegex();

    /// <summary>
    /// Turns something like <c>DOOM_2_v1.9_[CD]</c> or <c>duke3d.dos</c> into <c>Doom 2</c> /
    /// <c>Duke3d</c>. The result is only a suggestion; the user can always edit it.
    /// </summary>
    public static string FromDirectoryName(string? directoryOrFileName)
    {
        if (string.IsNullOrWhiteSpace(directoryOrFileName))
        {
            return string.Empty;
        }

        var name = directoryOrFileName.Trim();

        // Strip an extension if a file name was passed in.
        var extension = Path.GetExtension(name);
        if (extension.Length is > 1 and <= 5)
        {
            name = Path.GetFileNameWithoutExtension(name);
        }

        name = BracketRegex().Replace(name, " ");

        // Underscores and hyphens become spaces first: they are word separators, but they are
        // also word characters for the regex engine, which would stop the version pattern below
        // from ever matching inside something like "Doom_v1.9".
        name = WordSeparatorRegex().Replace(name, " ");

        // Remove version markers while the dot is still intact, so "v1.9" disappears as a whole
        // instead of decaying into the two tokens "v1" and "9".
        name = VersionRegex().Replace(name, " ");

        name = DotRegex().Replace(name, " ");
        name = WhitespaceRegex().Replace(name, " ").Trim();

        if (name.Length == 0)
        {
            return string.Empty;
        }

        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !NoiseTokens.Contains(w.ToLowerInvariant()))
            .Where(w => !YearRegex().IsMatch(w))
            .ToArray();

        if (words.Length == 0)
        {
            words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }

        var builder = new StringBuilder();
        foreach (var word in words)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(CapitalizeWord(word));
        }

        return builder.ToString();
    }

    private static string CapitalizeWord(string word)
    {
        // Keep words that are already mixed case or all-caps acronyms untouched apart from
        // full upper case names such as "DOOM", which read better as "Doom".
        if (word.Length <= 1)
        {
            return word.ToUpperInvariant();
        }

        var allUpper = word.All(c => !char.IsLetter(c) || char.IsUpper(c));
        var allLower = word.All(c => !char.IsLetter(c) || char.IsLower(c));

        if (allUpper || allLower)
        {
            return char.ToUpper(word[0], CultureInfo.InvariantCulture) + word[1..].ToLowerInvariant();
        }

        return word;
    }

    /// <summary>
    /// Sort key: upper case, leading article removed. Used for stable, culture independent
    /// alphabetical ordering.
    /// </summary>
    public static string ToSortTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var value = title.Trim();

        foreach (var article in LeadingArticles)
        {
            if (value.StartsWith(article, StringComparison.OrdinalIgnoreCase))
            {
                value = value[article.Length..].Trim();
                break;
            }
        }

        return value.ToUpperInvariant();
    }
}
