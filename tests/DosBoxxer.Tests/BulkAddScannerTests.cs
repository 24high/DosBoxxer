using System.IO;
using System.Linq;
using DosBoxxer.Core.Helpers;
using Xunit;

namespace DosBoxxer.Tests;

public sealed class BulkAddScannerTests
{
    [Fact]
    public void GetGameDirectories_ReturnsImmediateSubdirectoriesSorted()
    {
        using var temp = new TempDirectory();
        temp.CreateSubdirectory("Doom");
        temp.CreateSubdirectory("Commander Keen");
        temp.CreateSubdirectory("Civilization");

        var result = BulkAddScanner.GetGameDirectories(temp.Path);

        Assert.Equal(3, result.Count);
        Assert.Equal(
            new[] { "Civilization", "Commander Keen", "Doom" },
            result.Select(Path.GetFileName));
    }

    [Fact]
    public void GetGameDirectories_ReturnsAbsoluteNormalisedPaths()
    {
        using var temp = new TempDirectory();
        var doom = temp.CreateSubdirectory("Doom");

        var result = BulkAddScanner.GetGameDirectories(temp.Path);

        Assert.Equal(PathHelper.Normalize(doom), result[0]);
    }

    [Fact]
    public void GetGameDirectories_SkipsHiddenDirectories()
    {
        using var temp = new TempDirectory();
        temp.CreateSubdirectory("Doom");
        temp.CreateSubdirectory(".git");
        temp.CreateSubdirectory(".cache");

        var result = BulkAddScanner.GetGameDirectories(temp.Path);

        Assert.Single(result);
        Assert.Equal("Doom", Path.GetFileName(result[0]));
    }

    [Fact]
    public void GetGameDirectories_IgnoresFilesInTheParent()
    {
        using var temp = new TempDirectory();
        temp.CreateSubdirectory("Doom");
        temp.CreateFile("readme.txt");
        temp.CreateFile("catalog.dat");

        var result = BulkAddScanner.GetGameDirectories(temp.Path);

        Assert.Single(result);
    }

    [Fact]
    public void GetGameDirectories_ReturnsEmptyForMissingOrNullPath()
    {
        Assert.Empty(BulkAddScanner.GetGameDirectories(null));
        Assert.Empty(BulkAddScanner.GetGameDirectories(string.Empty));
        Assert.Empty(BulkAddScanner.GetGameDirectories("/this/does/not/exist-abc123"));
    }

    [Fact]
    public void GetGameDirectories_ReturnsEmptyForAnEmptyParent()
    {
        using var temp = new TempDirectory();
        Assert.Empty(BulkAddScanner.GetGameDirectories(temp.Path));
    }
}
