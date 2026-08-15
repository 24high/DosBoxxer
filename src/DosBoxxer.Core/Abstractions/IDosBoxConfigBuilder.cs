using DosBoxxer.Core.Models;

namespace DosBoxxer.Core.Abstractions;

public sealed class DosBoxConfigRequest
{
    /// <summary>Path of the user supplied base configuration. May be <c>null</c>: DOSBox then uses its own defaults.</summary>
    public string? BaseConfigPath { get; init; }

    public required string GameDirectory { get; init; }

    public required string LaunchFile { get; init; }

    public GameDosBoxSettings? Overrides { get; init; }

    /// <summary>Appends an <c>exit</c> command so DOSBox terminates with the game.</summary>
    public bool AppendExit { get; init; }

    /// <summary>Absolute path of the configuration file that will be written.</summary>
    public required string OutputPath { get; init; }
}

public sealed class DosBoxConfigResult
{
    public required string OutputPath { get; init; }

    /// <summary>The generated autoexec lines, useful for a preview dialog and for tests.</summary>
    public required IReadOnlyList<string> GeneratedAutoexecLines { get; init; }

    /// <summary>
    /// Directory or file names that had to be shortened to DOS 8.3 form. Non-empty means the
    /// user should be warned that DOSBox may not find the path.
    /// </summary>
    public IReadOnlyList<string> MangledNames { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Produces the game specific DOSBox configuration. The base configuration file is only ever
/// read, never modified.
/// </summary>
public interface IDosBoxConfigBuilder
{
    Task<DosBoxConfigResult> BuildAsync(DosBoxConfigRequest request, CancellationToken cancellationToken = default);

    /// <summary>Renders the configuration to a string without touching the file system.</summary>
    Task<string> RenderAsync(DosBoxConfigRequest request, CancellationToken cancellationToken = default);
}
