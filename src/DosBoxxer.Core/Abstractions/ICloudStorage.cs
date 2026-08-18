using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Cloud;

namespace DosBoxxer.Core.Abstractions;

/// <summary>
/// Abstraction over a remote file store (implemented for Google Drive). Everything the sync
/// engine needs is expressed here so the engine can be unit tested against an in-memory fake and
/// never depends on the Google API directly.
///
/// All operations are relative to a per-game folder returned by <see cref="EnsureGameFolderAsync"/>.
/// The provider is responsible for creating any intermediate sub folders required by a relative
/// path so the local directory structure is mirrored faithfully.
/// </summary>
public interface ICloudStorage
{
    /// <summary>
    /// Resolves the game's cloud folder via the cloud game index (<c>dosboxxer/index.json</c>):
    /// the stable cloud game id is looked up by the normalised settings title, savegame-catalog
    /// title and main-directory folder name of <paramref name="game"/> (or registered as a new
    /// entry on first contact), and the <c>dosboxxer/&lt;cloud-game-id&gt;/</c> folder is created
    /// when missing. A previously cached <paramref name="knownFolderId"/> from a pre-index
    /// installation is adopted into the index.
    /// </summary>
    Task<CloudGameFolder> EnsureGameFolderAsync(Game game, string? knownFolderId, CancellationToken cancellationToken = default);

    /// <summary>Recursively lists all files below <paramref name="gameFolderId"/> with game-relative paths.</summary>
    Task<IReadOnlyList<CloudFile>> ListFilesAsync(string gameFolderId, CancellationToken cancellationToken = default);

    /// <summary>Downloads <paramref name="fileId"/> into <paramref name="destination"/>. Throws on failure.</summary>
    Task DownloadAsync(string fileId, Stream destination, CancellationToken cancellationToken = default);

    /// <summary>
    /// Uploads <paramref name="content"/> to <paramref name="relativePath"/> below the game folder,
    /// creating sub folders as needed. Updates <paramref name="existingFileId"/> in place when
    /// provided, otherwise creates a new file. Returns the resulting remote descriptor.
    /// </summary>
    Task<CloudFile> UploadAsync(
        string gameFolderId,
        string relativePath,
        Stream content,
        DateTimeOffset modifiedUtc,
        string? existingFileId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes (trashes) a remote file. Deleting an already-missing file is not an error.</summary>
    Task DeleteAsync(string fileId, CancellationToken cancellationToken = default);
}
