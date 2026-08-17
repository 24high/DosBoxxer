namespace DosBoxxer.Core.Models.Savegame;

/// <summary>
/// One parsed row of <c>dos_savegame_pfade.json</c>. The raw JSON stores paths as
/// <c>&lt;SPIELORDNER&gt;\SAVE</c> / <c>&lt;base&gt;/SAVE</c>; the catalog loader normalises them
/// into a single <see cref="SavegameEntry"/> that is relative to the game directory.
/// </summary>
public sealed class CatalogEntry
{
    public required string Title { get; init; }

    /// <summary>The normalised savegame declaration derived from the manifest path.</summary>
    public required SavegameEntry Entry { get; init; }

    /// <summary>Raw "Art" value from the JSON, kept for display ("Pfad (Datei oder Ordner)" / "Dateimuster / Glob").</summary>
    public string? RawKind { get; init; }

    /// <summary>Raw "DOS-Savepfad / Muster" value, kept for display.</summary>
    public string? RawPattern { get; init; }
}
