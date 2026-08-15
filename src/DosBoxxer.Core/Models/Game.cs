namespace DosBoxxer.Core.Models;

/// <summary>
/// A single entry of the local game library. This is the persistence/domain model; it is
/// deliberately free of any UI or emulator process concerns.
/// </summary>
public sealed class Game
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Display title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Title used for sorting (leading articles removed, upper-cased).</summary>
    public string SortTitle { get; set; } = string.Empty;

    /// <summary>Title as reported by the metadata provider, before any manual edit.</summary>
    public string? OriginalTitle { get; set; }

    /// <summary>Absolute path of the game's root directory on the host file system.</summary>
    public string GameDirectory { get; set; } = string.Empty;

    /// <summary>Absolute path of the executable/batch file that starts the game.</summary>
    public string LaunchFile { get; set; } = string.Empty;

    /// <summary>
    /// Path of <see cref="LaunchFile"/> relative to <see cref="GameDirectory"/>, stored with
    /// the host separator. Recomputed whenever directory or launch file change.
    /// </summary>
    public string RelativeLaunchFile { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? Publisher { get; set; }

    public string? Developer { get; set; }

    public int? ReleaseYear { get; set; }

    /// <summary>Human readable platform name, e.g. "PC (DOS)".</summary>
    public string Platform { get; set; } = "PC (DOS)";

    /// <summary>Number of players as reported by the metadata provider, e.g. "1-4".</summary>
    public string? Players { get; set; }

    /// <summary>Identifier of the matched game at the metadata provider (ScreenScraper game id).</summary>
    public string? ScreenScraperId { get; set; }

    /// <summary>Absolute path of the locally cached cover image, if any.</summary>
    public string? CoverImagePath { get; set; }

    /// <summary>
    /// Key of the emulator profile used to launch this game. Only <c>dosbox</c> is implemented,
    /// but the column exists so additional emulators can be added without a schema migration.
    /// </summary>
    public string EmulatorProfile { get; set; } = EmulatorProfiles.DosBox;

    public DateTimeOffset DateAdded { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastPlayed { get; set; }

    public int PlayCount { get; set; }

    public long TotalPlayTimeSeconds { get; set; }

    public DateTimeOffset? MetadataLastUpdated { get; set; }

    public bool IsFavorite { get; set; }

    /// <summary>Fields that were edited by the user and are protected from automatic refresh.</summary>
    public GameField ManualFields { get; set; } = GameField.None;

    public List<GenreKey> Genres { get; } = new();

    public List<Screenshot> Screenshots { get; } = new();

    /// <summary>Per-game DOSBox overrides. Never <c>null</c>; an empty instance means "use base config".</summary>
    public GameDosBoxSettings DosBoxSettings { get; set; } = new();

    public Game Clone()
    {
        var clone = new Game
        {
            Id = Id,
            Title = Title,
            SortTitle = SortTitle,
            OriginalTitle = OriginalTitle,
            GameDirectory = GameDirectory,
            LaunchFile = LaunchFile,
            RelativeLaunchFile = RelativeLaunchFile,
            Description = Description,
            Publisher = Publisher,
            Developer = Developer,
            ReleaseYear = ReleaseYear,
            Platform = Platform,
            Players = Players,
            ScreenScraperId = ScreenScraperId,
            CoverImagePath = CoverImagePath,
            EmulatorProfile = EmulatorProfile,
            DateAdded = DateAdded,
            LastPlayed = LastPlayed,
            PlayCount = PlayCount,
            TotalPlayTimeSeconds = TotalPlayTimeSeconds,
            MetadataLastUpdated = MetadataLastUpdated,
            IsFavorite = IsFavorite,
            ManualFields = ManualFields,
            DosBoxSettings = DosBoxSettings.Clone(),
        };
        clone.Genres.AddRange(Genres);
        clone.Screenshots.AddRange(Screenshots.Select(s => s.Clone()));
        return clone;
    }
}

public static class EmulatorProfiles
{
    public const string DosBox = "dosbox";
}
