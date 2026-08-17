namespace DosBoxxer.Core.Models.Savegame;

/// <summary>
/// How a single savegame declaration is interpreted, mirroring the two shapes that occur in
/// <c>dos_savegame_pfade.json</c> ("Pfad (Datei oder Ordner)" and "Dateimuster / Glob").
/// </summary>
public enum SavegameEntryKind
{
    /// <summary>
    /// A concrete relative path. Resolved at scan time: an existing directory is taken with all
    /// of its content (recursively), an existing file is taken on its own.
    /// </summary>
    Path = 0,

    /// <summary>A glob pattern such as <c>SAVE*.DAT</c> or <c>*.SAV</c>, optionally with a sub directory prefix.</summary>
    Glob = 1,
}

/// <summary>
/// A single savegame declaration, always expressed <b>relative to the game's root directory</b>
/// with forward slashes. This is the persisted, host independent form.
/// </summary>
public sealed class SavegameEntry
{
    public SavegameEntry()
    {
    }

    public SavegameEntry(string relativePattern, SavegameEntryKind kind)
    {
        RelativePattern = relativePattern;
        Kind = kind;
    }

    /// <summary>Relative pattern using <c>/</c> separators, e.g. <c>SAVE</c>, <c>tower/highscores.dat</c>, <c>*.SAV</c>.</summary>
    public string RelativePattern { get; set; } = string.Empty;

    public SavegameEntryKind Kind { get; set; } = SavegameEntryKind.Path;

    public SavegameEntry Clone() => new(RelativePattern, Kind);

    public override string ToString() => $"{Kind}:{RelativePattern}";
}

/// <summary>
/// Per-game savegame configuration. Attached to a <see cref="Game"/> and persisted alongside it.
/// A game with an empty, non-configured instance is treated as "Savegames not configured" and is
/// never auto-synced.
/// </summary>
public sealed class SavegameConfig
{
    public Guid GameId { get; set; }

    /// <summary>
    /// True once the user has made an explicit choice for this game (picked a catalog title,
    /// chose "no match" or configured entries manually). Distinguishes a deliberately empty
    /// configuration from a never-configured game.
    /// </summary>
    public bool IsConfigured { get; set; }

    /// <summary>Title of the matched entry in the JSON catalog, if one was chosen. <c>null</c> for a purely manual setup.</summary>
    public string? MatchedCatalogTitle { get; set; }

    /// <summary>True when the entries were edited manually rather than taken verbatim from the catalog.</summary>
    public bool IsManual { get; set; }

    public List<SavegameEntry> Entries { get; } = new();

    /// <summary>A configuration that has at least one entry is eligible for synchronisation.</summary>
    public bool HasSavegames => Entries.Count > 0;

    public SavegameConfig Clone()
    {
        var clone = new SavegameConfig
        {
            GameId = GameId,
            IsConfigured = IsConfigured,
            MatchedCatalogTitle = MatchedCatalogTitle,
            IsManual = IsManual,
        };
        clone.Entries.AddRange(Entries.Select(e => e.Clone()));
        return clone;
    }
}
