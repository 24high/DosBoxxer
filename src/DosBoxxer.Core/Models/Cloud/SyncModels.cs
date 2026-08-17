namespace DosBoxxer.Core.Models.Cloud;

/// <summary>What caused a synchronisation. Used for UI/logging only — it must never influence
/// which side is considered newer.</summary>
public enum SyncTrigger
{
    Manual = 0,
    GameAdded = 1,
    PreLaunch = 2,
    PostExit = 3,
}

/// <summary>State of a single file within a computed sync plan.</summary>
public enum SyncItemState
{
    /// <summary>Present on both sides and equal — nothing to do.</summary>
    Unchanged = 0,

    /// <summary>Local copy wins and will be uploaded.</summary>
    Upload = 1,

    /// <summary>Cloud copy wins and will be downloaded.</summary>
    Download = 2,

    /// <summary>Exists only locally.</summary>
    LocalOnly = 3,

    /// <summary>Exists only in the cloud.</summary>
    RemoteOnly = 4,

    /// <summary>Both sides changed and the newer side cannot be determined safely.</summary>
    Conflict = 5,
}

/// <summary>How the user (or a defaulting policy) resolved a conflict.</summary>
public enum ConflictResolution
{
    UseLocal = 0,
    UseCloud = 1,
    Skip = 2,
}

/// <summary>Overall status surfaced to the UI.</summary>
public enum SyncStatus
{
    Idle = 0,
    Running = 1,
    Success = 2,
    Conflict = 3,
    Failed = 4,
    /// <summary>Cloud is not configured/authenticated or the game has no savegames — sync skipped.</summary>
    Skipped = 5,
}

/// <summary>Machine readable failure category, so callers can react (retry, offer manual start, …).</summary>
public enum SyncErrorKind
{
    None = 0,
    NotConfigured = 1,
    NotAuthenticated = 2,
    AuthRevoked = 3,
    Network = 4,
    UploadFailed = 5,
    DownloadFailed = 6,
    Cancelled = 7,
    Unexpected = 8,
}

/// <summary>A remote file descriptor as reported by the cloud storage.</summary>
public sealed class CloudFile
{
    public required string Id { get; init; }

    /// <summary>Game-relative path with <c>/</c> separators. The comparison key.</summary>
    public required string RelativePath { get; init; }

    public long Size { get; init; }

    public DateTimeOffset ModifiedUtc { get; init; }

    /// <summary>MD5 checksum reported by the provider, if available.</summary>
    public string? Md5 { get; init; }
}

/// <summary>A single planned operation.</summary>
public sealed class SyncPlanItem
{
    public required string RelativePath { get; init; }

    public required SyncItemState State { get; set; }

    /// <summary>Human readable reason (e.g. "local newer by 42s", "only in cloud").</summary>
    public string? Reason { get; init; }

    public string? LocalAbsolutePath { get; init; }

    public long LocalSize { get; init; }

    public DateTimeOffset? LocalModifiedUtc { get; init; }

    public string? RemoteId { get; init; }

    public long RemoteSize { get; init; }

    public DateTimeOffset? RemoteModifiedUtc { get; init; }
}

/// <summary>The full, side-effect-free plan produced before any file is touched.</summary>
public sealed class SyncPlan
{
    public List<SyncPlanItem> Items { get; } = new();

    public IEnumerable<SyncPlanItem> Conflicts => Items.Where(i => i.State == SyncItemState.Conflict);

    public bool HasConflicts => Items.Any(i => i.State == SyncItemState.Conflict);

    public bool HasWork => Items.Any(i => i.State != SyncItemState.Unchanged);
}

/// <summary>Compact counts shown after a sync.</summary>
public sealed class SyncSummary
{
    public int Uploaded { get; set; }

    public int Downloaded { get; set; }

    public int Unchanged { get; set; }

    public int Conflicts { get; set; }

    public int Skipped { get; set; }

    public int Failed { get; set; }
}

/// <summary>Result of a whole <c>SyncGameAsync</c> call.</summary>
public sealed class SyncOutcome
{
    public required SyncStatus Status { get; init; }

    public SyncSummary Summary { get; init; } = new();

    public SyncErrorKind Error { get; init; } = SyncErrorKind.None;

    /// <summary>Technical detail for logs. Never shown verbatim.</summary>
    public string? Detail { get; init; }

    /// <summary>Conflicts that remained unresolved (only when <see cref="Status"/> is <see cref="SyncStatus.Conflict"/>).</summary>
    public IReadOnlyList<SyncPlanItem> UnresolvedConflicts { get; init; } = Array.Empty<SyncPlanItem>();

    public bool Success => Status is SyncStatus.Success or SyncStatus.Skipped;

    public static SyncOutcome Skipped(SyncErrorKind reason = SyncErrorKind.None) =>
        new() { Status = SyncStatus.Skipped, Error = reason };

    public static SyncOutcome Failed(SyncErrorKind error, string? detail = null, SyncSummary? summary = null) =>
        new() { Status = SyncStatus.Failed, Error = error, Detail = detail, Summary = summary ?? new SyncSummary() };
}
