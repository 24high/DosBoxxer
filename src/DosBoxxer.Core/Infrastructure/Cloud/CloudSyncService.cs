using System.Collections.Concurrent;
using System.Security.Cryptography;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Infrastructure.Savegame;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Cloud;
using DosBoxxer.Core.Models.Savegame;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Cloud;

/// <summary>
/// The single synchronisation engine used by manual and every automatic trigger. A sync always
/// runs in three phases: build a side-effect-free <see cref="SyncPlan"/>, resolve any conflicts,
/// then execute the plan with data-loss-safe file operations. At most one sync runs per game
/// (a per-game gate serialises overlapping triggers).
/// </summary>
public sealed class CloudSyncService : ICloudSyncService
{
    /// <summary>Timestamps closer than this are treated as "equal" (FAT has 2s resolution).</summary>
    private static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(2);

    private readonly IGameRepository _repository;
    private readonly ILocalSavegameScanner _scanner;
    private readonly ICloudStorage _storage;
    private readonly ICloudAuthService _auth;
    private readonly ISyncMetadataStore _metadataStore;
    private readonly ISettingsService _settings;
    private readonly IAppPaths _paths;
    private readonly ILogger<CloudSyncService> _logger;

    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _gates = new();
    private readonly ConcurrentDictionary<Guid, byte> _active = new();

    public CloudSyncService(
        IGameRepository repository,
        ILocalSavegameScanner scanner,
        ICloudStorage storage,
        ICloudAuthService auth,
        ISyncMetadataStore metadataStore,
        ISettingsService settings,
        IAppPaths paths,
        ILogger<CloudSyncService> logger)
    {
        _repository = repository;
        _scanner = scanner;
        _storage = storage;
        _auth = auth;
        _metadataStore = metadataStore;
        _settings = settings;
        _paths = paths;
        _logger = logger;
    }

    public bool IsCloudUsable => _settings.Current.GoogleDrive.IsConfigured && _auth.IsAuthenticated;

    public bool IsSyncing(Guid gameId) => _active.ContainsKey(gameId);

    public async Task<SyncOutcome> SyncGameAsync(
        Guid gameId,
        SyncTrigger trigger,
        IConflictResolver? conflictResolver = null,
        IProgress<SyncProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var game = await _repository.GetAsync(gameId, cancellationToken).ConfigureAwait(false);
        if (game is null)
        {
            return SyncOutcome.Skipped();
        }

        // Games without a savegame configuration are never auto-synced.
        if (!game.SavegameConfig.IsConfigured || !game.SavegameConfig.HasSavegames)
        {
            return SyncOutcome.Skipped();
        }

        // The central cloudConfigured && cloudAuthenticated gate. When it is not met the sync is
        // skipped silently so the user is never nagged for a cloud they did not set up.
        if (!_settings.Current.GoogleDrive.IsConfigured)
        {
            return SyncOutcome.Skipped(SyncErrorKind.NotConfigured);
        }

        if (!_auth.IsAuthenticated)
        {
            return SyncOutcome.Skipped(SyncErrorKind.NotAuthenticated);
        }

        var gate = _gates.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _active[gameId] = 0;
        try
        {
            return await RunAsync(game, trigger, conflictResolver, progress, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _active.TryRemove(gameId, out _);
            gate.Release();
        }
    }

    private async Task<SyncOutcome> RunAsync(
        Game game,
        SyncTrigger trigger,
        IConflictResolver? conflictResolver,
        IProgress<SyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var metadata = await _metadataStore.LoadAsync(game.Id, cancellationToken).ConfigureAwait(false);

        try
        {
            progress?.Report(new SyncProgress { Trigger = trigger, Phase = "connect" });

            var folderId = await _storage.EnsureGameFolderAsync(game.Id, game.Title, metadata.DriveFolderId, cancellationToken)
                .ConfigureAwait(false);

            if (!string.Equals(folderId, metadata.DriveFolderId, StringComparison.Ordinal))
            {
                metadata.DriveFolderId = folderId;
                await _metadataStore.SaveAsync(metadata, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report(new SyncProgress { Trigger = trigger, Phase = "compare" });

            var local = _scanner.Enumerate(game);
            var remote = await _storage.ListFilesAsync(folderId, cancellationToken).ConfigureAwait(false);

            var plan = BuildPlan(local, remote, metadata);

            // Resolve conflicts before touching any file.
            if (plan.HasConflicts)
            {
                if (conflictResolver is null)
                {
                    return new SyncOutcome
                    {
                        Status = SyncStatus.Conflict,
                        UnresolvedConflicts = plan.Conflicts.ToList(),
                        Summary = Summarise(plan),
                    };
                }

                var decision = await conflictResolver
                    .ResolveAsync(plan.Conflicts.ToList(), trigger, cancellationToken)
                    .ConfigureAwait(false);

                if (decision is null)
                {
                    // User cancelled the sync.
                    return new SyncOutcome
                    {
                        Status = SyncStatus.Conflict,
                        Error = SyncErrorKind.Cancelled,
                        UnresolvedConflicts = plan.Conflicts.ToList(),
                        Summary = Summarise(plan),
                    };
                }

                ApplyConflictDecision(plan, decision);
            }

            progress?.Report(new SyncProgress { Trigger = trigger, Phase = "transfer" });

            var summary = await ExecuteAsync(game, folderId, plan, metadata, cancellationToken).ConfigureAwait(false);

            metadata.LastSuccessfulSyncUtc = DateTimeOffset.UtcNow;
            await _metadataStore.SaveAsync(metadata, cancellationToken).ConfigureAwait(false);

            var status = summary.Failed > 0 ? SyncStatus.Failed : SyncStatus.Success;
            return new SyncOutcome { Status = status, Summary = summary, Error = summary.Failed > 0 ? SyncErrorKind.Unexpected : SyncErrorKind.None };
        }
        catch (OperationCanceledException)
        {
            await _metadataStore.SaveAsync(metadata, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return SyncOutcome.Failed(SyncErrorKind.Cancelled);
        }
        catch (CloudStorageException ex)
        {
            _logger.LogWarning(ex, "Sync for '{Title}' failed ({Kind})", game.Title, ex.Kind);
            // Persist any folder id / partial metadata already gathered; local saves are untouched.
            await _metadataStore.SaveAsync(metadata, cancellationToken: CancellationToken.None).ConfigureAwait(false);
            return SyncOutcome.Failed(ex.Kind, ex.Message);
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException)
        {
            _logger.LogError(ex, "Unexpected sync failure for '{Title}'", game.Title);
            return SyncOutcome.Failed(SyncErrorKind.Network, ex.Message);
        }
    }

    // ---- planning -------------------------------------------------------------------------

    /// <summary>
    /// Builds the comparison plan. "Newer wins" is the automatic rule; a genuine both-sides-changed
    /// situation (or an ambiguous near-equal timestamp with differing content) becomes a conflict
    /// that is never resolved silently. Missing files are treated conservatively — a file absent on
    /// one side is uploaded/downloaded, never used as a reason to delete the other side.
    /// </summary>
    internal SyncPlan BuildPlan(
        IReadOnlyList<LocalSavegameFile> localFiles,
        IReadOnlyList<CloudFile> remoteFiles,
        GameSyncMetadata metadata)
    {
        var plan = new SyncPlan();

        var localMap = localFiles.ToDictionary(f => f.RelativePath, StringComparer.Ordinal);
        var remoteMap = remoteFiles.ToDictionary(f => f.RelativePath, StringComparer.Ordinal);

        var allPaths = new SortedSet<string>(StringComparer.Ordinal);
        allPaths.UnionWith(localMap.Keys);
        allPaths.UnionWith(remoteMap.Keys);

        foreach (var path in allPaths)
        {
            var hasLocal = localMap.TryGetValue(path, out var local);
            var hasRemote = remoteMap.TryGetValue(path, out var remote);
            metadata.Files.TryGetValue(path, out var record);

            if (hasLocal && !hasRemote)
            {
                plan.Items.Add(MakeItem(path, SyncItemState.LocalOnly, local, null, "only on this computer"));
                continue;
            }

            if (!hasLocal && hasRemote)
            {
                plan.Items.Add(MakeItem(path, SyncItemState.RemoteOnly, null, remote, "only in the cloud"));
                continue;
            }

            // Both sides present.
            if (AreEqual(local!, remote!))
            {
                plan.Items.Add(MakeItem(path, SyncItemState.Unchanged, local, remote, "identical"));
                continue;
            }

            var localChanged = record is null || !MatchesLocal(local!, record);
            var remoteChanged = record is null || !MatchesRemote(remote!, record);

            SyncItemState state;
            string reason;

            if (record is not null)
            {
                if (localChanged && remoteChanged)
                {
                    (state, reason) = (SyncItemState.Conflict, "changed on both sides since the last sync");
                }
                else if (localChanged)
                {
                    (state, reason) = (SyncItemState.Upload, "changed locally");
                }
                else
                {
                    (state, reason) = (SyncItemState.Download, "changed in the cloud");
                }
            }
            else
            {
                // No common baseline: decide purely on timestamps, but only when they differ clearly.
                var diff = local!.LastModifiedUtc - remote!.ModifiedUtc;
                if (diff > TimeTolerance)
                {
                    (state, reason) = (SyncItemState.Upload, "local copy is newer");
                }
                else if (diff < -TimeTolerance)
                {
                    (state, reason) = (SyncItemState.Download, "cloud copy is newer");
                }
                else
                {
                    (state, reason) = (SyncItemState.Conflict, "different content with near-equal timestamps");
                }
            }

            plan.Items.Add(MakeItem(path, state, local, remote, reason));
        }

        return plan;
    }

    private static SyncPlanItem MakeItem(string path, SyncItemState state, LocalSavegameFile? local, CloudFile? remote, string reason) => new()
    {
        RelativePath = path,
        State = state,
        Reason = reason,
        LocalAbsolutePath = local?.AbsolutePath,
        LocalSize = local?.Size ?? 0,
        LocalModifiedUtc = local?.LastModifiedUtc,
        RemoteId = remote?.Id,
        RemoteSize = remote?.Size ?? 0,
        RemoteModifiedUtc = remote?.ModifiedUtc,
    };

    private bool AreEqual(LocalSavegameFile local, CloudFile remote)
    {
        if (local.Size != remote.Size)
        {
            return false;
        }

        // When the provider reports an MD5, compare content directly — clock skew independent.
        if (!string.IsNullOrEmpty(remote.Md5))
        {
            var localHash = TryComputeMd5(local.AbsolutePath);
            if (localHash is not null)
            {
                return string.Equals(localHash, remote.Md5, StringComparison.OrdinalIgnoreCase);
            }
        }

        // Fall back to size + near-equal timestamp.
        return Math.Abs((local.LastModifiedUtc - remote.ModifiedUtc).TotalSeconds) <= TimeTolerance.TotalSeconds;
    }

    private static bool MatchesLocal(LocalSavegameFile local, FileSyncRecord record) =>
        local.Size == record.Size &&
        Math.Abs((local.LastModifiedUtc - record.LocalModifiedUtc).TotalSeconds) <= TimeTolerance.TotalSeconds;

    private static bool MatchesRemote(CloudFile remote, FileSyncRecord record) =>
        Math.Abs((remote.ModifiedUtc - record.RemoteModifiedUtc).TotalSeconds) <= TimeTolerance.TotalSeconds &&
        (record.DriveFileId is null || string.Equals(remote.Id, record.DriveFileId, StringComparison.Ordinal));

    private static void ApplyConflictDecision(SyncPlan plan, ConflictDecision decision)
    {
        foreach (var item in plan.Items.Where(i => i.State == SyncItemState.Conflict))
        {
            if (!decision.PerFile.TryGetValue(item.RelativePath, out var resolution))
            {
                continue;
            }

            item.State = resolution switch
            {
                ConflictResolution.UseLocal => SyncItemState.Upload,
                ConflictResolution.UseCloud => SyncItemState.Download,
                _ => SyncItemState.Conflict, // Skip: leave untouched.
            };
        }
    }

    // ---- execution ------------------------------------------------------------------------

    private async Task<SyncSummary> ExecuteAsync(
        Game game,
        string folderId,
        SyncPlan plan,
        GameSyncMetadata metadata,
        CancellationToken cancellationToken)
    {
        var summary = new SyncSummary();

        foreach (var item in plan.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                switch (item.State)
                {
                    case SyncItemState.Unchanged:
                        summary.Unchanged++;
                        RecordFromItem(metadata, item);
                        break;

                    case SyncItemState.Upload:
                    case SyncItemState.LocalOnly:
                        await UploadAsync(folderId, item, metadata, cancellationToken).ConfigureAwait(false);
                        summary.Uploaded++;
                        break;

                    case SyncItemState.Download:
                    case SyncItemState.RemoteOnly:
                        await DownloadAsync(game, item, metadata, cancellationToken).ConfigureAwait(false);
                        summary.Downloaded++;
                        break;

                    case SyncItemState.Conflict:
                        // Unresolved / skipped conflict: never overwrite either side.
                        summary.Skipped++;
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is CloudStorageException or IOException or HttpRequestException or UnauthorizedAccessException)
            {
                // A single file failure must not corrupt other files or the local savegame.
                _logger.LogWarning(ex, "Sync of '{Path}' failed; local copy left unchanged", item.RelativePath);
                summary.Failed++;
            }
        }

        if (plan.HasConflicts && plan.Items.Any(i => i.State == SyncItemState.Conflict))
        {
            summary.Conflicts = plan.Items.Count(i => i.State == SyncItemState.Conflict);
        }

        return summary;
    }

    private async Task UploadAsync(string folderId, SyncPlanItem item, GameSyncMetadata metadata, CancellationToken cancellationToken)
    {
        if (item.LocalAbsolutePath is null || !File.Exists(item.LocalAbsolutePath))
        {
            return;
        }

        var localInfo = new FileInfo(item.LocalAbsolutePath);
        var modifiedUtc = new DateTimeOffset(localInfo.LastWriteTimeUtc, TimeSpan.Zero);

        await using var stream = new FileStream(item.LocalAbsolutePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var uploaded = await _storage
            .UploadAsync(folderId, item.RelativePath, stream, modifiedUtc, item.RemoteId, cancellationToken)
            .ConfigureAwait(false);

        metadata.Files[item.RelativePath] = new FileSyncRecord
        {
            RelativePath = item.RelativePath,
            LocalModifiedUtc = modifiedUtc,
            RemoteModifiedUtc = uploaded.ModifiedUtc,
            Size = localInfo.Length,
            Hash = TryComputeMd5(item.LocalAbsolutePath),
            DriveFileId = uploaded.Id,
        };
    }

    private async Task DownloadAsync(Game game, SyncPlanItem item, GameSyncMetadata metadata, CancellationToken cancellationToken)
    {
        if (item.RemoteId is null)
        {
            return;
        }

        // Anchor downloads at the same base the scanner used (the launch file's folder), so a
        // round trip always restores a file to where the game actually looks for it.
        var baseDirectory = SavegameBaseDirectory.Resolve(game);
        var targetPath = Path.GetFullPath(Path.Combine(baseDirectory, item.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!PathHelper.IsWithin(baseDirectory, targetPath))
        {
            _logger.LogWarning("Refusing to write downloaded savegame outside the game directory: {Path}", targetPath);
            return;
        }

        var targetDir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(targetDir);

        // Download to a temp file first; the real file is only replaced once the copy is complete.
        Directory.CreateDirectory(_paths.TempDirectory);
        var tempFile = Path.Combine(_paths.TempDirectory, $"dl-{Guid.NewGuid():N}.tmp");

        try
        {
            await using (var temp = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await _storage.DownloadAsync(item.RemoteId, temp, cancellationToken).ConfigureAwait(false);
            }

            // Back up an existing local copy before overwriting (Cloud -> Local).
            if (File.Exists(targetPath))
            {
                BackupLocal(game.Id, item.RelativePath, targetPath);
            }

            // Atomic replace.
            File.Move(tempFile, targetPath, overwrite: true);

            if (item.RemoteModifiedUtc.HasValue)
            {
                try
                {
                    File.SetLastWriteTimeUtc(targetPath, item.RemoteModifiedUtc.Value.UtcDateTime);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Non-fatal: the content is already in place.
                }
            }

            var info = new FileInfo(targetPath);
            metadata.Files[item.RelativePath] = new FileSyncRecord
            {
                RelativePath = item.RelativePath,
                LocalModifiedUtc = new DateTimeOffset(info.LastWriteTimeUtc, TimeSpan.Zero),
                RemoteModifiedUtc = item.RemoteModifiedUtc ?? DateTimeOffset.UtcNow,
                Size = info.Length,
                Hash = TryComputeMd5(targetPath),
                DriveFileId = item.RemoteId,
            };
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try
                {
                    File.Delete(tempFile);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private void BackupLocal(Guid gameId, string relativePath, string sourcePath)
    {
        try
        {
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            var backupDir = Path.Combine(_paths.SavegameBackupDirectory, gameId.ToString("N"), stamp);
            var backupPath = Path.Combine(backupDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(backupPath)!);
            File.Copy(sourcePath, backupPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not back up local savegame before overwrite: {Path}", sourcePath);
        }
    }

    private static void RecordFromItem(GameSyncMetadata metadata, SyncPlanItem item)
    {
        metadata.Files[item.RelativePath] = new FileSyncRecord
        {
            RelativePath = item.RelativePath,
            LocalModifiedUtc = item.LocalModifiedUtc ?? DateTimeOffset.UtcNow,
            RemoteModifiedUtc = item.RemoteModifiedUtc ?? DateTimeOffset.UtcNow,
            Size = item.LocalSize,
            Hash = item.LocalAbsolutePath is not null ? TryComputeMd5(item.LocalAbsolutePath) : null,
            DriveFileId = item.RemoteId,
        };
    }

    private static SyncSummary Summarise(SyncPlan plan)
    {
        var summary = new SyncSummary();
        foreach (var item in plan.Items)
        {
            switch (item.State)
            {
                case SyncItemState.Unchanged: summary.Unchanged++; break;
                case SyncItemState.Upload or SyncItemState.LocalOnly: summary.Uploaded++; break;
                case SyncItemState.Download or SyncItemState.RemoteOnly: summary.Downloaded++; break;
                case SyncItemState.Conflict: summary.Conflicts++; break;
            }
        }

        return summary;
    }

    private static string? TryComputeMd5(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(stream);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
