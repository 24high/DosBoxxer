namespace DosBoxxer.Core.Models.Cloud;

/// <summary>
/// Persisted, per-file record of the last successfully synchronised state. It lets the sync
/// engine tell a genuine conflict (both sides changed since the last common state) apart from a
/// normal one-sided change. Missing or stale metadata is treated conservatively.
/// </summary>
public sealed class FileSyncRecord
{
    public required string RelativePath { get; set; }

    public DateTimeOffset LocalModifiedUtc { get; set; }

    public DateTimeOffset RemoteModifiedUtc { get; set; }

    public long Size { get; set; }

    /// <summary>Content hash of the last synced version (used to detect equal content across clocks).</summary>
    public string? Hash { get; set; }

    public string? DriveFileId { get; set; }
}

/// <summary>
/// Per-game cloud sync metadata: the Drive folder id and the last-known common state of every
/// file. Persisted as JSON under the launcher data directory, keyed by the stable game id.
/// </summary>
public sealed class GameSyncMetadata
{
    public Guid GameId { get; set; }

    /// <summary>Google Drive folder id of <c>dosboxxer/&lt;game-id&gt;/</c>, cached to avoid re-lookups.</summary>
    public string? DriveFolderId { get; set; }

    public DateTimeOffset? LastSuccessfulSyncUtc { get; set; }

    public Dictionary<string, FileSyncRecord> Files { get; set; } = new(StringComparer.Ordinal);

    public GameSyncMetadata Clone()
    {
        var clone = new GameSyncMetadata
        {
            GameId = GameId,
            DriveFolderId = DriveFolderId,
            LastSuccessfulSyncUtc = LastSuccessfulSyncUtc,
        };

        foreach (var (key, record) in Files)
        {
            clone.Files[key] = new FileSyncRecord
            {
                RelativePath = record.RelativePath,
                LocalModifiedUtc = record.LocalModifiedUtc,
                RemoteModifiedUtc = record.RemoteModifiedUtc,
                Size = record.Size,
                Hash = record.Hash,
                DriveFileId = record.DriveFileId,
            };
        }

        return clone;
    }
}
