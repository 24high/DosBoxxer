using DosBoxxer.Core.Models.Cloud;

namespace DosBoxxer.Core.Abstractions;

/// <summary>Progress notification raised while a sync runs (for unobtrusive UI status).</summary>
public sealed class SyncProgress
{
    public required SyncTrigger Trigger { get; init; }

    public required string Phase { get; init; }
}

/// <summary>
/// Callback the UI implements to resolve conflicts that the automatic rules could not decide.
/// The engine calls it once with all conflicts of the current sync; returning <c>null</c> means
/// the user cancelled the whole sync.
/// </summary>
public interface IConflictResolver
{
    Task<ConflictDecision?> ResolveAsync(IReadOnlyList<SyncPlanItem> conflicts, SyncTrigger trigger, CancellationToken cancellationToken = default);
}

/// <summary>A conflict decision, optionally applied to every remaining conflict of the sync.</summary>
public sealed class ConflictDecision
{
    public required IReadOnlyDictionary<string, ConflictResolution> PerFile { get; init; }
}

/// <summary>
/// The single entry point for all synchronisation — manual and every automatic trigger use it.
/// Never builds more than one plan implementation and guarantees at most one active sync per game.
/// </summary>
public interface ICloudSyncService
{
    /// <summary><c>cloudConfigured &amp;&amp; cloudAuthenticated</c>. Every auto-sync point checks this first.</summary>
    bool IsCloudUsable { get; }

    /// <summary>True while a sync for <paramref name="gameId"/> is in progress.</summary>
    bool IsSyncing(Guid gameId);

    /// <summary>
    /// Synchronises one game's savegames. Skips silently (no error) when the cloud is not usable
    /// or the game has no savegame configuration. Uses the same comparison/conflict logic for
    /// every <paramref name="trigger"/>; the trigger only affects messaging and error handling.
    /// </summary>
    Task<SyncOutcome> SyncGameAsync(
        Guid gameId,
        SyncTrigger trigger,
        IConflictResolver? conflictResolver = null,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
