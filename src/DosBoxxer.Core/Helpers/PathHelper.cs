using System.Runtime.InteropServices;

namespace DosBoxxer.Core.Helpers;

/// <summary>
/// Host file system path helpers. Everything here works with the platform separator; DOS
/// specific conversions live in <see cref="DosPathConverter"/>.
/// </summary>
public static class PathHelper
{
    private static readonly StringComparison PathComparison =
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

    /// <summary>Case sensitivity used when comparing host paths on the current platform.</summary>
    public static StringComparer PathComparer =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            ? StringComparer.Ordinal
            : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Returns an absolute path without a trailing separator (except for a filesystem root).
    /// Returns an empty string for null/whitespace input instead of throwing.
    /// </summary>
    public static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path.Trim());
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }

        return TrimTrailingSeparator(full);
    }

    public static string TrimTrailingSeparator(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var root = Path.GetPathRoot(path);
        if (!string.IsNullOrEmpty(root) && string.Equals(path, root, PathComparison))
        {
            return path;
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// Computes the path of <paramref name="fullPath"/> relative to <paramref name="baseDirectory"/>.
    /// Returns <c>null</c> when the file is not located below the base directory.
    /// </summary>
    public static string? GetRelativePathWithin(string baseDirectory, string fullPath)
    {
        var normalizedBase = Normalize(baseDirectory);
        var normalizedFull = Normalize(fullPath);

        if (normalizedBase.Length == 0 || normalizedFull.Length == 0)
        {
            return null;
        }

        var relative = Path.GetRelativePath(normalizedBase, normalizedFull);

        if (Path.IsPathRooted(relative))
        {
            return null;
        }

        if (relative == "." ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
            relative == "..")
        {
            return null;
        }

        return relative;
    }

    /// <summary>True when <paramref name="candidate"/> is the base directory itself or below it.</summary>
    public static bool IsWithin(string baseDirectory, string candidate)
    {
        var normalizedBase = Normalize(baseDirectory);
        var normalizedCandidate = Normalize(candidate);

        if (normalizedBase.Length == 0 || normalizedCandidate.Length == 0)
        {
            return false;
        }

        if (string.Equals(normalizedBase, normalizedCandidate, PathComparison))
        {
            return true;
        }

        return normalizedCandidate.StartsWith(
            normalizedBase + Path.DirectorySeparatorChar,
            PathComparison);
    }

    /// <summary>
    /// Characters that are illegal in a file name on at least one supported platform.
    /// A fixed set is used rather than <see cref="Path.GetInvalidFileNameChars"/> because that
    /// method only reports <c>/</c> and NUL on Unix — a name sanitised on Linux would then be
    /// unusable if the data directory were copied to Windows.
    /// </summary>
    private const string InvalidFileNameChars = "<>:\"/\\|?*";

    /// <summary>Replaces every character that is invalid in a file name with '_'.</summary>
    public static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "unnamed";
        }

        Span<char> buffer = stackalloc char[name.Length];
        var length = 0;

        foreach (var c in name)
        {
            var isInvalid = char.IsControl(c) || InvalidFileNameChars.Contains(c, StringComparison.Ordinal);
            buffer[length++] = isInvalid ? '_' : c;
        }

        var result = new string(buffer[..length]).Trim().Trim('.');
        return result.Length == 0 ? "unnamed" : result;
    }

    public static bool FileExistsSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return File.Exists(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool DirectoryExistsSafe(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            return Directory.Exists(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
