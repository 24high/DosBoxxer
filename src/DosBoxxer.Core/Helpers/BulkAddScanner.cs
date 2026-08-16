namespace DosBoxxer.Core.Helpers;

/// <summary>
/// Enumerates the game directories for a bulk import: every immediate sub directory of the
/// chosen parent folder is treated as the root directory of one game.
/// </summary>
public static class BulkAddScanner
{
    /// <summary>
    /// Returns the absolute, normalised paths of the immediate sub directories of
    /// <paramref name="parentDirectory"/>, sorted by name and with hidden directories (those whose
    /// name starts with a dot) skipped. Returns an empty list when the parent does not exist.
    /// </summary>
    public static IReadOnlyList<string> GetGameDirectories(string? parentDirectory)
    {
        var parent = PathHelper.Normalize(parentDirectory);
        if (parent.Length == 0 || !PathHelper.DirectoryExistsSafe(parent))
        {
            return Array.Empty<string>();
        }

        string[] directories;
        try
        {
            directories = Directory.GetDirectories(parent);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }

        return directories
            .Where(d => !Path.GetFileName(d).StartsWith('.'))
            .Select(PathHelper.Normalize)
            .Where(d => d.Length > 0)
            .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
