using DosBoxxer.Core.Models.Metadata;

namespace DosBoxxer.Core.Abstractions;

public sealed class DownloadedMedia
{
    public required string LocalPath { get; init; }

    public required string SourceUrl { get; init; }
}

/// <summary>
/// Downloads cover art and screenshots into the local cache. Target file names are derived from
/// the game id, media kind and a hash of the URL — never from data supplied by the API, so a
/// hostile response cannot write outside the cache directory.
/// </summary>
public interface IMediaDownloader
{
    Task<DownloadedMedia?> DownloadAsync(
        Guid gameId,
        MetadataMedia media,
        int index,
        CancellationToken cancellationToken = default);

    /// <summary>Copies a user selected local image into the cover cache and returns its new path.</summary>
    Task<string?> ImportLocalCoverAsync(Guid gameId, string sourcePath, CancellationToken cancellationToken = default);

    /// <summary>Copies a user selected local image into the screenshot cache.</summary>
    Task<string?> ImportLocalScreenshotAsync(Guid gameId, string sourcePath, int index, CancellationToken cancellationToken = default);

    /// <summary>Deletes every cached media file that belongs to the given game.</summary>
    Task DeleteMediaAsync(Guid gameId, CancellationToken cancellationToken = default);

    /// <summary>Removes the whole media cache. Only touches directories owned by the launcher.</summary>
    Task ClearCacheAsync(CancellationToken cancellationToken = default);

    Task<long> GetCacheSizeAsync(CancellationToken cancellationToken = default);
}
