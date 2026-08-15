using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure;

/// <summary>
/// Recursively finds DOS launchable files (.exe, .bat, .com) below a game directory and ranks
/// them heuristically. The ranking only changes the presentation order — the user always picks
/// the starter explicitly.
/// </summary>
public sealed class ExecutableScanner : IExecutableScanner
{
    /// <summary>Hard limit so a mistakenly selected root directory cannot hang the wizard.</summary>
    private const int MaxResults = 2000;

    private const int MaxDepth = 12;

    private static readonly string[] UtilityNames =
    {
        "setup", "install", "instal", "config", "configur", "uninstall", "unins",
        "readme", "read", "order", "vendor", "help", "regist", "register",
        "cwsdpmi", "dos4gw", "dos32a", "univbe", "sbset", "modem", "patch",
        "makecd", "cdinst", "insthelp",
    };

    private static readonly string[] StrongGameNames =
    {
        "start", "play", "run", "game", "launch", "go",
    };

    private readonly ILogger<ExecutableScanner> _logger;

    public ExecutableScanner(ILogger<ExecutableScanner> logger) => _logger = logger;

    public Task<IReadOnlyList<ExecutableCandidate>> ScanAsync(
        string rootDirectory,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => Scan(rootDirectory, cancellationToken), cancellationToken);

    internal IReadOnlyList<ExecutableCandidate> Scan(string rootDirectory, CancellationToken cancellationToken)
    {
        var root = PathHelper.Normalize(rootDirectory);
        var results = new List<ExecutableCandidate>();

        if (root.Length == 0 || !Directory.Exists(root))
        {
            _logger.LogWarning("Scan requested for a directory that does not exist");
            return results;
        }

        var directoryName = Path.GetFileName(root);
        var pending = new Queue<(string Path, int Depth)>();
        pending.Enqueue((root, 0));

        while (pending.Count > 0 && results.Count < MaxResults)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (current, depth) = pending.Dequeue();

            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                _logger.LogDebug("Skipping unreadable directory during scan");
                continue;
            }

            foreach (var file in files)
            {
                if (results.Count >= MaxResults)
                {
                    break;
                }

                var kind = ClassifyExtension(Path.GetExtension(file));
                if (kind is null)
                {
                    continue;
                }

                var relative = PathHelper.GetRelativePathWithin(root, file);
                if (relative is null)
                {
                    continue;
                }

                var fileName = Path.GetFileName(file);
                long size = 0;
                try
                {
                    size = new FileInfo(file).Length;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Size is cosmetic; ignore.
                }

                var isUtility = IsUtility(fileName);

                results.Add(new ExecutableCandidate
                {
                    FullPath = file,
                    RelativePath = relative,
                    FileName = fileName,
                    Kind = kind.Value,
                    SizeBytes = size,
                    LooksLikeUtility = isUtility,
                    Score = ComputeScore(fileName, kind.Value, depth, size, isUtility, directoryName),
                });
            }

            if (depth >= MaxDepth)
            {
                continue;
            }

            try
            {
                foreach (var directory in Directory.GetDirectories(current))
                {
                    var name = Path.GetFileName(directory);
                    if (name.StartsWith('.'))
                    {
                        continue;
                    }

                    pending.Enqueue((directory, depth + 1));
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                _logger.LogDebug("Skipping unreadable sub directory during scan");
            }
        }

        _logger.LogInformation("Executable scan finished with {Count} candidate(s)", results.Count);

        return results
            .OrderByDescending(c => c.Score)
            .ThenBy(c => c.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static ExecutableKind? ClassifyExtension(string extension) =>
        extension.ToLowerInvariant() switch
        {
            ".exe" => ExecutableKind.Exe,
            ".com" => ExecutableKind.Com,
            ".bat" => ExecutableKind.Bat,
            _ => null,
        };

    internal static bool IsUtility(string fileName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        return UtilityNames.Any(u => stem.Equals(u, StringComparison.Ordinal) || stem.StartsWith(u, StringComparison.Ordinal));
    }

    internal static int ComputeScore(
        string fileName,
        ExecutableKind kind,
        int depth,
        long sizeBytes,
        bool isUtility,
        string gameDirectoryName)
    {
        var stem = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
        var score = 100;

        if (isUtility)
        {
            score -= 200;
        }

        // Files sitting directly in the game root are more likely to be the starter.
        score -= depth * 12;

        score += kind switch
        {
            ExecutableKind.Exe => 15,
            ExecutableKind.Bat => 10,
            ExecutableKind.Com => 5,
            _ => 0,
        };

        if (StrongGameNames.Contains(stem))
        {
            score += 35;
        }

        var normalizedDirectory = new string(gameDirectoryName.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
        var normalizedStem = new string(stem.Where(char.IsLetterOrDigit).ToArray());

        if (normalizedStem.Length >= 2 && normalizedDirectory.Length >= 2)
        {
            if (normalizedDirectory.Equals(normalizedStem, StringComparison.Ordinal))
            {
                score += 60;
            }
            else if (normalizedDirectory.StartsWith(normalizedStem, StringComparison.Ordinal) ||
                     normalizedStem.StartsWith(normalizedDirectory, StringComparison.Ordinal))
            {
                score += 40;
            }
        }

        // Very small executables are usually loaders or drivers rather than the game.
        if (sizeBytes > 0)
        {
            score += sizeBytes switch
            {
                < 2_000 => -15,
                < 20_000 => 0,
                < 200_000 => 10,
                _ => 15,
            };
        }

        return score;
    }
}
