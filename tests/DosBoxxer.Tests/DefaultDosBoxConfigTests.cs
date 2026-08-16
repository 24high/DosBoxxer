using System.IO;
using System.Threading.Tasks;
using DosBoxxer.Core.Infrastructure;
using DosBoxxer.Core.Infrastructure.DosBox;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DosBoxxer.Tests;

public sealed class DefaultDosBoxConfigTests
{
    private static DefaultDosBoxConfig Create(TempDirectory temp, out AppPaths paths)
    {
        paths = new AppPaths(Path.Combine(temp.Path, "data"), Path.Combine(temp.Path, "program"));
        Directory.CreateDirectory(paths.ProgramDirectory);
        return new DefaultDosBoxConfig(paths, NullLogger<DefaultDosBoxConfig>.Instance);
    }

    [Fact]
    public void GetContent_ReturnsTheEmbeddedConfiguration()
    {
        using var temp = new TempDirectory();
        var content = Create(temp, out _).GetContent();

        Assert.False(string.IsNullOrWhiteSpace(content));
        Assert.Contains("[sdl]", content);
        Assert.Contains("[autoexec]", content);
    }

    [Fact]
    public void EmbeddedDefault_StartsGamesFullscreenScaledWithAspectRatio()
    {
        using var temp = new TempDirectory();
        var content = Create(temp, out _).GetContent();

        // The launch requirements: fullscreen, scaled to the desktop, aspect ratio preserved.
        Assert.Contains("fullscreen=true", content);
        Assert.Contains("fullresolution=desktop", content);
        Assert.Contains("aspect=true", content);
        Assert.DoesNotContain("fullscreen=false", content);
        Assert.DoesNotContain("aspect=false", content);
    }

    [Fact]
    public async Task MaterializeAsync_WritesToTheProgramDirectory()
    {
        using var temp = new TempDirectory();
        var config = Create(temp, out var paths);

        var path = await config.MaterializeAsync();

        Assert.Equal(Path.Combine(paths.ProgramDirectory, "dosbox.conf"), path);
        Assert.True(File.Exists(path));
        Assert.Equal(config.GetContent(), await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task MaterializeAsync_IsIdempotentAndKeepsAnUpToDateFile()
    {
        using var temp = new TempDirectory();
        var config = Create(temp, out _);

        var first = await config.MaterializeAsync();
        var writtenAt = File.GetLastWriteTimeUtc(first);

        await Task.Delay(20);
        var second = await config.MaterializeAsync();

        Assert.Equal(first, second);
        // Unchanged content must not be rewritten.
        Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(second));
    }

    [Fact]
    public async Task MaterializeAsync_FallsBackToDataDirectoryWhenProgramDirIsNotWritable()
    {
        using var temp = new TempDirectory();

        // Make the "program directory" a regular file so writing a child path fails.
        var programAsFile = Path.Combine(temp.Path, "program-file");
        File.WriteAllText(programAsFile, "not a directory");

        var paths = new AppPaths(Path.Combine(temp.Path, "data"), programAsFile);
        var config = new DefaultDosBoxConfig(paths, NullLogger<DefaultDosBoxConfig>.Instance);

        var path = await config.MaterializeAsync();

        Assert.Equal(Path.Combine(paths.DataRoot, "dosbox.conf"), path);
        Assert.True(File.Exists(path));
    }
}
