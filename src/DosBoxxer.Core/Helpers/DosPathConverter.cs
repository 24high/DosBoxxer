using System.Text;

namespace DosBoxxer.Core.Helpers;

public sealed record DosPathConversion(string DosPath, IReadOnlyList<string> MangledNames);

/// <summary>
/// Converts host paths that live inside a mounted directory into paths usable inside DOSBox.
///
/// Two rules matter:
/// <list type="bullet">
/// <item>The host separator must become a backslash, because the guest is DOS.</item>
/// <item>DOSBox' emulated DOS is 8.3 only. Names longer than 8 characters (or with an
/// extension longer than 3, or containing characters DOS rejects) are exposed to the guest in
/// mangled <c>NAME~1.EXT</c> form. We reproduce the common mangling so <c>cd</c> works, and
/// report every mangled name so the UI can warn the user.</item>
/// </list>
/// </summary>
public static class DosPathConverter
{
    /// <summary>Characters DOS accepts in a file name in addition to letters and digits.</summary>
    private const string AllowedSpecials = "!#$%&'()-@^_`{}~";

    /// <summary>
    /// Converts a host-relative path (e.g. <c>BIN/DATA/GAME.EXE</c>) into a DOS path
    /// (<c>BIN\DATA\GAME.EXE</c>), shortening every segment to 8.3 when necessary.
    /// </summary>
    public static DosPathConversion ConvertRelativePath(string relativePath)
    {
        var mangled = new List<string>();

        if (string.IsNullOrWhiteSpace(relativePath) || relativePath == ".")
        {
            return new DosPathConversion(string.Empty, mangled);
        }

        var segments = relativePath.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar, '/', '\\' },
            StringSplitOptions.RemoveEmptyEntries);

        var converted = new List<string>(segments.Length);

        foreach (var segment in segments)
        {
            if (segment == ".")
            {
                continue;
            }

            var dosName = ToDosName(segment);
            if (!string.Equals(dosName, segment.ToUpperInvariant(), StringComparison.Ordinal))
            {
                mangled.Add(segment);
            }

            converted.Add(dosName);
        }

        return new DosPathConversion(string.Join('\\', converted), mangled);
    }

    /// <summary>
    /// Converts a single file or directory name to its DOS 8.3 representation.
    /// The result is always upper case.
    /// </summary>
    public static string ToDosName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var trimmed = name.Trim().TrimEnd('.');
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var lastDot = trimmed.LastIndexOf('.');

        // A leading dot is part of the base name for DOS purposes (".config" -> "_CONFIG").
        string baseName;
        string extension;
        if (lastDot > 0)
        {
            baseName = trimmed[..lastDot];
            extension = trimmed[(lastDot + 1)..];
        }
        else
        {
            baseName = trimmed;
            extension = string.Empty;
        }

        var cleanBase = CleanSegment(baseName);
        var cleanExtension = CleanSegment(extension);

        if (cleanBase.Length == 0)
        {
            cleanBase = "_";
        }

        if (cleanBase.Length > 8)
        {
            cleanBase = cleanBase[..6] + "~1";
        }

        if (cleanExtension.Length > 3)
        {
            cleanExtension = cleanExtension[..3];
        }

        return cleanExtension.Length == 0 ? cleanBase : cleanBase + "." + cleanExtension;
    }

    private static string CleanSegment(string value)
    {
        if (value.Length == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var raw in value)
        {
            var c = char.ToUpperInvariant(raw);
            if (IsAllowed(c))
            {
                builder.Append(c);
            }
            else if (c is ' ' or '.')
            {
                // Spaces and additional dots are dropped, matching DOS short name generation.
                continue;
            }
            else
            {
                builder.Append('_');
            }
        }

        return builder.ToString();
    }

    private static bool IsAllowed(char c) =>
        (c >= 'A' && c <= 'Z') ||
        (c >= '0' && c <= '9') ||
        AllowedSpecials.Contains(c, StringComparison.Ordinal);

    /// <summary>
    /// Quotes a host path for use as the argument of DOSBox' <c>mount</c> command.
    /// Double quotes cannot be escaped inside the DOSBox shell, so a path containing one is
    /// rejected by returning <c>null</c>.
    /// </summary>
    public static string? QuoteHostPathForMount(string hostPath)
    {
        if (string.IsNullOrWhiteSpace(hostPath))
        {
            return null;
        }

        if (hostPath.Contains('"', StringComparison.Ordinal))
        {
            return null;
        }

        return "\"" + hostPath + "\"";
    }
}
