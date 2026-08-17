namespace DosBoxxer.Core.Abstractions;

/// <summary>
/// Platform independent access to the launcher's data directories. No path in the application
/// is ever built relative to the executable location.
/// </summary>
public interface IAppPaths
{
    /// <summary>Root user data directory, e.g. <c>%APPDATA%\DosBoxxer</c> or <c>~/.local/share/DosBoxxer</c>.</summary>
    string DataRoot { get; }

    /// <summary>Directory the application runs from (where the executable lives).</summary>
    string ProgramDirectory { get; }

    string DatabaseDirectory { get; }

    string DatabaseFile { get; }

    string CacheDirectory { get; }

    string CoversDirectory { get; }

    string ScreenshotsDirectory { get; }

    string MetadataCacheDirectory { get; }

    string TempDirectory { get; }

    string LogsDirectory { get; }

    /// <summary>Directory holding per-game cloud sync metadata JSON files.</summary>
    string SyncMetadataDirectory { get; }

    /// <summary>Directory holding pre-overwrite savegame backups.</summary>
    string SavegameBackupDirectory { get; }

    string SettingsFile { get; }

    string SecretsFile { get; }

    /// <summary>Creates every directory that does not exist yet. Safe to call repeatedly.</summary>
    void EnsureCreated();
}
