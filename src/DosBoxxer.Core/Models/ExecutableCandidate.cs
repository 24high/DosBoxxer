namespace DosBoxxer.Core.Models;

public enum ExecutableKind
{
    Exe,
    Com,
    Bat,
}

/// <summary>
/// A launchable file found while scanning a game directory.
/// </summary>
public sealed class ExecutableCandidate
{
    public required string FullPath { get; init; }

    /// <summary>Path relative to the scanned root directory, using the host separator.</summary>
    public required string RelativePath { get; init; }

    public required string FileName { get; init; }

    public required ExecutableKind Kind { get; init; }

    public long SizeBytes { get; init; }

    /// <summary>
    /// True when the file name looks like an installer, setup or configuration utility rather
    /// than the game itself. Such entries are sorted to the bottom but never excluded — the
    /// user always makes the final decision.
    /// </summary>
    public bool LooksLikeUtility { get; init; }

    /// <summary>Heuristic score; higher is more likely to be the actual game starter.</summary>
    public int Score { get; init; }

    public string KindLabel => Kind switch
    {
        ExecutableKind.Exe => "EXE",
        ExecutableKind.Com => "COM",
        ExecutableKind.Bat => "BAT",
        _ => "?",
    };
}
