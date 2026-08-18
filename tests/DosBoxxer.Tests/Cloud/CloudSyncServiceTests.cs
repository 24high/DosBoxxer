using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models.Cloud;
using DosBoxxer.Core.Models.Savegame;
using Xunit;

namespace DosBoxxer.Tests.Cloud;

/// <summary>
/// Scenario coverage for the central sync engine (specification §19). Google Drive is mocked via
/// <see cref="InMemoryCloudStorage"/>; the local file system and metadata store are real (temp).
/// </summary>
public sealed class CloudSyncServiceTests
{
    private static readonly DateTimeOffset Base = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // (1) File exists only locally -> uploaded.
    [Fact]
    public async Task LocalOnlyFile_IsUploaded()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "hero", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, outcome.Status);
        Assert.Equal(1, outcome.Summary.Uploaded);
        Assert.Equal("hero", h.RemoteContent(game, "SAVE.SAV"));
    }

    // (2) File exists only in the cloud -> downloaded.
    [Fact]
    public async Task RemoteOnlyFile_IsDownloaded()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        await h.SeedRemoteAsync(game, "SAVE.SAV", "cloud-hero", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, outcome.Status);
        Assert.Equal(1, outcome.Summary.Downloaded);
        Assert.Equal("cloud-hero", h.ReadLocal("SAVE.SAV"));
    }

    // (3) Identical files -> nothing to do.
    [Fact]
    public async Task IdenticalFiles_AreUnchanged()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "same", Base);
        await h.SeedRemoteAsync(game, "SAVE.SAV", "same", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, outcome.Status);
        Assert.Equal(1, outcome.Summary.Unchanged);
        Assert.Equal(0, outcome.Summary.Uploaded);
        Assert.Equal(0, outcome.Summary.Downloaded);
    }

    // (4) Local newer -> uploaded.
    [Fact]
    public async Task LocalNewer_IsUploaded()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "new-local", Base.AddMinutes(10));
        await h.SeedRemoteAsync(game, "SAVE.SAV", "old-cloud", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(1, outcome.Summary.Uploaded);
        Assert.Equal("new-local", h.RemoteContent(game, "SAVE.SAV"));
    }

    // (5) Remote newer -> downloaded.
    [Fact]
    public async Task RemoteNewer_IsDownloaded()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "old-local", Base);
        await h.SeedRemoteAsync(game, "SAVE.SAV", "new-cloud", Base.AddMinutes(10));
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(1, outcome.Summary.Downloaded);
        Assert.Equal("new-cloud", h.ReadLocal("SAVE.SAV"));
    }

    // (6) Equal timestamp but different content -> conflict.
    [Fact]
    public async Task EqualTimestampDifferentContent_IsConflict()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "local-xxxx", Base);
        await h.SeedRemoteAsync(game, "SAVE.SAV", "cloud-yyyy", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Conflict, outcome.Status);
        Assert.Single(outcome.UnresolvedConflicts);
        // Nothing changed on either side.
        Assert.Equal("local-xxxx", h.ReadLocal("SAVE.SAV"));
    }

    // (7) Both sides changed since the last common state -> conflict.
    [Fact]
    public async Task BothChangedSinceBaseline_IsConflict()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "start", Base);
        var service = h.CreateService(game);

        // First sync establishes the common baseline (local uploaded).
        await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        // Now change BOTH sides to different content with clearly different times.
        h.WriteLocal("SAVE.SAV", "local-change", Base.AddMinutes(30));
        await h.SeedRemoteAsync(game, "SAVE.SAV", "cloud-change", Base.AddMinutes(20));

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual, new FixedConflictResolver(null));

        Assert.Equal(SyncStatus.Conflict, outcome.Status);
        Assert.Contains(outcome.UnresolvedConflicts, c => c.RelativePath == "SAVE.SAV");
    }

    // (8) Conflict, user chooses local -> uploaded.
    [Fact]
    public async Task ConflictResolvedToLocal_UploadsLocal()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "local-wins", Base);
        await h.SeedRemoteAsync(game, "SAVE.SAV", "cloud-loses", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual, new FixedConflictResolver(ConflictResolution.UseLocal));

        Assert.Equal(SyncStatus.Success, outcome.Status);
        Assert.Equal("local-wins", h.RemoteContent(game, "SAVE.SAV"));
        Assert.Equal("local-wins", h.ReadLocal("SAVE.SAV"));
    }

    // (9) Conflict, user chooses cloud -> downloaded.
    [Fact]
    public async Task ConflictResolvedToCloud_DownloadsCloud()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "local-loses", Base);
        await h.SeedRemoteAsync(game, "SAVE.SAV", "cloud-wins", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual, new FixedConflictResolver(ConflictResolution.UseCloud));

        Assert.Equal(SyncStatus.Success, outcome.Status);
        Assert.Equal("cloud-wins", h.ReadLocal("SAVE.SAV"));
    }

    // (10) Conflict, user cancels -> nothing changes.
    [Fact]
    public async Task ConflictCancelled_LeavesEverythingUntouched()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "local", Base);
        await h.SeedRemoteAsync(game, "SAVE.SAV", "cloud", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual, new FixedConflictResolver(null));

        Assert.Equal(SyncStatus.Conflict, outcome.Status);
        Assert.Equal("local", h.ReadLocal("SAVE.SAV"));
        Assert.Equal("cloud", h.RemoteContent(game, "SAVE.SAV"));
    }

    // (11) Upload fails -> reported, local savegame intact.
    [Fact]
    public async Task UploadFailure_KeepsLocalIntact()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "precious", Base);
        h.Storage.FailUploadWith = new CloudStorageException(SyncErrorKind.UploadFailed);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Failed, outcome.Status);
        Assert.True(outcome.Summary.Failed >= 1);
        Assert.Equal("precious", h.ReadLocal("SAVE.SAV")); // never lost
    }

    // (12) Download fails -> reported, local savegame intact.
    [Fact]
    public async Task DownloadFailure_KeepsLocalIntact()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "local-keep", Base);
        h.Storage.Seed(h.Storage.FolderId(game.Id), "SAVE.SAV", "cloud-newer", Base.AddMinutes(10));
        h.Storage.FailDownloadWith = new CloudStorageException(SyncErrorKind.DownloadFailed);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Failed, outcome.Status);
        Assert.Equal("local-keep", h.ReadLocal("SAVE.SAV")); // untouched because temp download failed
    }

    // (13) Access token expired but refresh works -> sync still succeeds.
    [Fact]
    public async Task ExpiredAccessTokenThatRefreshes_StillSyncs()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "data", Base);
        h.Auth.AccessToken = "refreshed-token"; // stand-in for a successful silent refresh
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, outcome.Status);
    }

    // (14) Network drops -> failure, local intact.
    [Fact]
    public async Task NetworkFailure_IsReportedAndLocalKept()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "local", Base);
        h.Storage.FailListWith = new CloudStorageException(SyncErrorKind.Network);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Failed, outcome.Status);
        Assert.Equal(SyncErrorKind.Network, outcome.Error);
        Assert.Equal("local", h.ReadLocal("SAVE.SAV"));
    }

    // (15) Several savegame files at once.
    [Fact]
    public async Task MultipleFiles_AreAllSynced()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("*.SAV", SavegameEntryKind.Glob));
        h.WriteLocal("A.SAV", "a", Base);
        h.WriteLocal("B.SAV", "b", Base);
        h.WriteLocal("C.SAV", "c", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(3, outcome.Summary.Uploaded);
        Assert.Equal(3, h.Storage.Snapshot(h.Storage.FolderId(game.Id)).Count);
    }

    // (16) Sub directories keep their relative structure.
    [Fact]
    public async Task Subdirectories_PreserveStructure()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE", SavegameEntryKind.Path));
        h.WriteLocal("SAVE/slot1.dat", "1", Base);
        h.WriteLocal("SAVE/deep/slot2.dat", "2", Base);
        var service = h.CreateService(game);

        await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        var remote = h.Storage.Snapshot(h.Storage.FolderId(game.Id));
        Assert.Contains("SAVE/slot1.dat", remote.Keys);
        Assert.Contains("SAVE/deep/slot2.dat", remote.Keys);
    }

    // (17) Game added with cloud configured -> sync runs.
    [Fact]
    public async Task GameAdded_WithCloud_TriggersSync()
    {
        using var h = new CloudSyncTestHarness(cloudConfigured: true);
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "fresh", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.GameAdded);

        Assert.Equal(SyncStatus.Success, outcome.Status);
        Assert.Equal(1, outcome.Summary.Uploaded);
    }

    // (18) Game added without cloud -> no sync.
    [Fact]
    public async Task GameAdded_WithoutCloud_IsSkipped()
    {
        using var h = new CloudSyncTestHarness(cloudConfigured: false);
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "fresh", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.GameAdded);

        Assert.Equal(SyncStatus.Skipped, outcome.Status);
        Assert.Empty(h.Storage.Snapshot(h.Storage.FolderId(game.Id)));
    }

    // (21) Pre-launch pulls the newer cloud save.
    [Fact]
    public async Task PreLaunch_DownloadsNewerCloudSave()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "old", Base);
        h.Storage.Seed(h.Storage.FolderId(game.Id), "SAVE.SAV", "newer-from-other-pc", Base.AddHours(1));
        var service = h.CreateService(game);

        await service.SyncGameAsync(game.Id, SyncTrigger.PreLaunch);

        Assert.Equal("newer-from-other-pc", h.ReadLocal("SAVE.SAV"));
    }

    // (22) Pre-launch does NOT push an older cloud save over a newer local one.
    [Fact]
    public async Task PreLaunch_DoesNotOverwriteNewerLocalWithOlderCloud()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "newer-local", Base.AddHours(1));
        h.Storage.Seed(h.Storage.FolderId(game.Id), "SAVE.SAV", "older-cloud", Base);
        var service = h.CreateService(game);

        await service.SyncGameAsync(game.Id, SyncTrigger.PreLaunch);

        Assert.Equal("newer-local", h.ReadLocal("SAVE.SAV"));
        Assert.Equal("newer-local", System.Text.Encoding.UTF8.GetString(h.Storage.Snapshot(h.Storage.FolderId(game.Id))["SAVE.SAV"].Content));
    }

    // (26) Post-exit uploads the freshly played local save.
    [Fact]
    public async Task PostExit_UploadsNewerLocalSave()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "progress", Base.AddHours(2));
        h.Storage.Seed(h.Storage.FolderId(game.Id), "SAVE.SAV", "before-play", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.PostExit);

        Assert.Equal(1, outcome.Summary.Uploaded);
        Assert.Equal("progress", System.Text.Encoding.UTF8.GetString(h.Storage.Snapshot(h.Storage.FolderId(game.Id))["SAVE.SAV"].Content));
    }

    // (27) Post-exit upload failure never harms the local save.
    [Fact]
    public async Task PostExit_UploadFailure_NeverDamagesLocal()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "just-played", Base.AddHours(2));
        h.Storage.FailUploadWith = new CloudStorageException(SyncErrorKind.UploadFailed);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.PostExit);

        Assert.Equal(SyncStatus.Failed, outcome.Status);
        Assert.Equal("just-played", h.ReadLocal("SAVE.SAV"));
    }

    // (28) Game without savegame configuration -> no auto-sync.
    [Fact]
    public async Task GameWithoutSavegameConfig_IsSkipped()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(configured: false);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.PreLaunch);

        Assert.Equal(SyncStatus.Skipped, outcome.Status);
    }

    // (29) Authorization revoked -> clean error state.
    [Fact]
    public async Task RevokedAuthorization_ProducesCleanError()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "local", Base);
        h.Storage.FailEnsureFolderWith = new CloudStorageException(SyncErrorKind.AuthRevoked);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Failed, outcome.Status);
        Assert.Equal(SyncErrorKind.AuthRevoked, outcome.Error);
        Assert.Equal("local", h.ReadLocal("SAVE.SAV"));
    }

    // (28b) Not authenticated -> skipped silently, no error nagging.
    [Fact]
    public async Task NotAuthenticated_IsSkipped()
    {
        using var h = new CloudSyncTestHarness();
        h.Auth.IsAuthenticated = false;
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "local", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.PreLaunch);

        Assert.Equal(SyncStatus.Skipped, outcome.Status);
    }

    // (30/31) Two concurrent triggers -> the storage never sees two operations at once.
    [Fact]
    public async Task ConcurrentTriggers_NeverRunInParallelForTheSameGame()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("*.SAV", SavegameEntryKind.Glob));
        for (var i = 0; i < 5; i++)
        {
            h.WriteLocal($"S{i}.SAV", $"v{i}", Base);
        }

        h.Storage.OperationDelay = TimeSpan.FromMilliseconds(15);
        var service = h.CreateService(game);

        var a = service.SyncGameAsync(game.Id, SyncTrigger.Manual);
        var b = service.SyncGameAsync(game.Id, SyncTrigger.PreLaunch);
        await Task.WhenAll(a, b);

        Assert.Equal(1, h.Storage.MaxConcurrentOps);
        Assert.True((await a).Success);
        Assert.True((await b).Success);
    }

    // Trigger must not dictate direction: post-exit still downloads when the cloud is genuinely newer.
    [Fact]
    public async Task Trigger_DoesNotOverrideComparison()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "older-local", Base);
        h.Storage.Seed(h.Storage.FolderId(game.Id), "SAVE.SAV", "newer-cloud", Base.AddHours(1));
        var service = h.CreateService(game);

        // Even though the trigger is POST_EXIT (normally an upload), the newer cloud copy wins.
        await service.SyncGameAsync(game.Id, SyncTrigger.PostExit);

        Assert.Equal("newer-cloud", h.ReadLocal("SAVE.SAV"));
    }

    // Launch file in a sub folder: savegames are searched relative to that folder (the DOS
    // working directory), not the game root.
    [Fact]
    public async Task LaunchFileInSubFolder_UploadsFromTheLaunchFolder()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("*.SAV", SavegameEntryKind.Glob));
        game.LaunchFile = Path.Combine(h.GameDirectory, "DOS", "GAME.EXE");
        h.WriteLocal("DOS/SAVE.SAV", "fresh", Base);
        h.WriteLocal("ROOT.SAV", "decoy in the root", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, outcome.Status);
        Assert.Equal(1, outcome.Summary.Uploaded);
        var uploaded = h.Storage.Snapshot(h.Storage.FolderId(game.Id));
        Assert.True(uploaded.ContainsKey("SAVE.SAV"));
        Assert.False(uploaded.ContainsKey("ROOT.SAV"));
        Assert.False(uploaded.ContainsKey("DOS/SAVE.SAV"));
    }

    // The reverse direction: a download lands in the launch folder, not in the game root.
    [Fact]
    public async Task LaunchFileInSubFolder_DownloadsIntoTheLaunchFolder()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("*.SAV", SavegameEntryKind.Glob));
        game.LaunchFile = Path.Combine(h.GameDirectory, "DOS", "GAME.EXE");
        h.Storage.Seed(h.Storage.FolderId(game.Id), "SAVE.SAV", "cloud-hero", Base);
        var service = h.CreateService(game);

        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, outcome.Status);
        Assert.Equal(1, outcome.Summary.Downloaded);
        Assert.Equal("cloud-hero", h.ReadLocal("DOS/SAVE.SAV"));
        Assert.False(h.LocalExists("SAVE.SAV"));
    }

    // The cloud game id is a plain unique id (e.g. "00001"), not "<name>-<suffix>".
    [Fact]
    public async Task CloudGameId_IsAPlainUniqueId()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        h.WriteLocal("SAVE.SAV", "hero", Base);
        var service = h.CreateService(game);

        await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        var entry = Assert.Single(h.Storage.IndexEntries);
        Assert.Equal("00001", entry.Id);
        Assert.DoesNotContain("-", entry.Id);
    }

    // The index entry links the normalised settings title, catalog title and main folder name.
    [Fact]
    public async Task IndexEntry_ContainsSettingsCatalogAndMainFolderKeys()
    {
        using var h = new CloudSyncTestHarness();
        var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
        game.SavegameConfig.MatchedCatalogTitle = "The Secret of Monkey Island";
        game.GameDirectory = Path.Combine(h.GameDirectory, "Monkey Island");
        game.Title = "Monkey Island (Special Edition)";
        h.WriteLocal("SAVE.SAV", "hero", Base);
        var service = h.CreateService(game);

        await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        var entry = Assert.Single(h.Storage.IndexEntries);
        Assert.Equal("monkeyislandspecialedition", entry.SettingsName);
        Assert.Equal("thesecretofmonkeyisland", entry.CatalogName);
        Assert.Equal("monkeyisland", entry.MainFolderName);
    }

    // After a reinstallation (fresh metadata store, same titles/folder) the same id is recovered.
    [Fact]
    public async Task Reinstall_RecoversTheSameCloudGameId()
    {
        var sharedStorage = new InMemoryCloudStorage();

        string firstFolderId;
        using (var h = new CloudSyncTestHarness(storage: sharedStorage))
        {
            var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
            game.SavegameConfig.MatchedCatalogTitle = "DreamWeb";
            game.GameDirectory = Path.Combine(h.GameDirectory, "DreamWeb");
            game.Title = "DreamWeb";
            h.WriteLocal("SAVE.SAV", "hero", Base);
            var service = h.CreateService(game);

            await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

            firstFolderId = h.Storage.TryGetFolderId(game)!;
            Assert.NotNull(firstFolderId);
        }

        // "Reinstall": a brand-new harness (fresh temp dirs and metadata store), same cloud storage,
        // same game titles and main folder name.
        using (var h = new CloudSyncTestHarness(storage: sharedStorage))
        {
            var game = h.NewGame(entries: new SavegameEntry("SAVE.SAV", SavegameEntryKind.Path));
            game.SavegameConfig.MatchedCatalogTitle = "DreamWeb";
            game.GameDirectory = Path.Combine(h.GameDirectory, "DreamWeb");
            game.Title = "DreamWeb";
            h.WriteLocal("SAVE.SAV", "hero2", Base);
            var service = h.CreateService(game);

            var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

            Assert.Equal(SyncStatus.Success, outcome.Status);
            Assert.Equal(h.Storage.TryGetFolderId(game), firstFolderId);
            Assert.Single(h.Storage.IndexEntries);
        }
    }
}
