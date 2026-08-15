using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.DosBox;

/// <summary>
/// Produces the temporary, game specific <c>dosbox.conf</c>.
///
/// Order of composition:
/// <list type="number">
/// <item>full content of the user's base configuration (read-only, never modified),</item>
/// <item>structured per-game overrides ([cpu], [dosbox], [render], [sdl], ...),</item>
/// <item>free-form per-game configuration lines,</item>
/// <item>a clearly delimited block appended to <c>[autoexec]</c> with mount / drive / cd / start.</item>
/// </list>
/// </summary>
public sealed class DosBoxConfigBuilder : IDosBoxConfigBuilder
{
    public const string BeginMarker = "REM --- DOSBox Launcher generated commands ---";
    public const string EndMarker = "REM --- End DOSBox Launcher generated commands ---";
    public const string AutoexecSection = "autoexec";

    private const string MountDrive = "c";

    private readonly ILogger<DosBoxConfigBuilder> _logger;

    public DosBoxConfigBuilder(ILogger<DosBoxConfigBuilder> logger) => _logger = logger;

    public async Task<DosBoxConfigResult> BuildAsync(DosBoxConfigRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var (content, autoexecLines, mangled) = await ComposeAsync(request, cancellationToken).ConfigureAwait(false);

        var directory = Path.GetDirectoryName(request.OutputPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(request.OutputPath, content, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Generated DOSBox configuration at {Path} ({LineCount} autoexec lines, {MangledCount} mangled name(s))",
            request.OutputPath,
            autoexecLines.Count,
            mangled.Count);

        return new DosBoxConfigResult
        {
            OutputPath = request.OutputPath,
            GeneratedAutoexecLines = autoexecLines,
            MangledNames = mangled,
        };
    }

    public async Task<string> RenderAsync(DosBoxConfigRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var (content, _, _) = await ComposeAsync(request, cancellationToken).ConfigureAwait(false);
        return content;
    }

    private async Task<(string Content, IReadOnlyList<string> AutoexecLines, IReadOnlyList<string> Mangled)> ComposeAsync(
        DosBoxConfigRequest request,
        CancellationToken cancellationToken)
    {
        DosBoxConfigDocument document;

        if (!string.IsNullOrWhiteSpace(request.BaseConfigPath) && File.Exists(request.BaseConfigPath))
        {
            document = await DosBoxConfigDocument.LoadAsync(request.BaseConfigPath, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(request.BaseConfigPath))
            {
                _logger.LogWarning("Base DOSBox configuration not found, generating a minimal configuration instead");
            }

            document = DosBoxConfigDocument.CreateEmpty();
        }

        ApplyOverrides(document, request.Overrides);

        var (autoexecLines, mangled) = BuildAutoexecBlock(request);
        document.AppendLines(AutoexecSection, autoexecLines);

        return (document.Render(), autoexecLines, mangled);
    }

    internal static void ApplyOverrides(DosBoxConfigDocument document, GameDosBoxSettings? overrides)
    {
        if (overrides is null || !overrides.HasAnyOverride)
        {
            return;
        }

        SetIfPresent(document, "cpu", "cycles", overrides.Cycles);
        SetIfPresent(document, "cpu", "core", overrides.Core);
        SetIfPresent(document, "dosbox", "machine", overrides.Machine);
        SetIfPresent(document, "dosbox", "memsize", overrides.MemSize);
        SetIfPresent(document, "render", "scaler", overrides.Scaler);
        SetIfPresent(document, "sdl", "output", overrides.Output);
        SetIfPresent(document, "mixer", "rate", overrides.MixerRate);
        SetIfPresent(document, "sblaster", "sbtype", overrides.SoundBlasterType);

        if (overrides.Aspect.HasValue)
        {
            document.SetValue("render", "aspect", Bool(overrides.Aspect.Value));
        }

        if (overrides.Fullscreen.HasValue)
        {
            document.SetValue("sdl", "fullscreen", Bool(overrides.Fullscreen.Value));
        }

        if (overrides.PcSpeaker.HasValue)
        {
            document.SetValue("speaker", "pcspeaker", Bool(overrides.PcSpeaker.Value));
        }

        document.ApplyAdditionalLines(overrides.AdditionalConfigLines, "dosbox", out _);
    }

    /// <summary>
    /// Builds the delimited autoexec block:
    /// <code>
    /// REM --- DOSBox Launcher generated commands ---
    /// mount c "/home/user/games/doom"
    /// c:
    /// cd BIN
    /// DOOM.EXE
    /// REM --- End DOSBox Launcher generated commands ---
    /// </code>
    /// </summary>
    internal static (IReadOnlyList<string> Lines, IReadOnlyList<string> Mangled) BuildAutoexecBlock(DosBoxConfigRequest request)
    {
        var lines = new List<string> { BeginMarker };
        var mangled = new List<string>();

        var gameDirectory = PathHelper.Normalize(request.GameDirectory);
        var launchFile = PathHelper.Normalize(request.LaunchFile);

        var quoted = DosPathConverter.QuoteHostPathForMount(gameDirectory);
        if (quoted is null)
        {
            // A double quote in the host path cannot be expressed inside the DOSBox shell.
            lines.Add("REM The game directory contains a character that cannot be mounted.");
            lines.Add(EndMarker);
            return (lines, mangled);
        }

        lines.Add($"mount {MountDrive} {quoted}");
        lines.Add(MountDrive + ":");

        var relative = PathHelper.GetRelativePathWithin(gameDirectory, launchFile);

        string launchFileName;
        if (relative is null)
        {
            // Launch file outside the game directory: fall back to just the file name so the
            // configuration stays syntactically valid; the launcher validates this beforehand.
            launchFileName = Path.GetFileName(launchFile);
        }
        else
        {
            var relativeDirectory = Path.GetDirectoryName(relative);
            launchFileName = Path.GetFileName(relative);

            if (!string.IsNullOrEmpty(relativeDirectory))
            {
                var conversion = DosPathConverter.ConvertRelativePath(relativeDirectory);
                mangled.AddRange(conversion.MangledNames);

                if (conversion.DosPath.Length > 0)
                {
                    lines.Add("cd " + conversion.DosPath);
                }
            }
        }

        var dosFileName = DosPathConverter.ToDosName(launchFileName);
        if (!string.Equals(dosFileName, launchFileName.ToUpperInvariant(), StringComparison.Ordinal))
        {
            mangled.Add(launchFileName);
        }

        if (!string.IsNullOrWhiteSpace(request.Overrides?.PreLaunchCommands))
        {
            foreach (var command in SplitCommands(request.Overrides.PreLaunchCommands))
            {
                lines.Add(command);
            }
        }

        // A .BAT file is executed by the DOSBox shell just like an .EXE/.COM.
        lines.Add(dosFileName);

        if (request.AppendExit)
        {
            lines.Add("exit");
        }

        lines.Add(EndMarker);
        return (lines, mangled);
    }

    private static IEnumerable<string> SplitCommands(string block)
    {
        using var reader = new StringReader(block);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }

    private static void SetIfPresent(DosBoxConfigDocument document, string section, string key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            document.SetValue(section, key, value.Trim());
        }
    }

    private static string Bool(bool value) => value ? "true" : "false";
}
