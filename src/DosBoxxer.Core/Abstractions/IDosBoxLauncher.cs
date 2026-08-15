using DosBoxxer.Core.Models;

namespace DosBoxxer.Core.Abstractions;

/// <summary>
/// Starts a game. Implemented for DOSBox; the interface deliberately talks about "a game"
/// rather than about DOSBox so a second emulator backend can be registered later.
/// </summary>
public interface IDosBoxLauncher
{
    /// <summary>True while a game process started by this launcher is running.</summary>
    bool IsRunning { get; }

    event EventHandler<Game>? GameStarted;

    event EventHandler<LaunchResult>? GameExited;

    /// <summary>
    /// Generates the game specific configuration, starts DOSBox and waits for the process to
    /// exit. Never throws for expected error conditions — inspect <see cref="LaunchResult.Status"/>.
    /// </summary>
    Task<LaunchResult> LaunchAsync(Game game, CancellationToken cancellationToken = default);
}
