namespace DosBoxxer.Core.Models.Savegame;

/// <summary>
/// A concrete local savegame file discovered by resolving a <see cref="SavegameConfig"/> against
/// a game directory. Paths are always kept both absolute (for I/O) and game-relative with forward
/// slashes (the stable key used for cloud comparison).
/// </summary>
public sealed class LocalSavegameFile
{
    public required string AbsolutePath { get; init; }

    /// <summary>Path relative to the game directory, using <c>/</c> separators. The cloud comparison key.</summary>
    public required string RelativePath { get; init; }

    public long Size { get; init; }

    public DateTimeOffset LastModifiedUtc { get; init; }
}
