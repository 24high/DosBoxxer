using DosBoxxer.Core.Models.Savegame;

namespace DosBoxxer.Core.Infrastructure.Savegame;

/// <summary>
/// Turns the raw path notations found in <c>dos_savegame_pfade.json</c>
/// (<c>&lt;base&gt;/SAVE</c>, <c>&lt;SPIELORDNER&gt;\SAVEGAM*.DAT</c>) into a normalised
/// game-relative <see cref="SavegameEntry"/>. Also used to validate manually entered patterns.
/// </summary>
public static class SavegamePathParser
{
    private static readonly string[] Placeholders =
    {
        "<base>", "<spielordner>", "<game>", "<root>", "<gamedir>",
    };

    /// <summary>
    /// Parses one manifest/pattern string. Returns <c>null</c> when the value is empty or, after
    /// stripping the placeholder, escapes the game directory (path traversal).
    /// </summary>
    public static SavegameEntry? Parse(string? rawPath, string? rawKind = null)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        var value = rawPath.Trim().Replace('\\', '/');

        // Strip a leading placeholder such as <base>/ or <SPIELORDNER>/.
        foreach (var placeholder in Placeholders)
        {
            if (value.StartsWith(placeholder, StringComparison.OrdinalIgnoreCase))
            {
                value = value[placeholder.Length..];
                break;
            }
        }

        value = value.TrimStart('/', ' ');

        var relative = NormalizeRelative(value);
        if (relative is null)
        {
            return null;
        }

        var isGlob = relative.Contains('*', StringComparison.Ordinal) ||
                     relative.Contains('?', StringComparison.Ordinal) ||
                     (rawKind is not null && rawKind.Contains("Glob", StringComparison.OrdinalIgnoreCase));

        return new SavegameEntry(relative, isGlob ? SavegameEntryKind.Glob : SavegameEntryKind.Path);
    }

    /// <summary>
    /// Validates a user supplied relative pattern, rejecting absolute paths, drive letters,
    /// empty segments and any <c>..</c> traversal. Returns the cleaned pattern or <c>null</c>.
    /// </summary>
    public static string? NormalizeRelative(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        var value = pattern.Trim().Replace('\\', '/');

        // Reject absolute paths and Windows drive specifiers.
        if (value.StartsWith('/') || (value.Length >= 2 && value[1] == ':'))
        {
            return null;
        }

        var segments = value.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        foreach (var segment in segments)
        {
            if (segment == "." || segment == "..")
            {
                return null;
            }
        }

        return string.Join('/', segments);
    }
}
