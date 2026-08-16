namespace DosBoxxer.Core.Abstractions;

/// <summary>
/// Provides the built-in default <c>dosbox.conf</c> that ships embedded in the program and is
/// materialised to disk when a game is started (unless the user configured their own base config).
/// </summary>
public interface IDefaultDosBoxConfig
{
    /// <summary>The embedded default configuration as text.</summary>
    string GetContent();

    /// <summary>
    /// Ensures the embedded default configuration exists on disk and returns its path. It is
    /// written into the program directory (falling back to the user data directory when the
    /// program directory is not writable), and refreshed if the file is missing or out of date.
    /// </summary>
    Task<string> MaterializeAsync(CancellationToken cancellationToken = default);
}
