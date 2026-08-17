using System.IO.Enumeration;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Savegame;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Savegame;

/// <summary>
/// Resolves savegame declarations to actual files on disk. Directory entries are expanded
/// recursively; glob entries are matched inside their (optional) sub directory — recursively when
/// they carry no sub directory prefix, because menu launchers can move the DOS working directory
/// into any sub folder of the game directory. Every result is verified to stay within the game
/// directory, so a malformed pattern can never reach unrelated parts of the file system. All
/// matching is case-insensitive: DOS file systems (and DOSBox) ignore case, so a pattern must find
/// the file regardless of the casing the game happened to write — even on case-sensitive host file
/// systems.
/// </summary>
public sealed class LocalSavegameScanner : ILocalSavegameScanner
{
    private readonly ILogger<LocalSavegameScanner> _logger;

    public LocalSavegameScanner(ILogger<LocalSavegameScanner> logger) => _logger = logger;

    public IReadOnlyList<LocalSavegameFile> Enumerate(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);

        // Savegames live in the game's DOS working directory — the folder the launch file is
        // started from — which is not necessarily the game root.
        return Enumerate(SavegameBaseDirectory.Resolve(game), game.SavegameConfig);
    }

    public IReadOnlyList<LocalSavegameFile> Enumerate(string gameDirectory, SavegameConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        var root = PathHelper.Normalize(gameDirectory);
        if (root.Length == 0 || !PathHelper.DirectoryExistsSafe(root))
        {
            return Array.Empty<LocalSavegameFile>();
        }

        // De-duplicate by relative path — several entries may overlap.
        var results = new Dictionary<string, LocalSavegameFile>(StringComparer.Ordinal);

        foreach (var entry in config.Entries)
        {
            var pattern = SavegamePathParser.NormalizeRelative(entry.RelativePattern);
            if (pattern is null)
            {
                _logger.LogWarning("Skipping invalid savegame pattern '{Pattern}'", entry.RelativePattern);
                continue;
            }

            try
            {
                if (entry.Kind == SavegameEntryKind.Glob)
                {
                    ExpandGlob(root, pattern, results);
                }
                else
                {
                    ExpandPath(root, pattern, results);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogWarning(ex, "Could not resolve savegame pattern '{Pattern}'", pattern);
            }
        }

        return results.Values
            .OrderBy(f => f.RelativePath, StringComparer.Ordinal)
            .ToList();
    }

    private void ExpandPath(string root, string pattern, Dictionary<string, LocalSavegameFile> results)
    {
        var absolute = ResolveCaseInsensitive(root, pattern);
        if (absolute is null)
        {
            return;
        }

        if (!PathHelper.IsWithin(root, absolute))
        {
            _logger.LogWarning("Rejected savegame path outside the game directory: {Path}", absolute);
            return;
        }

        if (Directory.Exists(absolute))
        {
            foreach (var file in Directory.EnumerateFiles(absolute, "*", SearchOption.AllDirectories))
            {
                AddFile(root, file, results);
            }
        }
        else if (File.Exists(absolute))
        {
            AddFile(root, absolute, results);
        }
    }

    /// <summary>
    /// Resolves a relative pattern segment by segment, falling back to a case-insensitive lookup
    /// whenever the exact casing is not present. DOS games do not observe host file name casing,
    /// so a declared <c>SAVE/highscores.dat</c> must also find <c>save/HIGHSCORES.DAT</c>.
    /// Returns <c>null</c> when any segment is absent.
    /// </summary>
    private static string? ResolveCaseInsensitive(string root, string pattern)
    {
        var current = root;

        foreach (var segment in pattern.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var direct = Path.Combine(current, segment);
            if (Directory.Exists(direct) || File.Exists(direct))
            {
                current = direct;
                continue;
            }

            try
            {
                var match = Directory
                    .EnumerateFileSystemEntries(current)
                    .FirstOrDefault(e => string.Equals(Path.GetFileName(e), segment, StringComparison.OrdinalIgnoreCase));

                if (match is null)
                {
                    return null;
                }

                current = match;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        return Path.GetFullPath(current);
    }

    private void ExpandGlob(string root, string pattern, Dictionary<string, LocalSavegameFile> results)
    {
        // Split into an optional directory prefix and the file mask.
        var lastSlash = pattern.LastIndexOf('/');
        var subDir = lastSlash >= 0 ? pattern[..lastSlash] : string.Empty;
        var mask = lastSlash >= 0 ? pattern[(lastSlash + 1)..] : pattern;

        if (mask.Length == 0)
        {
            return;
        }

        var searchDir = subDir.Length == 0
            ? root
            : ResolveCaseInsensitive(root, subDir);

        if (searchDir is null || !PathHelper.IsWithin(root, searchDir) || !Directory.Exists(searchDir))
        {
            return;
        }

        // A glob without a sub directory prefix is matched recursively from the base directory:
        // menu launchers (run.bat → "cd DREAMWEB") make the DOS working directory a sub folder of
        // the game directory, so the savegames are not necessarily next to the launch file. A glob
        // that addresses a sub directory itself stays confined to that folder. Matching is
        // case-insensitive on every platform (DOS file name semantics); the native mask overload
        // of EnumerateFiles is case-sensitive on Linux, so it cannot be used here.
        var searchOption = subDir.Length == 0 ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var file in Directory.EnumerateFiles(searchDir, "*", searchOption))
        {
            if (FileSystemName.MatchesSimpleExpression(mask, Path.GetFileName(file), ignoreCase: true))
            {
                AddFile(root, file, results);
            }
        }
    }

    private static void AddFile(string root, string absolute, Dictionary<string, LocalSavegameFile> results)
    {
        var relative = PathHelper.GetRelativePathWithin(root, absolute);
        if (relative is null)
        {
            return;
        }

        var key = relative.Replace('\\', '/');

        var info = new FileInfo(absolute);
        results[key] = new LocalSavegameFile
        {
            AbsolutePath = absolute,
            RelativePath = key,
            Size = info.Exists ? info.Length : 0,
            LastModifiedUtc = info.Exists
                ? new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero)
                : DateTimeOffset.UtcNow,
        };
    }
}
