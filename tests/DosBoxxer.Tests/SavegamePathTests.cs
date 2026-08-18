using System.IO;
using System.Linq;
using DosBoxxer.Core.Infrastructure.Savegame;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Savegame;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DosBoxxer.Tests;

/// <summary>
/// Coverage of savegame path parsing (the two JSON notations) and of resolving a configuration to
/// concrete files: single file, several files, a directory, nested directories, a missing path and
/// invalid / traversal patterns.
/// </summary>
public sealed class SavegamePathTests
{
    private static LocalSavegameScanner Scanner() => new(NullLogger<LocalSavegameScanner>.Instance);

    private static SavegameConfig Config(params SavegameEntry[] entries)
    {
        var config = new SavegameConfig { IsConfigured = true };
        config.Entries.AddRange(entries);
        return config;
    }

    // ---- parser ---------------------------------------------------------------------------

    [Theory]
    [InlineData("<base>/SAVE", "SAVE", SavegameEntryKind.Path)]
    [InlineData("<SPIELORDNER>\\SAVE", "SAVE", SavegameEntryKind.Path)]
    [InlineData("<base>/tower/highscores.dat", "tower/highscores.dat", SavegameEntryKind.Path)]
    [InlineData("<base>/SAVEGAM*.DAT", "SAVEGAM*.DAT", SavegameEntryKind.Glob)]
    [InlineData("<base>/*.SAV", "*.SAV", SavegameEntryKind.Glob)]
    [InlineData("<base>/SLOT.*", "SLOT.*", SavegameEntryKind.Glob)]
    public void Parse_NormalisesBothNotations(string raw, string expectedPattern, SavegameEntryKind expectedKind)
    {
        var entry = SavegamePathParser.Parse(raw);

        Assert.NotNull(entry);
        Assert.Equal(expectedPattern, entry!.RelativePattern);
        Assert.Equal(expectedKind, entry.Kind);
    }

    [Fact]
    public void Parse_RespectsExplicitGlobKind()
    {
        var entry = SavegamePathParser.Parse("<base>/SAVE", "Dateimuster / Glob");
        Assert.Equal(SavegameEntryKind.Glob, entry!.Kind);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/etc/passwd")]
    [InlineData("C:/Windows")]
    [InlineData("save/../../escape")]
    [InlineData("")]
    public void Parse_RejectsTraversalAndAbsolutePaths(string raw) =>
        Assert.Null(SavegamePathParser.NormalizeRelative(raw));

    // ---- scanner --------------------------------------------------------------------------

    [Fact]
    public void SingleFile_IsResolved()
    {
        using var temp = new TempDirectory();
        var dir = temp.CreateSubdirectory("game");
        temp.CreateFile("game/PLAYER.U0", "hero");

        var files = Scanner().Enumerate(dir, Config(new SavegameEntry("PLAYER.U0", SavegameEntryKind.Path)));

        Assert.Single(files);
        Assert.Equal("PLAYER.U0", files[0].RelativePath);
    }

    [Fact]
    public void MultipleGlobFiles_AreResolved()
    {
        using var temp = new TempDirectory();
        var dir = temp.CreateSubdirectory("game");
        temp.CreateFile("game/SAVE1.SAV", "a");
        temp.CreateFile("game/SAVE2.SAV", "b");
        temp.CreateFile("game/README.TXT", "not a save");

        var files = Scanner().Enumerate(dir, Config(new SavegameEntry("*.SAV", SavegameEntryKind.Glob)));

        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.EndsWith(".SAV", f.RelativePath));
    }

    [Fact]
    public void Directory_IsExpandedRecursively()
    {
        using var temp = new TempDirectory();
        var dir = temp.CreateSubdirectory("game");
        temp.CreateFile("game/SAVE/slot1.dat", "1");
        temp.CreateFile("game/SAVE/slot2.dat", "2");
        temp.CreateFile("game/SAVE/nested/slot3.dat", "3");

        var files = Scanner().Enumerate(dir, Config(new SavegameEntry("SAVE", SavegameEntryKind.Path)));

        Assert.Equal(3, files.Count);
        Assert.Contains(files, f => f.RelativePath == "SAVE/nested/slot3.dat");
    }

    [Fact]
    public void NestedDirectory_PreservesRelativeStructure()
    {
        using var temp = new TempDirectory();
        var dir = temp.CreateSubdirectory("game");
        temp.CreateFile("game/tower/highscores.dat", "hi");

        var files = Scanner().Enumerate(dir, Config(new SavegameEntry("tower/highscores.dat", SavegameEntryKind.Path)));

        Assert.Single(files);
        Assert.Equal("tower/highscores.dat", files[0].RelativePath);
    }

    [Fact]
    public void MissingPath_ResolvesToNothing()
    {
        using var temp = new TempDirectory();
        var dir = temp.CreateSubdirectory("game");

        var files = Scanner().Enumerate(dir, Config(new SavegameEntry("DOESNOTEXIST.SAV", SavegameEntryKind.Path)));

        Assert.Empty(files);
    }

    [Fact]
    public void InvalidTraversalPattern_IsIgnored()
    {
        using var temp = new TempDirectory();
        var dir = temp.CreateSubdirectory("game");
        temp.CreateFile("secret.txt", "outside the game dir");

        var files = Scanner().Enumerate(dir, Config(new SavegameEntry("../secret.txt", SavegameEntryKind.Path)));

        Assert.Empty(files);
    }

    [Fact]
    public void GlobInSubdirectory_IsFoundWhenRootYieldsNothing()
    {
        using var temp = new TempDirectory();
        var dir = temp.CreateSubdirectory("game");
        temp.CreateFile("game/DREAMWEB/DREAMWEB.D00", "save 0");
        temp.CreateFile("game/DREAMWEB/DREAMWEB.D01", "save 1");
        temp.CreateFile("game/DREAMWEB/README.TXT", "not a save");

        var files = Scanner().Enumerate(dir, Config(new SavegameEntry("DREAMWEB.D0*", SavegameEntryKind.Glob)));

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.RelativePath == "DREAMWEB/DREAMWEB.D00");
        Assert.Contains(files, f => f.RelativePath == "DREAMWEB/DREAMWEB.D01");
    }

    [Fact]
    public void GlobWithExplicitSubdirectory_DoesNotSearchDeeper()
    {
        using var temp = new TempDirectory();
        var dir = temp.CreateSubdirectory("game");
        temp.CreateFile("game/SAVE/slot1.dat", "1");
        temp.CreateFile("game/SAVE/nested/slot2.dat", "2");

        var files = Scanner().Enumerate(dir, Config(new SavegameEntry("SAVE/*.dat", SavegameEntryKind.Glob)));

        Assert.Single(files);
        Assert.Equal("SAVE/slot1.dat", files[0].RelativePath);
    }

    [Fact]
    public void MissingGameDirectory_ResolvesToNothing()
    {
        using var temp = new TempDirectory();
        var files = Scanner().Enumerate(Path.Combine(temp.Path, "nope"), Config(new SavegameEntry("SAVE", SavegameEntryKind.Path)));
        Assert.Empty(files);
    }

    // ---- scanner: the base directory follows the launch file ------------------------------

    [Fact]
    public void ExecutableInRoot_ScansFromGameDirectory()
    {
        using var temp = new TempDirectory();
        var dir = temp.CreateSubdirectory("game");
        temp.CreateFile("game/GAME.EXE", "exe");
        temp.CreateFile("game/SAVE.SAV", "hero");

        var game = new Game
        {
            GameDirectory = dir,
            LaunchFile = Path.Combine(dir, "GAME.EXE"),
            SavegameConfig = Config(new SavegameEntry("*.SAV", SavegameEntryKind.Glob)),
        };

        var files = Scanner().Enumerate(game);

        Assert.Single(files);
        Assert.Equal("SAVE.SAV", files[0].RelativePath);
    }

    [Fact]
    public void ExecutableInSubdirectory_ScansFromTheLaunchFolder()
    {
        using var temp = new TempDirectory();
        var dir = temp.CreateSubdirectory("game");
        temp.CreateFile("game/DOS/GAME.EXE", "exe");
        temp.CreateFile("game/DOS/SAVE.SAV", "hero");
        temp.CreateFile("game/ROOT.SAV", "decoy in the root");

        var game = new Game
        {
            GameDirectory = dir,
            LaunchFile = Path.Combine(dir, "DOS", "GAME.EXE"),
            SavegameConfig = Config(new SavegameEntry("*.SAV", SavegameEntryKind.Glob)),
        };

        var files = Scanner().Enumerate(game);

        Assert.Single(files);
        Assert.Equal("SAVE.SAV", files[0].RelativePath);
        Assert.Equal(Path.Combine(dir, "DOS", "SAVE.SAV"), files[0].AbsolutePath);
    }

    [Fact]
    public void ExecutableOutsideGameDirectory_FallsBackToGameDirectory()
    {
        using var temp = new TempDirectory();
        var dir = temp.CreateSubdirectory("game");
        temp.CreateFile("game/SAVE.SAV", "hero");
        var elsewhere = temp.CreateSubdirectory("elsewhere");

        var game = new Game
        {
            GameDirectory = dir,
            LaunchFile = Path.Combine(elsewhere, "GAME.EXE"),
            SavegameConfig = Config(new SavegameEntry("*.SAV", SavegameEntryKind.Glob)),
        };

        var files = Scanner().Enumerate(game);

        Assert.Single(files);
        Assert.Equal("SAVE.SAV", files[0].RelativePath);
    }
}
