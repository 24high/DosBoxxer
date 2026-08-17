using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Infrastructure;
using DosBoxxer.Core.Infrastructure.Cloud;
using DosBoxxer.Core.Infrastructure.Savegame;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Cloud;
using DosBoxxer.Core.Models.Savegame;
using Microsoft.Extensions.Logging.Abstractions;

namespace DosBoxxer.Tests.Cloud;

/// <summary>Configurable OAuth stub — authenticated toggle and access-token behaviour.</summary>
public sealed class StubCloudAuth : ICloudAuthService
{
    public bool IsConfigured { get; set; } = true;
    public bool IsAuthenticated { get; set; } = true;
    public string? AccountEmail => "tester@example.com";
    public string? AccessToken { get; set; } = "test-token";

    public event EventHandler? StateChanged;

    public Task<CloudAuthResult> ConnectAsync(CancellationToken cancellationToken = default)
    {
        IsAuthenticated = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.FromResult(new CloudAuthResult { Status = CloudAuthStatus.Success });
    }

    public Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        IsAuthenticated = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(IsAuthenticated ? AccessToken : null);
}

/// <summary>Repository stub that just returns the single game under test.</summary>
public sealed class StubGameRepository : IGameRepository
{
    private readonly Game? _game;
    public StubGameRepository(Game? game) => _game = game;

    public Task<Game?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_game is not null && _game.Id == id ? _game : null);

    public Task<IReadOnlyList<Game>> GetAllAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task<Game?> FindByLaunchFileAsync(string launchFile, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task AddAsync(Game game, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task UpdateAsync(Game game, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task DeleteAsync(Guid id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task RecordPlaySessionAsync(Guid id, DateTimeOffset startedAt, long durationSeconds, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    public Task SetFavoriteAsync(Guid id, bool isFavorite, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyDictionary<GenreKey, int>> GetGenreCountsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

/// <summary>Minimal settings service whose Google Drive configuration flag can be toggled.</summary>
public sealed class StubSyncSettings : ISettingsService
{
    public AppSettings Current { get; } = new();
    public event EventHandler<AppSettings>? SettingsChanged;

    public StubSyncSettings(bool cloudConfigured)
    {
        if (!cloudConfigured)
        {
            // The OAuth credentials are hard coded defaults; clear them to simulate "not configured".
            Current.GoogleDrive.ClientId = null;
            Current.GoogleDrive.ClientSecret = null;
        }
    }

    public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        SettingsChanged?.Invoke(this, settings);
        return Task.CompletedTask;
    }
    public Task SaveCurrentAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}

/// <summary>Conflict resolver that returns a fixed decision (or cancels).</summary>
public sealed class FixedConflictResolver : IConflictResolver
{
    private readonly ConflictResolution? _resolution;
    public int CallCount { get; private set; }
    public IReadOnlyList<SyncPlanItem>? LastConflicts { get; private set; }

    /// <summary><c>null</c> resolution means "user cancelled".</summary>
    public FixedConflictResolver(ConflictResolution? resolution) => _resolution = resolution;

    public Task<ConflictDecision?> ResolveAsync(IReadOnlyList<SyncPlanItem> conflicts, SyncTrigger trigger, CancellationToken cancellationToken = default)
    {
        CallCount++;
        LastConflicts = conflicts;

        if (_resolution is null)
        {
            return Task.FromResult<ConflictDecision?>(null);
        }

        var map = new Dictionary<string, ConflictResolution>(StringComparer.Ordinal);
        foreach (var conflict in conflicts)
        {
            map[conflict.RelativePath] = _resolution.Value;
        }

        return Task.FromResult<ConflictDecision?>(new ConflictDecision { PerFile = map });
    }
}

/// <summary>Wires a real <see cref="CloudSyncService"/> against the in-memory storage and temp disk.</summary>
public sealed class CloudSyncTestHarness : IDisposable
{
    private readonly TempDirectory _temp;

    public CloudSyncTestHarness(bool cloudConfigured = true)
    {
        _temp = new TempDirectory();
        Paths = new AppPaths(Path.Combine(_temp.Path, "appdata"), Path.Combine(_temp.Path, "program"));
        Paths.EnsureCreated();

        GameDirectory = _temp.CreateSubdirectory("game");
        Storage = new InMemoryCloudStorage();
        Auth = new StubCloudAuth();
        Settings = new StubSyncSettings(cloudConfigured);
        MetadataStore = new SyncMetadataStore(Paths, NullLogger<SyncMetadataStore>.Instance);
        Scanner = new LocalSavegameScanner(NullLogger<LocalSavegameScanner>.Instance);
    }

    public AppPaths Paths { get; }
    public string GameDirectory { get; }
    public InMemoryCloudStorage Storage { get; }
    public StubCloudAuth Auth { get; }
    public StubSyncSettings Settings { get; }
    public SyncMetadataStore MetadataStore { get; }
    public LocalSavegameScanner Scanner { get; }
    public Game Game { get; private set; } = null!;

    public CloudSyncService CreateService(Game game)
    {
        Game = game;
        return new CloudSyncService(
            new StubGameRepository(game),
            Scanner,
            Storage,
            Auth,
            MetadataStore,
            Settings,
            Paths,
            NullLogger<CloudSyncService>.Instance);
    }

    /// <summary>Builds a game rooted in the temp game directory with a savegame configuration.</summary>
    public Game NewGame(bool configured = true, params SavegameEntry[] entries)
    {
        var config = new SavegameConfig { IsConfigured = configured };
        if (entries.Length == 0 && configured)
        {
            config.Entries.Add(new SavegameEntry("*.SAV", SavegameEntryKind.Glob));
        }
        else
        {
            config.Entries.AddRange(entries);
        }

        var game = new Game
        {
            Title = "Test Game",
            GameDirectory = GameDirectory,
            LaunchFile = Path.Combine(GameDirectory, "GAME.EXE"),
            SavegameConfig = config,
        };
        config.GameId = game.Id;
        return game;
    }

    public string WriteLocal(string relativePath, string content, DateTimeOffset? modified = null)
    {
        var path = Path.Combine(GameDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        if (modified.HasValue)
        {
            File.SetLastWriteTimeUtc(path, modified.Value.UtcDateTime);
        }

        return path;
    }

    public string ReadLocal(string relativePath) =>
        File.ReadAllText(Path.Combine(GameDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public bool LocalExists(string relativePath) =>
        File.Exists(Path.Combine(GameDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar)));

    public void Dispose() => _temp.Dispose();
}
