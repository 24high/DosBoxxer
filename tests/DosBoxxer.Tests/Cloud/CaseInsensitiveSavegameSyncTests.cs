using System.IO;
using System.Threading.Tasks;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Cloud;
using DosBoxxer.Core.Models.Savegame;
using Xunit;

namespace DosBoxxer.Tests.Cloud;

/// <summary>
/// Reproduction for the reported issue: files matching the savegame pattern that sit next to the
/// launch executable must be uploaded when they are not yet present in the cloud.
/// </summary>
public sealed class CaseInsensitiveSavegameSyncTests
{
    [Fact]
    public async Task ExeInSubdirectory_NewFileMatchingGlob_IsUploaded()
    {
        using var harness = new CloudSyncTestHarness();

        // EXE lives in a sub directory; the savegame is written next to it (DOS working dir).
        var exeDir = Path.Combine(harness.GameDirectory, "DOS");
        Directory.CreateDirectory(exeDir);
        File.WriteAllText(Path.Combine(exeDir, "GAME.EXE"), "exe");
        File.WriteAllText(Path.Combine(exeDir, "SAVE1.SAV"), "save-content");

        var config = new SavegameConfig { IsConfigured = true };
        config.Entries.Add(new SavegameEntry("*.SAV", SavegameEntryKind.Glob));

        var game = new Game
        {
            Title = "Test",
            GameDirectory = harness.GameDirectory,
            LaunchFile = Path.Combine(exeDir, "GAME.EXE"),
            SavegameConfig = config,
        };
        config.GameId = game.Id;

        var service = harness.CreateService(game);
        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, outcome.Status);
        Assert.Equal(1, outcome.Summary.Uploaded);
        Assert.Single(harness.Storage.Snapshot(harness.Storage.FolderId(game.Id)));
    }

    [Fact]
    public async Task ExeInRoot_NewFileMatchingGlob_IsUploaded()
    {
        using var harness = new CloudSyncTestHarness();
        harness.WriteLocal("SAVE1.SAV", "save-content");

        var game = harness.NewGame(configured: true, new SavegameEntry("*.SAV", SavegameEntryKind.Glob));
        var service = harness.CreateService(game);
        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, outcome.Status);
        Assert.Equal(1, outcome.Summary.Uploaded);
        Assert.Single(harness.Storage.Snapshot(harness.Storage.FolderId(game.Id)));
    }

    [Theory]
    [InlineData("save1.sav", "*.SAV")]
    [InlineData("SAVE1.SAV", "*.sav")]
    [InlineData("Save1.Sav", "*.sav")]
    public async Task CaseVariant_MatchingGlob_IsUploaded(string fileName, string pattern)
    {
        using var harness = new CloudSyncTestHarness();
        harness.WriteLocal(fileName, "save-content");

        var game = harness.NewGame(configured: true, new SavegameEntry(pattern, SavegameEntryKind.Glob));
        var service = harness.CreateService(game);
        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, outcome.Status);
        Assert.Equal(1, outcome.Summary.Uploaded);
    }

    [Theory]
    [InlineData("save/highscores.dat", "SAVE/HIGHSCORES.DAT")]
    [InlineData("SAVE", "save")]
    public async Task CaseVariant_PathEntry_IsUploaded(string pattern, string actual)
    {
        using var harness = new CloudSyncTestHarness();
        harness.WriteLocal(actual, "save-content");

        var game = harness.NewGame(configured: true, new SavegameEntry(pattern, SavegameEntryKind.Path));
        var service = harness.CreateService(game);
        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, outcome.Status);
        Assert.Equal(1, outcome.Summary.Uploaded);
    }

    /// <summary>
    /// The real Dreamweb layout: a menu <c>run.bat</c> in the game root that does
    /// <c>cd DREAMWEB</c> before starting the actual executable, so the savegames land in the
    /// sub folder — while the launch file (and thus the scan base) is the game root. The
    /// catalog pattern <c>DREAMWEB.D0*</c> has no sub directory prefix and must still match.
    /// </summary>
    [Fact]
    public async Task MenuLauncher_SavegamesInSubfolder_AreUploadedWithRelativeStructure()
    {
        using var harness = new CloudSyncTestHarness();
        harness.WriteLocal("run.bat", "cd DREAMWEB\n@DREAMWEB");
        harness.WriteLocal("DREAMWEB/DREAMWEB.EXE", "exe");
        harness.WriteLocal("DREAMWEB/DREAMWEB.D00", "slot-0");
        harness.WriteLocal("DREAMWEB/DREAMWEB.D01", "slot-1");
        harness.WriteLocal("DREAMWEB/DREAMWEB.EXE.CFG", "not a save");

        var config = new SavegameConfig { IsConfigured = true };
        config.Entries.Add(new SavegameEntry("DREAMWEB.D0*", SavegameEntryKind.Glob));

        var game = new Game
        {
            Title = "Dreamweb",
            GameDirectory = harness.GameDirectory,
            LaunchFile = Path.Combine(harness.GameDirectory, "run.bat"),
            SavegameConfig = config,
        };
        config.GameId = game.Id;

        var service = harness.CreateService(game);
        var outcome = await service.SyncGameAsync(game.Id, SyncTrigger.Manual);

        Assert.Equal(SyncStatus.Success, outcome.Status);
        Assert.Equal(2, outcome.Summary.Uploaded);

        var uploaded = harness.Storage.Snapshot(harness.Storage.FolderId(game.Id));
        Assert.True(uploaded.ContainsKey("DREAMWEB/DREAMWEB.D00"));
        Assert.True(uploaded.ContainsKey("DREAMWEB/DREAMWEB.D01"));
    }
}