using System.Text.RegularExpressions;

namespace DosBoxxer.Core.Helpers;

/// <summary>
/// Removes credentials from strings before they reach the log. ScreenScraper embeds
/// <c>devpassword</c> and <c>sspassword</c> in every request URL and in the media URLs it
/// returns, so nothing containing a URL may be logged unsanitised.
/// </summary>
public static partial class LogSanitizer
{
    private const string Mask = "***";

    [GeneratedRegex(
        @"(?<key>devpassword|sspassword|password|devid|ssid|apikey|api_key)=(?<value>[^&\s""]*)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SecretParameterRegex();

    public static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return SecretParameterRegex().Replace(value, m => m.Groups["key"].Value + "=" + Mask);
    }

    public static string Sanitize(Uri? uri) => uri is null ? string.Empty : Sanitize(uri.ToString());
}
