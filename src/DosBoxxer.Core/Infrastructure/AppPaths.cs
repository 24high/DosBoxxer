using System.Runtime.InteropServices;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;

namespace DosBoxxer.Core.Infrastructure;

/// <summary>
/// Resolves the platform specific user data directory:
/// <list type="bullet">
/// <item>Windows: <c>%APPDATA%\DosBoxxer</c></item>
/// <item>macOS: <c>~/Library/Application Support/DosBoxxer</c></item>
/// <item>Linux: <c>$XDG_DATA_HOME/DosBoxxer</c> or <c>~/.local/share/DosBoxxer</c></item>
/// </list>
/// </summary>
public sealed class AppPaths : IAppPaths
{
    public const string ApplicationFolderName = "DosBoxxer";

    public AppPaths(string? overrideRoot = null, string? programDirectory = null)
    {
        DataRoot = overrideRoot is { Length: > 0 }
            ? Path.GetFullPath(overrideRoot)
            : ResolveDefaultRoot();

        ProgramDirectory = programDirectory is { Length: > 0 }
            ? Path.GetFullPath(programDirectory)
            : PathHelper.TrimTrailingSeparator(AppContext.BaseDirectory);

        DatabaseDirectory = Path.Combine(DataRoot, "database");
        CacheDirectory = Path.Combine(DataRoot, "cache");
        CoversDirectory = Path.Combine(CacheDirectory, "covers");
        ScreenshotsDirectory = Path.Combine(CacheDirectory, "screenshots");
        MetadataCacheDirectory = Path.Combine(CacheDirectory, "metadata");
        TempDirectory = Path.Combine(DataRoot, "temp");
        LogsDirectory = Path.Combine(DataRoot, "logs");
        SyncMetadataDirectory = Path.Combine(DataRoot, "sync");
        SavegameBackupDirectory = Path.Combine(DataRoot, "savegame-backups");
        DatabaseFile = Path.Combine(DatabaseDirectory, "library.db");
        SettingsFile = Path.Combine(DataRoot, "settings.json");
        SecretsFile = Path.Combine(DataRoot, "secrets.dat");
    }

    public string DataRoot { get; }

    public string ProgramDirectory { get; }

    public string DatabaseDirectory { get; }

    public string DatabaseFile { get; }

    public string CacheDirectory { get; }

    public string CoversDirectory { get; }

    public string ScreenshotsDirectory { get; }

    public string MetadataCacheDirectory { get; }

    public string TempDirectory { get; }

    public string LogsDirectory { get; }

    public string SyncMetadataDirectory { get; }

    public string SavegameBackupDirectory { get; }

    public string SettingsFile { get; }

    public string SecretsFile { get; }

    public void EnsureCreated()
    {
        foreach (var directory in new[]
                 {
                     DataRoot, DatabaseDirectory, CacheDirectory, CoversDirectory,
                     ScreenshotsDirectory, MetadataCacheDirectory, TempDirectory, LogsDirectory,
                     SyncMetadataDirectory, SavegameBackupDirectory,
                 })
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static string ResolveDefaultRoot()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var appData = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData,
                Environment.SpecialFolderOption.DoNotVerify);

            if (string.IsNullOrEmpty(appData))
            {
                appData = Path.Combine(GetHome(), "AppData", "Roaming");
            }

            return Path.Combine(appData, ApplicationFolderName);
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return Path.Combine(GetHome(), "Library", "Application Support", ApplicationFolderName);
        }

        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var baseDirectory = string.IsNullOrWhiteSpace(xdgDataHome)
            ? Path.Combine(GetHome(), ".local", "share")
            : xdgDataHome;

        return Path.Combine(baseDirectory, ApplicationFolderName);
    }

    private static string GetHome()
    {
        var home = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile,
            Environment.SpecialFolderOption.DoNotVerify);

        if (!string.IsNullOrEmpty(home))
        {
            return home;
        }

        home = Environment.GetEnvironmentVariable("HOME");
        return string.IsNullOrEmpty(home) ? Directory.GetCurrentDirectory() : home;
    }
}
