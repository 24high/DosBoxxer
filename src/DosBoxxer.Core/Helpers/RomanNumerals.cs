namespace DosBoxxer.Core.Helpers;

/// <summary>
/// Converts stand-alone Roman numeral tokens (I..XXXIX and beyond) to Arabic numbers so that
/// "Wing Commander III" and "Wing Commander 3" compare equal. Only pure Roman tokens are
/// converted; ordinary words that merely start with valid letters (e.g. "civ", "mix") are left
/// untouched because they are validated against a canonical round-trip.
/// </summary>
public static class RomanNumerals
{
    public static bool TryToArabic(string token, out int value)
    {
        value = 0;
        if (string.IsNullOrEmpty(token) || token.Length > 15)
        {
            return false;
        }

        var upper = token.ToUpperInvariant();
        var total = 0;
        var previous = 0;

        for (var i = upper.Length - 1; i >= 0; i--)
        {
            var digit = upper[i] switch
            {
                'I' => 1,
                'V' => 5,
                'X' => 10,
                'L' => 50,
                'C' => 100,
                'D' => 500,
                'M' => 1000,
                _ => -1,
            };

            if (digit < 0)
            {
                return false;
            }

            if (digit < previous)
            {
                total -= digit;
            }
            else
            {
                total += digit;
                previous = digit;
            }
        }

        if (total <= 0)
        {
            return false;
        }

        // Reject non-canonical spellings such as "IIII" or "IC" by requiring a round trip.
        if (!string.Equals(ToRoman(total), upper, StringComparison.Ordinal))
        {
            return false;
        }

        value = total;
        return true;
    }

    private static string ToRoman(int number)
    {
        if (number is <= 0 or > 3999)
        {
            return string.Empty;
        }

        var pairs = new (int Value, string Symbol)[]
        {
            (1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"),
            (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I"),
        };

        var builder = new System.Text.StringBuilder();
        foreach (var (value, symbol) in pairs)
        {
            while (number >= value)
            {
                builder.Append(symbol);
                number -= value;
            }
        }

        return builder.ToString();
    }
}
