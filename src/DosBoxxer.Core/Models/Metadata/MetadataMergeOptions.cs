namespace DosBoxxer.Core.Models.Metadata;

/// <summary>
/// Controls how freshly fetched metadata is merged into an existing library entry.
/// </summary>
public sealed class MetadataMergeOptions
{
    /// <summary>Fields the caller wants to update.</summary>
    public GameField Fields { get; init; } = GameField.All;

    /// <summary>
    /// When <c>false</c> (default) fields flagged in <see cref="Game.ManualFields"/> are kept,
    /// even when they are part of <see cref="Fields"/>.
    /// </summary>
    public bool OverwriteManualEdits { get; init; }

    /// <summary>When <c>true</c> empty provider values may clear existing data.</summary>
    public bool AllowClearing { get; init; }

    public static MetadataMergeOptions Default { get; } = new();

    /// <summary>Used when a game is created — everything is written, nothing is protected yet.</summary>
    public static MetadataMergeOptions Initial { get; } = new()
    {
        Fields = GameField.All,
        OverwriteManualEdits = true,
        AllowClearing = true,
    };
}

/// <summary>Result of a merge operation, used for status messages and logging.</summary>
public sealed class MetadataMergeResult
{
    public GameField UpdatedFields { get; init; } = GameField.None;

    public GameField SkippedManualFields { get; init; } = GameField.None;

    public bool AnyChange => UpdatedFields != GameField.None;
}
