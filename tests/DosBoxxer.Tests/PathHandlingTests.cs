using System.IO;
using DosBoxxer.Core.Helpers;
using Xunit;

namespace DosBoxxer.Tests;

public sealed class PathHandlingTests
{
    [Fact]
    public void Normalize_RemovesTrailingSeparator()
    {
        using var temp = new TempDirectory();
        var withSeparator = temp.Path + Path.DirectorySeparatorChar;

        Assert.Equal(PathHelper.Normalize(temp.Path), PathHelper.Normalize(withSeparator));
    }

    [Fact]
    public void Normalize_ReturnsEmptyForNullOrWhitespace()
    {
        Assert.Equal(string.Empty, PathHelper.Normalize(null));
        Assert.Equal(string.Empty, PathHelper.Normalize("   "));
    }

    [Fact]
    public void GetRelativePathWithin_ReturnsRelativePath()
    {
        using var temp = new TempDirectory();
        var file = temp.CreateFile("BIN/DOOM.EXE");

        var relative = PathHelper.GetRelativePathWithin(temp.Path, file);

        Assert.Equal(Path.Combine("BIN", "DOOM.EXE"), relative);
    }

    [Fact]
    public void GetRelativePathWithin_HandlesFileDirectlyInRoot()
    {
        using var temp = new TempDirectory();
        var file = temp.CreateFile("DOOM.EXE");

        Assert.Equal("DOOM.EXE", PathHelper.GetRelativePathWithin(temp.Path, file));
    }

    [Fact]
    public void GetRelativePathWithin_HandlesPathsWithSpaces()
    {
        using var temp = new TempDirectory();
        var file = temp.CreateFile("My Games/Some Game/START ME.EXE");

        var relative = PathHelper.GetRelativePathWithin(temp.Path, file);

        Assert.Equal(Path.Combine("My Games", "Some Game", "START ME.EXE"), relative);
    }

    [Fact]
    public void GetRelativePathWithin_ReturnsNullWhenOutsideTheBaseDirectory()
    {
        using var outer = new TempDirectory();
        using var other = new TempDirectory();

        var file = other.CreateFile("GAME.EXE");

        Assert.Null(PathHelper.GetRelativePathWithin(outer.Path, file));
    }

    [Fact]
    public void IsWithin_DetectsContainmentAndRejectsSiblings()
    {
        using var temp = new TempDirectory();
        var inner = temp.CreateSubdirectory("inner");

        Assert.True(PathHelper.IsWithin(temp.Path, inner));
        Assert.True(PathHelper.IsWithin(temp.Path, temp.Path));

        using var other = new TempDirectory();
        Assert.False(PathHelper.IsWithin(temp.Path, other.Path));
    }

    [Fact]
    public void SanitizeFileName_ReplacesInvalidCharacters()
    {
        var sanitized = PathHelper.SanitizeFileName("a/b\\c:d*e?f");

        Assert.DoesNotContain('/', sanitized);
        Assert.DoesNotContain('\\', sanitized);
        Assert.False(string.IsNullOrWhiteSpace(sanitized));
    }

    [Fact]
    public void FileAndDirectoryExistChecks_AreNullSafe()
    {
        Assert.False(PathHelper.FileExistsSafe(null));
        Assert.False(PathHelper.DirectoryExistsSafe(null));
        Assert.False(PathHelper.FileExistsSafe(""));
    }
}

public sealed class DosPathConverterTests
{
    [Theory]
    [InlineData("DOOM.EXE", "DOOM.EXE")]
    [InlineData("doom.exe", "DOOM.EXE")]
    [InlineData("GAME.COM", "GAME.COM")]
    [InlineData("SETUP.BAT", "SETUP.BAT")]
    [InlineData("KEEN4E.EXE", "KEEN4E.EXE")]
    public void ToDosName_KeepsValid83Names(string input, string expected) =>
        Assert.Equal(expected, DosPathConverter.ToDosName(input));

    [Theory]
    [InlineData("LONGDIRECTORY", "LONGDI~1")]
    [InlineData("VeryLongGameName.EXE", "VERYLO~1.EXE")]
    public void ToDosName_ShortensLongNames(string input, string expected) =>
        Assert.Equal(expected, DosPathConverter.ToDosName(input));

    [Fact]
    public void ToDosName_DropsSpaces()
    {
        // DOS has no spaces in short names; "MY GAME" becomes "MYGAME".
        Assert.Equal("MYGAME", DosPathConverter.ToDosName("My Game"));
    }

    [Fact]
    public void ConvertRelativePath_UsesBackslashesAndUpperCase()
    {
        var conversion = DosPathConverter.ConvertRelativePath(Path.Combine("bin", "data", "game.exe"));

        Assert.Equal(@"BIN\DATA\GAME.EXE", conversion.DosPath);
        Assert.Empty(conversion.MangledNames);
    }

    [Fact]
    public void ConvertRelativePath_ReportsMangledSegments()
    {
        var conversion = DosPathConverter.ConvertRelativePath(Path.Combine("ReallyLongFolder", "BIN"));

        Assert.Equal(@"REALLY~1\BIN", conversion.DosPath);
        Assert.Contains("ReallyLongFolder", conversion.MangledNames);
    }

    [Fact]
    public void ConvertRelativePath_ReturnsEmptyForRoot()
    {
        Assert.Equal(string.Empty, DosPathConverter.ConvertRelativePath(".").DosPath);
        Assert.Equal(string.Empty, DosPathConverter.ConvertRelativePath(string.Empty).DosPath);
    }

    [Fact]
    public void QuoteHostPathForMount_QuotesPathsWithSpaces()
    {
        Assert.Equal("\"/home/user/My Games/Doom\"", DosPathConverter.QuoteHostPathForMount("/home/user/My Games/Doom"));
        Assert.Equal("\"C:\\DOS Games\\DOOM\"", DosPathConverter.QuoteHostPathForMount(@"C:\DOS Games\DOOM"));
    }

    [Fact]
    public void QuoteHostPathForMount_RejectsPathsContainingQuotes() =>
        Assert.Null(DosPathConverter.QuoteHostPathForMount("/home/we\"ird"));
}

public sealed class CommandLineSplitterTests
{
    [Fact]
    public void Split_ReturnsEmptyForNullOrWhitespace()
    {
        Assert.Empty(CommandLineSplitter.Split(null));
        Assert.Empty(CommandLineSplitter.Split("   "));
    }

    [Fact]
    public void Split_SeparatesPlainArguments()
    {
        var result = CommandLineSplitter.Split("-fullscreen -noconsole");

        Assert.Equal(new[] { "-fullscreen", "-noconsole" }, result);
    }

    [Fact]
    public void Split_KeepsQuotedArgumentsTogether()
    {
        var result = CommandLineSplitter.Split("-conf \"/home/user/My Config/dosbox.conf\" -noconsole");

        Assert.Equal(3, result.Count);
        Assert.Equal("/home/user/My Config/dosbox.conf", result[1]);
    }

    [Fact]
    public void Split_DoesNotInterpretShellMetacharacters()
    {
        // The whole point of using an argument vector: this stays one literal argument.
        var result = CommandLineSplitter.Split("\"; rm -rf /\"");

        Assert.Single(result);
        Assert.Equal("; rm -rf /", result[0]);
    }
}
