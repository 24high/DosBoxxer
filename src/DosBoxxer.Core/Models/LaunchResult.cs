namespace DosBoxxer.Core.Models;

public enum LaunchStatus
{
    Completed,
    ExecutableNotConfigured,
    ExecutableNotFound,
    BaseConfigNotFound,
    GameDirectoryMissing,
    LaunchFileMissing,
    ConfigWriteFailed,
    StartFailed,
}

/// <summary>Outcome of a DOSBox run, including the data needed for play-time statistics.</summary>
public sealed class LaunchResult
{
    public required LaunchStatus Status { get; init; }

    public int? ExitCode { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? ExitedAt { get; init; }

    /// <summary>Path of the generated, game specific configuration file (for diagnostics).</summary>
    public string? ConfigFilePath { get; init; }

    /// <summary>Technical error detail for logging. Never shown verbatim in the UI.</summary>
    public string? ErrorDetail { get; init; }

    public TimeSpan Duration =>
        StartedAt.HasValue && ExitedAt.HasValue && ExitedAt.Value > StartedAt.Value
            ? ExitedAt.Value - StartedAt.Value
            : TimeSpan.Zero;

    public bool Success => Status == LaunchStatus.Completed;

    public static LaunchResult Failed(LaunchStatus status, string? detail = null, string? configPath = null) =>
        new() { Status = status, ErrorDetail = detail, ConfigFilePath = configPath };
}
