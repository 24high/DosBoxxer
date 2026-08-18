using System.Text.RegularExpressions;

namespace DosBoxxer.Core.Helpers;

/// <summary>
/// Derives the matching key used by the cloud game index (<c>dosboxxer/index.json</c>) from a game
/// title or folder name: lower-cased, diacritics folded, and every character that is not <c>a-z</c>
/// or <c>0-9</c> removed — umlauts, special characters and spaces are all stripped
/// ("The Secret of Monkey Island" → "thesecretofmonkeyisland", "Mäxchen's Abenteuer" → "maxchensabenteuer").
/// The same key is computed for the title from the DosBoxxer settings, for the title from the
/// savegame catalog configuration and for the folder name of the game's main directory, so a game
/// can be re-identified after a reinstallation.
/// </summary>
public static partial class CloudGameKey
{
    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonKeyCharsRegex();

    /// <summary>Normalises <paramref name="title"/> to its index key; empty input yields an empty key.</summary>
    public static string Normalize(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return string.Empty;
        }

        var folded = TitleNormalizer.FoldDiacritics(title.Trim().ToLowerInvariant());
        return NonKeyCharsRegex().Replace(folded, string.Empty);
    }
}
