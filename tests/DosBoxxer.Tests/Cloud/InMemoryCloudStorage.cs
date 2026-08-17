using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models.Cloud;

namespace DosBoxxer.Tests.Cloud;

/// <summary>
/// In-memory <see cref="ICloudStorage"/> fake. It mirrors the Drive semantics the sync engine
/// relies on (per-game folder, recursive listing with relative paths, md5, modified time) and can
/// inject faults and observe concurrency, so the whole engine is testable without any network.
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
    private int _nextId;
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

    public string FolderId(Guid gameId) => "gf-" + gameId.ToString("N");

    public async Task<string> EnsureGameFolderAsync(Guid gameId, string? knownFolderId, CancellationToken cancellationToken = default)
    {
        using var _ = await EnterAsync(cancellationToken).ConfigureAwait(false);
        if (FailEnsureFolderWith is not null)
        {
            throw FailEnsureFolderWith;
        }

        var id = FolderId(gameId);
        _folders.GetOrAdd(id, _ => new Dictionary<string, Entry>(StringComparer.Ordinal));
        return id;
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
