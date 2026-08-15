using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using DosBoxxer.Core.Helpers;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.App.Services;

public interface IImageLoader
{
    /// <summary>
    /// Loads an image scaled down to <paramref name="decodeWidth"/> pixels. Decoding at the
    /// target size keeps memory usage proportional to what is actually shown, which is what
    /// makes a library of several thousand covers viable.
    /// </summary>
    Task<Bitmap?> LoadThumbnailAsync(string? path, int decodeWidth, CancellationToken cancellationToken = default);

    /// <summary>Loads an image at full resolution (used by the lightbox).</summary>
    Task<Bitmap?> LoadFullAsync(string? path, CancellationToken cancellationToken = default);

    /// <summary>Drops every cached bitmap, e.g. after the media cache was cleared.</summary>
    void Clear();
}

/// <summary>
/// Bitmap cache with a bounded number of entries and least-recently-used eviction.
/// Decoding happens on the thread pool; only the finished <see cref="Bitmap"/> is handed back
/// to the UI thread.
/// </summary>
public sealed class ImageLoader : IImageLoader, IDisposable
{
    private const int MaxCachedBitmaps = 400;

    /// <summary>Limits parallel decoding so opening a large library does not saturate the CPU.</summary>
    private readonly SemaphoreSlim _decodeLimit = new(4, 4);

    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _index = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly object _cacheLock = new();
    private readonly ILogger<ImageLoader> _logger;

    public ImageLoader(ILogger<ImageLoader> logger) => _logger = logger;

    public Task<Bitmap?> LoadThumbnailAsync(string? path, int decodeWidth, CancellationToken cancellationToken = default) =>
        LoadAsync(path, decodeWidth, cancellationToken);

    public Task<Bitmap?> LoadFullAsync(string? path, CancellationToken cancellationToken = default) =>
        LoadAsync(path, 0, cancellationToken);

    private async Task<Bitmap?> LoadAsync(string? path, int decodeWidth, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !PathHelper.FileExistsSafe(path))
        {
            return null;
        }

        var key = decodeWidth > 0 ? path + "|" + decodeWidth : path;

        if (TryGetCached(key, out var cached))
        {
            return cached;
        }

        await _decodeLimit.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Another caller may have finished the same image while we waited.
            if (TryGetCached(key, out cached))
            {
                return cached;
            }

            var bitmap = await Task.Run(
                () =>
                {
                    try
                    {
                        using var stream = File.OpenRead(path);
                        return decodeWidth > 0
                            ? Bitmap.DecodeToWidth(stream, decodeWidth, BitmapInterpolationMode.HighQuality)
                            : new Bitmap(stream);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                    {
                        _logger.LogWarning("Image could not be decoded ({Type})", ex.GetType().Name);
                        return null;
                    }
                },
                cancellationToken).ConfigureAwait(false);

            if (bitmap is not null)
            {
                Store(key, bitmap);
            }

            return bitmap;
        }
        finally
        {
            _decodeLimit.Release();
        }
    }

    private bool TryGetCached(string key, out Bitmap? bitmap)
    {
        lock (_cacheLock)
        {
            if (_index.TryGetValue(key, out var node))
            {
                _lru.Remove(node);
                _lru.AddFirst(node);
                bitmap = node.Value.Bitmap;
                return true;
            }
        }

        bitmap = null;
        return false;
    }

    /// <summary>
    /// Evicted bitmaps are dropped from the cache but deliberately NOT disposed: the same
    /// instance may still be assigned to a visible <c>Image</c>, and disposing it underneath the
    /// renderer would tear down the native surface while it is being drawn. Releasing the last
    /// reference and letting the GC reclaim it is the only safe option for a cache whose entries
    /// are handed straight to the UI. Memory stays bounded because entries are capped and
    /// thumbnails are decoded at display size.
    /// </summary>
    private void Store(string key, Bitmap bitmap)
    {
        lock (_cacheLock)
        {
            if (_index.ContainsKey(key))
            {
                return;
            }

            var node = _lru.AddFirst(new CacheEntry(key, bitmap));
            _index[key] = node;

            if (_index.Count > MaxCachedBitmaps)
            {
                var last = _lru.Last;
                if (last is not null)
                {
                    _lru.RemoveLast();
                    _index.Remove(last.Value.Key);
                }
            }
        }
    }

    public void Clear()
    {
        lock (_cacheLock)
        {
            _index.Clear();
            _lru.Clear();
        }
    }

    public void Dispose()
    {
        Clear();
        _decodeLimit.Dispose();
    }

    private readonly record struct CacheEntry(string Key, Bitmap Bitmap);
}
