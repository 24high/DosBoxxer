using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Savegame;

namespace DosBoxxer.Core.Abstractions;

/// <summary>
/// Resolves a game's <see cref="SavegameConfig"/> into the concrete set of local files that must
/// be synchronised, expanding directories (recursively) and glob patterns. Patterns are anchored
/// at the launch file's folder — the DOS working directory at runtime (see
/// <c>SavegameBaseDirectory</c>). Everything returned is guaranteed to live inside the game
/// directory (path traversal is rejected).
/// </summary>
public interface ILocalSavegameScanner
{
    IReadOnlyList<LocalSavegameFile> Enumerate(Game game);

    /// <summary>Resolves against an explicit directory + configuration (used before a game is saved).</summary>
    IReadOnlyList<LocalSavegameFile> Enumerate(string gameDirectory, SavegameConfig config);
}
