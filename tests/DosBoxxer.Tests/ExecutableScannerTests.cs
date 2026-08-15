using System.Linq;
using System.Threading;
using DosBoxxer.Core.Infrastructure;
using DosBoxxer.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DosBoxxer.Tests;

public sealed class ExecutableScannerTests
{
    private static ExecutableScanner CreateScanner() =>
        new(NullLogger<ExecutableScanner>.Instance);

    [Fact]
    public void Scan_FindsExeBatAndComRecursively()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("DOOM.EXE", padToBytes: 250_000);
        temp.CreateFile("INSTALL.BAT");
        temp.CreateFile("BIN/GAME.COM", padToBytes: 30_000);
        temp.CreateFile("DATA/DEEP/NESTED/TOOL.EXE");
        temp.CreateFile("README.TXT");
        temp.CreateFile("DATA/SOUND.DAT");

        var results = CreateScanner().Scan(temp.Path, CancellationToken.None);

        Assert.Equal(4, results.Count);
        Assert.Contains(results, r => r.FileName == "DOOM.EXE" && r.Kind == ExecutableKind.Exe);
        Assert.Contains(results, r => r.FileName == "INSTALL.BAT" && r.Kind == ExecutableKind.Bat);
        Assert.Contains(results, r => r.FileName == "GAME.COM" && r.Kind == ExecutableKind.Com);
        Assert.Contains(results, r => r.FileName == "TOOL.EXE");
    }

    [Fact]
    public void Scan_IgnoresNonExecutableFiles()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("README.TXT");
        temp.CreateFile("SETUP.INI");
        temp.CreateFile("MUSIC.MID");

        var results = CreateScanner().Scan(temp.Path, CancellationToken.None);

        Assert.Empty(results);
    }

    [Fact]
    public void Scan_ReturnsRelativePathsBelowTheRoot()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("BIN/SUB/GAME.EXE");

        var results = CreateScanner().Scan(temp.Path, CancellationToken.None);
        var candidate = Assert.Single(results);

        Assert.Equal(
            System.IO.Path.Combine("BIN", "SUB", "GAME.EXE"),
            candidate.RelativePath);
    }

    [Fact]
    public void Scan_RanksInstallersBelowTheActualGame()
    {
        using var temp = new TempDirectory();
        temp.CreateFile("SETUP.EXE", padToBytes: 400_000);
        temp.CreateFile("INSTALL.EXE", padToBytes: 400_000);
        temp.CreateFile("DOOM.EXE", padToBytes: 100_000);

        var results = CreateScanner().Scan(temp.Path, CancellationToken.None);

        Assert.Equal("DOOM.EXE", results[0].FileName);
        Assert.All(results.Where(r => r.FileName != "DOOM.EXE"), r => Assert.True(r.LooksLikeUtility));
    }

    [Fact]
    public void Scan_PrefersAnExecutableNamedLikeTheDirectory()
    {
        using var temp = new TempDirectory();
        var gameDirectory = temp.CreateSubdirectory("KEEN");

        System.IO.File.WriteAllText(System.IO.Path.Combine(gameDirectory, "KEEN.EXE"), new string('x', 90_000));
        System.IO.File.WriteAllText(System.IO.Path.Combine(gameDirectory, "OTHER.EXE"), new string('x', 90_000));

        var results = CreateScanner().Scan(gameDirectory, CancellationToken.None);

        Assert.Equal("KEEN.EXE", results[0].FileName);
    }

    [Fact]
    public void Scan_ReturnsEmptyForMissingDirectory()
    {
        var results = CreateScanner().Scan("/this/path/does/not/exist-12345", CancellationToken.None);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData("SETUP.EXE", true)]
    [InlineData("INSTALL.BAT", true)]
    [InlineData("CONFIG.EXE", true)]
    [InlineData("UNINSTAL.EXE", true)]
    [InlineData("DOOM.EXE", false)]
    [InlineData("KEEN4E.EXE", false)]
    public void IsUtility_ClassifiesKnownNames(string fileName, bool expected) =>
        Assert.Equal(expected, ExecutableScanner.IsUtility(fileName));
}
