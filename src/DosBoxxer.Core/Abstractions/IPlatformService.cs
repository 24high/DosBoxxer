namespace DosBoxxer.Core.Abstractions;

/// <summary>Thin wrapper around the few genuinely platform specific operations we need.</summary>
public interface IPlatformService
{
    /// <summary>
    /// Opens the given directory in the platform file manager. Returns <c>false</c> when the
    /// directory does not exist or no handler could be started.
    /// </summary>
    bool OpenDirectory(string directory);

    /// <summary>Reveals a file in the platform file manager (falls back to opening its folder).</summary>
    bool RevealFile(string filePath);

    /// <summary>Opens an <c>http</c>/<c>https</c> URL in the user's default browser.</summary>
    bool OpenUrl(string url);
}
