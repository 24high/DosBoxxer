using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models;

namespace DosBoxxer.Core.Infrastructure.Savegame;

/// <summary>
/// Determines the directory a game's savegame entries are resolved against. DOSBox mounts the
/// game directory as <c>C:</c> and switches into the launch file's folder before starting it
/// (see <c>DosBoxConfigBuilder</c>), so a DOS game sees that folder as its current working
/// directory and writes its savegames there — not necessarily into the game root. Anchoring the
/// scan at the launch file's folder therefore matches the runtime behaviour for every game; for
/// the common case (executable directly in the game root) it is identical to the game directory.
/// </summary>
public static class SavegameBaseDirectory
{
    /// <summary>
    /// The folder containing the game's launch file, or the game directory itself when the launch
    /// file is empty, directly in the root or outside the game directory.
    /// </summary>
    public static string Resolve(Game game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var root = PathHelper.Normalize(game.GameDirectory);
        var launchDirectory = PathHelper.Normalize(Path.GetDirectoryName(game.LaunchFile));

        return launchDirectory.Length > 0 && PathHelper.IsWithin(root, launchDirectory)
            ? launchDirectory
            : root;
    }
}
