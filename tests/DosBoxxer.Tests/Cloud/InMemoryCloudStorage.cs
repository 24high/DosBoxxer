using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Cloud;

namespace DosBoxxer.Tests.Cloud;

/// <summary>
/// In-memory <see cref="ICloudStorage"/> fake. It mirrors the Drive semantics the sync engine
/// relies on (per-game folder, recursive listing with relative paths, md5, modified time) and
/// reproduces the cloud game index (<c>dosboxxer/index.json</c>): games are identified by the
/// normalised settings/catalog/main-folder titles and receive a stable, deterministic cloud id
/// (<c>00001</c>, <c>00002</c>, …). The folder id is derived from the game id, so tests can
/// address a game's folder before the first sync. It can inject faults and observe concurrency,
/// so the whole engine is testable without any network.
/// </summary>
public sealed class InMemoryCloudStorage : ICloudStorage
{
    public sealed class Entry
    {
        public required string Id { get; set; }
        public required byte[] Content { get; set; }
        public DateTimeOffset Modified { get; set; }
    }

    private readonly ConcurrentDictionary<string, Dictionary<string, Entry>> _folders = new();
    private readonly CloudGameIndex _index = new();
    private readonly object _indexSync = new();
    private int _nextId;
    private int _nextSuffix;
    private int _activeOps;

    /// <summary>Highest number of operations observed running at the same time.</summary>
    public int MaxConcurrentOps { get; private set; }

    // Fault injection.
    public Exception? FailListWith { get; set; }
    public Exception? FailUploadWith { get; set; }
    public Exception? FailDownloadWith { get; set; }
    public Exception? FailEnsureFolderWith { get; set; }

    /// <summary>Optional delay to widen the window for concurrency assertions.</summary>
    public TimeSpan OperationDelay { get; set; } = TimeSpan.Zero;

    /// <summary>Snapshot of the current index entries (the fake's <c>index.json</c> content).</summary>
    public IReadOnlyList<CloudGameIndexEntry> IndexEntries
    {
        get
        {
            lock (_indexSync)
            {
                return _index.Games.ToList();
            }
        }
    }

    /// <summary>Deterministic folder id for a game, stable before and after the first sync.</summary>
    public string FolderId(Guid gameId) => "gf-" + gameId.ToString("N");

    /// <summary>
    /// Looks up the folder id <paramref name="game"/> currently maps to via the index, or
    /// <c>null</c> when the game has no index entry yet (i.e. never synced). Never creates anything.
    /// </summary>
    public string? TryGetFolderId(Game game)
    {
        lock (_indexSync)
        {
            var entry = _index.Find(SettingsKey(game), CatalogKey(game), MainFolderKey(game));
            return entry?.DriveFolderId;
        }
    }

    public IReadOnlyDictionary<string, Entry> Snapshot(string folderId) =>
        _folders.TryGetValue(folderId, out var files) ? new Dictionary<string, Entry>(files) : new();

    public void Seed(string folderId, string relativePath, string content, DateTimeOffset modified)
    {
        var files = _folders.GetOrAdd(folderId, _ => new Dictionary<string, Entry>(StringComparer.Ordinal));
        files[relativePath] = new Entry
        {
            Id = "seed-" + Interlocked.Increment(ref _nextId),
            Content = System.Text.Encoding.UTF8.GetBytes(content),
            Modified = modified,
        };
    }

    public async Task<CloudGameFolder> EnsureGameFolderAsync(Game game, string? knownFolderId, CancellationToken cancellationToken = default)
    {
        using var _ = await EnterAsync(cancellationToken).ConfigureAwait(false);
        if (FailEnsureFolderWith is not null)
        {
            throw FailEnsureFolderWith;
        }

        lock (_indexSync)
        {
            var entry = _index.Find(SettingsKey(game), CatalogKey(game), MainFolderKey(game));
            if (entry is not null)
            {
                var folderId = entry.DriveFolderId;
                if (folderId is null || !_folders.ContainsKey(folderId))
                {
                    folderId = FolderId(game.Id);
                }

                _folders.GetOrAdd(folderId, _ => new Dictionary<string, Entry>(StringComparer.Ordinal));

                CloudGameIndex.UpdateNames(entry, SettingsKey(game), CatalogKey(game), MainFolderKey(game));
                entry.DriveFolderId = folderId;
                return new CloudGameFolder { FolderId = folderId, CloudGameId = entry.Id };
            }

            var cloudGameId = _index.GenerateId(
                () => Interlocked.Increment(ref _nextSuffix).ToString("D5"));

            string newFolderId;
            if (!string.IsNullOrEmpty(knownFolderId) && _folders.ContainsKey(knownFolderId))
            {
                // Pre-index folder adopted as-is (a rename keeps the folder id on Drive).
                newFolderId = knownFolderId;
            }
            else
            {
                newFolderId = FolderId(game.Id);
            }

            _folders.GetOrAdd(newFolderId, _ => new Dictionary<string, Entry>(StringComparer.Ordinal));

            _index.Games.Add(new CloudGameIndexEntry
            {
                Id = cloudGameId,
                SettingsName = NullIfEmpty(SettingsKey(game)),
                CatalogName = NullIfEmpty(CatalogKey(game)),
                MainFolderName = NullIfEmpty(MainFolderKey(game)),
                DriveFolderId = newFolderId,
            });

            return new CloudGameFolder { FolderId = newFolderId, CloudGameId = cloudGameId };
        }
    }

    public async Task<IReadOnlyList<CloudFile>> ListFilesAsync(string gameFolderId, CancellationToken cancellationToken = default)
    {
        using var _ = await EnterAsync(cancellationToken).ConfigureAwait(false);
        if (FailListWith is not null)
        {
            throw FailListWith;
        }

        if (!_folders.TryGetValue(gameFolderId, out var files))
        {
            return Array.Empty<CloudFile>();
        }

        return files
            .Select(kv => new CloudFile
            {
                Id = kv.Value.Id,
                RelativePath = kv.Key,
                Size = kv.Value.Content.Length,
                ModifiedUtc = kv.Value.Modified,
                Md5 = Md5(kv.Value.Content),
            })
            .ToList();
    }

    public async Task DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken = default)
    {
        using var _ = await EnterAsync(cancellationToken).ConfigureAwait(false);
        if (FailDownloadWith is not null)
        {
            throw FailDownloadWith;
        }

        var entry = _folders.Values.SelectMany(f => f.Values).FirstOrDefault(e => e.Id == fileId)
                    ?? throw new CloudStorageException(SyncErrorKind.DownloadFailed, "not found");

        await destination.WriteAsync(entry.Content, cancellationToken).ConfigureAwait(false);
    }

    public async Task<CloudFile> UploadAsync(
        string gameFolderId,
        string relativePath,
        Stream content,
        DateTimeOffset modifiedUtc,
        string? existingFileId,
        CancellationToken cancellationToken = default)
    {
        using var _ = await EnterAsync(cancellationToken).ConfigureAwait(false);
        if (FailUploadWith is not null)
        {
            throw FailUploadWith;
        }

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var bytes = buffer.ToArray();

        var files = _folders.GetOrAdd(gameFolderId, _ => new Dictionary<string, Entry>(StringComparer.Ordinal));
        var id = existingFileId ?? "up-" + Interlocked.Increment(ref _nextId);
        files[relativePath] = new Entry { Id = id, Content = bytes, Modified = modifiedUtc };

        return new CloudFile
        {
            Id = id,
            RelativePath = relativePath,
            Size = bytes.Length,
            ModifiedUtc = modifiedUtc,
            Md5 = Md5(bytes),
        };
    }

    public async Task DeleteAsync(string fileId, CancellationToken cancellationToken = default)
    {
        using var _ = await EnterAsync(cancellationToken).ConfigureAwait(false);
        foreach (var files in _folders.Values)
        {
            var key = files.FirstOrDefault(kv => kv.Value.Id == fileId).Key;
            if (key is not null)
            {
                files.Remove(key);
            }
        }
    }

    private static string SettingsKey(Game game) => CloudGameKey.Normalize(game.Title);

    private static string CatalogKey(Game game) => CloudGameKey.Normalize(game.SavegameConfig?.MatchedCatalogTitle);

    private static string MainFolderKey(Game game) => CloudGameKey.Normalize(Path.GetFileName(PathHelper.Normalize(game.GameDirectory)));

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private async Task<IDisposable> EnterAsync(CancellationToken cancellationToken)
    {
        var current = Interlocked.Increment(ref _activeOps);
        lock (this)
        {
            if (current > MaxConcurrentOps)
            {
                MaxConcurrentOps = current;
            }
        }

        if (OperationDelay > TimeSpan.Zero)
        {
            await Task.Delay(OperationDelay, cancellationToken).ConfigureAwait(false);
        }

        return new Scope(this);
    }

    private sealed class Scope : IDisposable
    {
        private readonly InMemoryCloudStorage _owner;
        public Scope(InMemoryCloudStorage owner) => _owner = owner;
        public void Dispose() => Interlocked.Decrement(ref _owner._activeOps);
    }

    private static string Md5(byte[] content) =>
        Convert.ToHexString(MD5.HashData(content)).ToLowerInvariant();
}
