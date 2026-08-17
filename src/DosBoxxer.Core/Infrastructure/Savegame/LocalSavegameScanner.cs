using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Savegame;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Savegame;

/// <summary>
/// Resolves savegame declarations to actual files on disk. Directory entries are expanded
/// recursively; glob entries are matched with <c>Directory.EnumerateFiles</c> inside their
/// (optional) sub directory. Every result is verified to stay within the game directory, so a
/// malformed pattern can never reach unrelated parts of the file system.
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
        var absolute = Path.GetFullPath(Path.Combine(root, pattern.Replace('/', Path.DirectorySeparatorChar)));

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
            : Path.GetFullPath(Path.Combine(root, subDir.Replace('/', Path.DirectorySeparatorChar)));

        if (!PathHelper.IsWithin(root, searchDir) || !Directory.Exists(searchDir))
        {
            return;
        }

        // Top directory only: a glob does not recurse unless it addresses a sub directory itself.
        foreach (var file in Directory.EnumerateFiles(searchDir, mask, SearchOption.TopDirectoryOnly))
        {
            AddFile(root, file, results);
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
