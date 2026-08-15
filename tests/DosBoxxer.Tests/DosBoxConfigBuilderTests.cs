using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Infrastructure.DosBox;
using DosBoxxer.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DosBoxxer.Tests;

public sealed class DosBoxConfigBuilderTests
{
    private static DosBoxConfigBuilder CreateBuilder() =>
        new(NullLogger<DosBoxConfigBuilder>.Instance);

    private const string BaseConfigWithAutoexec = """
        [sdl]
        fullscreen=false
        output=surface

        [cpu]
        core=auto
        cycles=auto

        [autoexec]
        # Base commands the user already had
        mount d "/mnt/cdrom" -t cdrom
        """;

    private const string BaseConfigWithoutAutoexec = """
        [sdl]
        fullscreen=false

        [cpu]
        core=auto
        cycles=auto
        """;

    // ---- autoexec generation --------------------------------------------------------------

    [Fact]
    public void BuildAutoexecBlock_ProducesMountDriveCdAndStart()
    {
        var request = new DosBoxConfigRequest
        {
            GameDirectory = "/home/user/games/doom",
            LaunchFile = "/home/user/games/doom/BIN/DOOM.EXE",
            OutputPath = "/tmp/out.conf",
        };

        var (lines, mangled) = DosBoxConfigBuilder.BuildAutoexecBlock(request);

        Assert.Equal(DosBoxConfigBuilder.BeginMarker, lines[0]);
        Assert.Equal("mount c \"/home/user/games/doom\"", lines[1]);
        Assert.Equal("c:", lines[2]);
        Assert.Equal("cd BIN", lines[3]);
        Assert.Equal("DOOM.EXE", lines[4]);
        Assert.Equal(DosBoxConfigBuilder.EndMarker, lines[^1]);
        Assert.Empty(mangled);
    }

    [Fact]
    public void BuildAutoexecBlock_OmitsCdWhenTheStarterIsInTheRoot()
    {
        var request = new DosBoxConfigRequest
        {
            GameDirectory = "/home/user/games/doom",
            LaunchFile = "/home/user/games/doom/DOOM.EXE",
            OutputPath = "/tmp/out.conf",
        };

        var (lines, _) = DosBoxConfigBuilder.BuildAutoexecBlock(request);

        Assert.DoesNotContain(lines, l => l.StartsWith("cd ", System.StringComparison.Ordinal));
        Assert.Contains("DOOM.EXE", lines);
    }

    [Fact]
    public void BuildAutoexecBlock_QuotesDirectoriesWithSpaces()
    {
        var request = new DosBoxConfigRequest
        {
            GameDirectory = "/home/user/My DOS Games/Doom II",
            LaunchFile = "/home/user/My DOS Games/Doom II/DOOM2.EXE",
            OutputPath = "/tmp/out.conf",
        };

        var (lines, _) = DosBoxConfigBuilder.BuildAutoexecBlock(request);

        Assert.Equal("mount c \"/home/user/My DOS Games/Doom II\"", lines[1]);
    }

    [Fact]
    public void BuildAutoexecBlock_HandlesDeeplyNestedStarters()
    {
        var request = new DosBoxConfigRequest
        {
            GameDirectory = "/games/x",
            LaunchFile = "/games/x/a/b/c/d/RUN.EXE",
            OutputPath = "/tmp/out.conf",
        };

        var (lines, _) = DosBoxConfigBuilder.BuildAutoexecBlock(request);

        Assert.Contains(@"cd A\B\C\D", lines);
        Assert.Contains("RUN.EXE", lines);
    }

    [Fact]
    public void BuildAutoexecBlock_SupportsBatAndComStarters()
    {
        var batRequest = new DosBoxConfigRequest
        {
            GameDirectory = "/games/x",
            LaunchFile = "/games/x/START.BAT",
            OutputPath = "/tmp/out.conf",
        };

        var comRequest = new DosBoxConfigRequest
        {
            GameDirectory = "/games/x",
            LaunchFile = "/games/x/GAME.COM",
            OutputPath = "/tmp/out.conf",
        };

        Assert.Contains("START.BAT", DosBoxConfigBuilder.BuildAutoexecBlock(batRequest).Lines);
        Assert.Contains("GAME.COM", DosBoxConfigBuilder.BuildAutoexecBlock(comRequest).Lines);
    }

    [Fact]
    public void BuildAutoexecBlock_ReportsNamesThatHadToBeShortened()
    {
        var request = new DosBoxConfigRequest
        {
            GameDirectory = "/games/x",
            LaunchFile = "/games/x/LongDirectoryName/START.EXE",
            OutputPath = "/tmp/out.conf",
        };

        var (lines, mangled) = DosBoxConfigBuilder.BuildAutoexecBlock(request);

        Assert.Contains("cd LONGDI~1", lines);
        Assert.Contains("LongDirectoryName", mangled);
    }

    [Fact]
    public void BuildAutoexecBlock_AppendsExitWhenRequested()
    {
        var request = new DosBoxConfigRequest
        {
            GameDirectory = "/games/x",
            LaunchFile = "/games/x/GAME.EXE",
            AppendExit = true,
            OutputPath = "/tmp/out.conf",
        };

        var (lines, _) = DosBoxConfigBuilder.BuildAutoexecBlock(request);

        Assert.Equal("exit", lines[^2]);
    }

    [Fact]
    public void BuildAutoexecBlock_InsertsPreLaunchCommandsBeforeTheStarter()
    {
        var request = new DosBoxConfigRequest
        {
            GameDirectory = "/games/x",
            LaunchFile = "/games/x/GAME.EXE",
            Overrides = new GameDosBoxSettings { PreLaunchCommands = "SET BLASTER=A220 I7 D1\nLOADFIX" },
            OutputPath = "/tmp/out.conf",
        };

        var (lines, _) = DosBoxConfigBuilder.BuildAutoexecBlock(request);

        var blasterIndex = lines.ToList().IndexOf("SET BLASTER=A220 I7 D1");
        var gameIndex = lines.ToList().IndexOf("GAME.EXE");

        Assert.True(blasterIndex > 0);
        Assert.True(blasterIndex < gameIndex);
        Assert.Contains("LOADFIX", lines);
    }

    // ---- full configuration ---------------------------------------------------------------

    [Fact]
    public async Task BuildAsync_KeepsExistingAutoexecCommands()
    {
        using var temp = new TempDirectory();
        var basePath = temp.CreateFile("dosbox.conf", BaseConfigWithAutoexec);
        var output = Path.Combine(temp.Path, "game.conf");

        await CreateBuilder().BuildAsync(new DosBoxConfigRequest
        {
            BaseConfigPath = basePath,
            GameDirectory = "/games/doom",
            LaunchFile = "/games/doom/DOOM.EXE",
            OutputPath = output,
        });

        var content = await File.ReadAllTextAsync(output);

        Assert.Contains("mount d \"/mnt/cdrom\" -t cdrom", content);
        Assert.Contains("mount c \"/games/doom\"", content);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(content, @"^\[autoexec\]$",
            System.Text.RegularExpressions.RegexOptions.Multiline));
    }

    [Fact]
    public async Task BuildAsync_CreatesAutoexecSectionWhenMissing()
    {
        using var temp = new TempDirectory();
        var basePath = temp.CreateFile("dosbox.conf", BaseConfigWithoutAutoexec);
        var output = Path.Combine(temp.Path, "game.conf");

        await CreateBuilder().BuildAsync(new DosBoxConfigRequest
        {
            BaseConfigPath = basePath,
            GameDirectory = "/games/doom",
            LaunchFile = "/games/doom/DOOM.EXE",
            OutputPath = output,
        });

        var content = await File.ReadAllTextAsync(output);

        Assert.Contains("[autoexec]", content);
        Assert.Contains("mount c \"/games/doom\"", content);
        Assert.Contains("[cpu]", content);
    }

    [Fact]
    public async Task BuildAsync_NeverModifiesTheBaseConfiguration()
    {
        using var temp = new TempDirectory();
        var basePath = temp.CreateFile("dosbox.conf", BaseConfigWithAutoexec);
        var before = await File.ReadAllTextAsync(basePath);
        var output = Path.Combine(temp.Path, "game.conf");

        await CreateBuilder().BuildAsync(new DosBoxConfigRequest
        {
            BaseConfigPath = basePath,
            GameDirectory = "/games/doom",
            LaunchFile = "/games/doom/DOOM.EXE",
            Overrides = new GameDosBoxSettings { Cycles = "20000", Fullscreen = true },
            OutputPath = output,
        });

        Assert.Equal(before, await File.ReadAllTextAsync(basePath));
    }

    [Fact]
    public async Task BuildAsync_AppliesPerGameOverrides()
    {
        using var temp = new TempDirectory();
        var basePath = temp.CreateFile("dosbox.conf", BaseConfigWithAutoexec);
        var output = Path.Combine(temp.Path, "game.conf");

        await CreateBuilder().BuildAsync(new DosBoxConfigRequest
        {
            BaseConfigPath = basePath,
            GameDirectory = "/games/doom",
            LaunchFile = "/games/doom/DOOM.EXE",
            Overrides = new GameDosBoxSettings
            {
                Cycles = "20000",
                Core = "dynamic",
                Machine = "svga_s3",
                MemSize = "32",
                Fullscreen = true,
                Aspect = true,
                Scaler = "normal2x",
            },
            OutputPath = output,
        });

        var document = DosBoxConfigDocument.Parse(await File.ReadAllTextAsync(output));

        Assert.Equal("20000", document.GetValue("cpu", "cycles"));
        Assert.Equal("dynamic", document.GetValue("cpu", "core"));
        Assert.Equal("svga_s3", document.GetValue("dosbox", "machine"));
        Assert.Equal("32", document.GetValue("dosbox", "memsize"));
        Assert.Equal("true", document.GetValue("sdl", "fullscreen"));
        Assert.Equal("true", document.GetValue("render", "aspect"));
        Assert.Equal("normal2x", document.GetValue("render", "scaler"));
    }

    [Fact]
    public async Task BuildAsync_WorksWithoutAnyBaseConfiguration()
    {
        using var temp = new TempDirectory();
        var output = Path.Combine(temp.Path, "game.conf");

        var result = await CreateBuilder().BuildAsync(new DosBoxConfigRequest
        {
            BaseConfigPath = null,
            GameDirectory = "/games/doom",
            LaunchFile = "/games/doom/DOOM.EXE",
            OutputPath = output,
        });

        var content = await File.ReadAllTextAsync(result.OutputPath);

        Assert.Contains("[autoexec]", content);
        Assert.Contains("mount c \"/games/doom\"", content);
    }

    [Fact]
    public async Task BuildAsync_AppliesFreeFormConfigurationLines()
    {
        using var temp = new TempDirectory();
        var basePath = temp.CreateFile("dosbox.conf", BaseConfigWithoutAutoexec);
        var output = Path.Combine(temp.Path, "game.conf");

        await CreateBuilder().BuildAsync(new DosBoxConfigRequest
        {
            BaseConfigPath = basePath,
            GameDirectory = "/games/doom",
            LaunchFile = "/games/doom/DOOM.EXE",
            Overrides = new GameDosBoxSettings
            {
                AdditionalConfigLines = "[midi]\nmpu401=intelligent\nmididevice=default",
            },
            OutputPath = output,
        });

        var document = DosBoxConfigDocument.Parse(await File.ReadAllTextAsync(output));

        Assert.Equal("intelligent", document.GetValue("midi", "mpu401"));
        Assert.Equal("default", document.GetValue("midi", "mididevice"));
    }
}

public sealed class DosBoxConfigDocumentTests
{
    [Fact]
    public void Parse_And_Render_PreserveCommentsAndOrder()
    {
        const string input = """
            # global comment
            [sdl]
            # keep me
            fullscreen=false

            [cpu]
            core=auto
            """;

        var rendered = DosBoxConfigDocument.Parse(input).Render();

        Assert.Contains("# global comment", rendered);
        Assert.Contains("# keep me", rendered);
        Assert.Contains("[sdl]", rendered);
        Assert.Contains("[cpu]", rendered);
    }

    [Fact]
    public void SetValue_ReplacesExistingKeyInPlace()
    {
        var document = DosBoxConfigDocument.Parse("[cpu]\ncore=auto\ncycles=auto\n");
        document.SetValue("cpu", "cycles", "max");

        var rendered = document.Render();

        Assert.Contains("cycles=max", rendered);
        Assert.DoesNotContain("cycles=auto", rendered);
        Assert.Equal("max", document.GetValue("cpu", "cycles"));
    }

    [Fact]
    public void SetValue_IsCaseInsensitiveForKeysAndSections()
    {
        var document = DosBoxConfigDocument.Parse("[CPU]\nCycles=auto\n");
        document.SetValue("cpu", "cycles", "max");

        Assert.Equal("max", document.GetValue("CPU", "CYCLES"));
    }

    [Fact]
    public void SetValue_CreatesMissingSection()
    {
        var document = DosBoxConfigDocument.Parse("[cpu]\ncore=auto\n");
        document.SetValue("render", "aspect", "true");

        Assert.Contains("[render]", document.Render());
        Assert.Equal("true", document.GetValue("render", "aspect"));
    }

    [Fact]
    public void Parse_MergesDuplicateSectionHeaders()
    {
        var document = DosBoxConfigDocument.Parse("[autoexec]\necho one\n\n[autoexec]\necho two\n");
        var rendered = document.Render();

        Assert.Single(System.Text.RegularExpressions.Regex.Matches(rendered, @"^\[autoexec\]$",
            System.Text.RegularExpressions.RegexOptions.Multiline));
        Assert.Contains("echo one", rendered);
        Assert.Contains("echo two", rendered);
    }

    [Fact]
    public void AppendLines_AddsToTheEndOfTheSection()
    {
        var document = DosBoxConfigDocument.Parse("[autoexec]\necho first\n");
        document.AppendLines("autoexec", new[] { "echo second" });

        var rendered = document.Render();

        Assert.True(rendered.IndexOf("echo first", System.StringComparison.Ordinal)
                    < rendered.IndexOf("echo second", System.StringComparison.Ordinal));
    }

    [Fact]
    public void ApplyAdditionalLines_ReportsUnparseableLines()
    {
        var document = DosBoxConfigDocument.Parse("[cpu]\ncore=auto\n");
        document.ApplyAdditionalLines("cycles=max\nnonsense line\n# comment", "cpu", out var ignored);

        Assert.Equal("max", document.GetValue("cpu", "cycles"));
        Assert.Contains("nonsense line", ignored);
    }
}
