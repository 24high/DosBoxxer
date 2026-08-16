using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Infrastructure;
using DosBoxxer.Core.Infrastructure.Database;
using DosBoxxer.Core.Infrastructure.DosBox;
using DosBoxxer.Core.Infrastructure.Repositories;
using DosBoxxer.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DosBoxxer.Tests;

/// <summary>
/// End-to-end coverage of the launch path: configuration generation, process start with an
/// argument vector, exit handling and the resulting play statistics.
///
/// A small script stands in for DOSBox, so the test needs no emulator installed. It records the
/// arguments it was given, which is what proves the <c>-conf</c> handover works.
/// </summary>
public sealed class DosBoxLauncherTests
{
    private sealed class StubSettings : ISettingsService
    {
        public AppSettings Current { get; } = new();

        public event EventHandler<AppSettings>? SettingsChanged;

        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
        {
            SettingsChanged?.Invoke(this, settings);
            return Task.CompletedTask;
        }

        public Task SaveCurrentAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>Creates an executable stub that appends its arguments to <c>args.txt</c>.</summary>
    private static string CreateFakeDosBox(TempDirectory temp)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var cmd = Path.Combine(temp.Path, "fake-dosbox.cmd");
            File.WriteAllText(cmd, "@echo off\r\n(echo %*)>> \"%~dp0args.txt\"\r\nexit /b 0\r\n");
            return cmd;
        }

        var script = Path.Combine(temp.Path, "fake-dosbox.sh");
        File.WriteAllText(script, "#!/bin/sh\nprintf '%s\\n' \"$@\" >> \"$(dirname \"$0\")/args.txt\"\nexit 0\n");
        File.SetUnixFileMode(
            script,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        return script;
    }

    private static (DosBoxLauncher Launcher, StubSettings Settings, AppPaths Paths) CreateLauncher(TempDirectory temp)
    {
        // Program directory points at a writable temp folder so the default config materialises there.
        var paths = new AppPaths(Path.Combine(temp.Path, "appdata"), Path.Combine(temp.Path, "program"));
        paths.EnsureCreated();
        Directory.CreateDirectory(paths.ProgramDirectory);

        var settings = new StubSettings();
        var builder = new DosBoxConfigBuilder(NullLogger<DosBoxConfigBuilder>.Instance);
        var defaultConfig = new DefaultDosBoxConfig(paths, NullLogger<DefaultDosBoxConfig>.Instance);
        var launcher = new DosBoxLauncher(settings, builder, defaultConfig, paths, NullLogger<DosBoxLauncher>.Instance);

        return (launcher, settings, paths);
    }

    private static Game CreateGame(TempDirectory temp, string relativeLaunchFile)
    {
        var gameDirectory = temp.CreateSubdirectory("games/doom");
        var launchFile = temp.CreateFile(Path.Combine("games", "doom", relativeLaunchFile), "stub");

        return new Game
        {
            Title = "Doom",
            GameDirectory = gameDirectory,
            LaunchFile = launchFile,
            RelativeLaunchFile = relativeLaunchFile,
        };
    }

    [Fact]
    public async Task Launch_GeneratesConfigStartsTheProcessAndReportsSuccess()
    {
        using var temp = new TempDirectory();
        var (launcher, settings, _) = CreateLauncher(temp);

        settings.Current.DosBoxExecutablePath = CreateFakeDosBox(temp);
        var game = CreateGame(temp, Path.Combine("BIN", "DOOM.EXE"));

        var result = await launcher.LaunchAsync(game);

        Assert.True(result.Success, result.ErrorDetail);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.StartedAt);
        Assert.NotNull(result.ExitedAt);
        Assert.NotNull(result.ConfigFilePath);
        Assert.True(File.Exists(result.ConfigFilePath!));

        var config = await File.ReadAllTextAsync(result.ConfigFilePath!);
        Assert.Contains("[autoexec]", config);
        Assert.Contains($"mount c \"{game.GameDirectory}\"", config);
        Assert.Contains("c:", config);
        Assert.Contains("cd BIN", config);
        Assert.Contains("DOOM.EXE", config);

        // The stub recorded exactly what DOSBox would have received.
        var recorded = await File.ReadAllLinesAsync(Path.Combine(temp.Path, "args.txt"));
        Assert.Equal("-conf", recorded[0]);
        Assert.Equal(result.ConfigFilePath, recorded[1]);
    }

    [Fact]
    public async Task Launch_PassesAdditionalArgumentsAsSeparateVectorEntries()
    {
        using var temp = new TempDirectory();
        var (launcher, settings, _) = CreateLauncher(temp);

        settings.Current.DosBoxExecutablePath = CreateFakeDosBox(temp);

        // A value containing a space and a shell metacharacter must survive as ONE argument.
        settings.Current.DosBoxAdditionalArguments = "-noconsole \"; touch /tmp/pwned\"";

        var game = CreateGame(temp, "DOOM.EXE");
        var result = await launcher.LaunchAsync(game);

        Assert.True(result.Success);

        var recorded = await File.ReadAllLinesAsync(Path.Combine(temp.Path, "args.txt"));

        Assert.Equal("-noconsole", recorded[2]);
        Assert.Equal("; touch /tmp/pwned", recorded[3]);
        Assert.False(File.Exists("/tmp/pwned"));
    }

    [Fact]
    public async Task Launch_UsesTheEmbeddedDefaultWhenNoBaseConfigIsConfigured()
    {
        using var temp = new TempDirectory();
        var (launcher, settings, paths) = CreateLauncher(temp);

        settings.Current.DosBoxExecutablePath = CreateFakeDosBox(temp);
        settings.Current.BaseDosBoxConfigPath = null; // no override → embedded default

        var game = CreateGame(temp, "DOOM.EXE");
        var result = await launcher.LaunchAsync(game);

        Assert.True(result.Success, result.ErrorDetail);

        // The default was materialised into the program directory, and the generated per-game
        // config inherits its fullscreen/aspect settings.
        Assert.True(File.Exists(Path.Combine(paths.ProgramDirectory, "dosbox.conf")));

        var config = await File.ReadAllTextAsync(result.ConfigFilePath!);
        Assert.Contains("fullscreen=true", config);
        Assert.Contains("aspect=true", config);
        Assert.Contains("mount c", config);
    }

    [Fact]
    public async Task Launch_MergesPerGameOverridesIntoTheGeneratedConfig()
    {
        using var temp = new TempDirectory();
        var (launcher, settings, _) = CreateLauncher(temp);

        settings.Current.DosBoxExecutablePath = CreateFakeDosBox(temp);
        settings.Current.BaseDosBoxConfigPath = temp.CreateFile("base.conf", "[cpu]\ncycles=auto\n\n[autoexec]\necho base\n");

        var game = CreateGame(temp, "DOOM.EXE");
        game.DosBoxSettings = new GameDosBoxSettings { Cycles = "20000", Fullscreen = true };

        var result = await launcher.LaunchAsync(game);

        var document = DosBoxConfigDocument.Parse(await File.ReadAllTextAsync(result.ConfigFilePath!));

        Assert.Equal("20000", document.GetValue("cpu", "cycles"));
        Assert.Equal("true", document.GetValue("sdl", "fullscreen"));
        Assert.Contains("echo base", await File.ReadAllTextAsync(result.ConfigFilePath!));
    }

    [Theory]
    [InlineData(false, false, LaunchStatus.ExecutableNotConfigured)]
    [InlineData(true, false, LaunchStatus.ExecutableNotFound)]
    public async Task Launch_ReportsConfigurationProblemsWithoutThrowing(
        bool setPath,
        bool pathExists,
        LaunchStatus expected)
    {
        using var temp = new TempDirectory();
        var (launcher, settings, _) = CreateLauncher(temp);

        if (setPath)
        {
            settings.Current.DosBoxExecutablePath = pathExists
                ? CreateFakeDosBox(temp)
                : Path.Combine(temp.Path, "does-not-exist");
        }

        var game = CreateGame(temp, "DOOM.EXE");

        var result = await launcher.LaunchAsync(game);

        Assert.False(result.Success);
        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public async Task Launch_DetectsAMissingGameDirectoryAndLaunchFile()
    {
        using var temp = new TempDirectory();
        var (launcher, settings, _) = CreateLauncher(temp);
        settings.Current.DosBoxExecutablePath = CreateFakeDosBox(temp);

        var missingDirectory = new Game
        {
            Title = "Gone",
            GameDirectory = Path.Combine(temp.Path, "not-here"),
            LaunchFile = Path.Combine(temp.Path, "not-here", "G.EXE"),
        };

        Assert.Equal(LaunchStatus.GameDirectoryMissing, (await launcher.LaunchAsync(missingDirectory)).Status);

        var game = CreateGame(temp, "DOOM.EXE");
        File.Delete(game.LaunchFile);

        Assert.Equal(LaunchStatus.LaunchFileMissing, (await launcher.LaunchAsync(game)).Status);
    }

    [Fact]
    public async Task Launch_ReportsAMissingBaseConfiguration()
    {
        using var temp = new TempDirectory();
        var (launcher, settings, _) = CreateLauncher(temp);

        settings.Current.DosBoxExecutablePath = CreateFakeDosBox(temp);
        settings.Current.BaseDosBoxConfigPath = Path.Combine(temp.Path, "missing.conf");

        var result = await launcher.LaunchAsync(CreateGame(temp, "DOOM.EXE"));

        Assert.Equal(LaunchStatus.BaseConfigNotFound, result.Status);
    }

    [Fact]
    public async Task PlayStatistics_AreUpdatedAfterARun()
    {
        using var temp = new TempDirectory();
        var (launcher, settings, paths) = CreateLauncher(temp);

        settings.Current.DosBoxExecutablePath = CreateFakeDosBox(temp);

        var factory = new SqliteConnectionFactory(paths);
        await new DatabaseInitializer(factory, NullLogger<DatabaseInitializer>.Instance).InitializeAsync();
        var repository = new GameRepository(factory, NullLogger<GameRepository>.Instance);

        var game = CreateGame(temp, "DOOM.EXE");
        await repository.AddAsync(game);

        var result = await launcher.LaunchAsync(game);
        Assert.True(result.Success);

        await repository.RecordPlaySessionAsync(
            game.Id,
            result.StartedAt!.Value,
            (long)result.Duration.TotalSeconds);

        var reloaded = await repository.GetAsync(game.Id);

        Assert.Equal(1, reloaded!.PlayCount);
        Assert.NotNull(reloaded.LastPlayed);
        Assert.True(reloaded.TotalPlayTimeSeconds >= 0);
    }
}
