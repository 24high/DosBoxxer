namespace DosBoxxer.Core.Models;

public sealed class Screenshot
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid GameId { get; set; }

    /// <summary>Absolute path of the locally cached image file.</summary>
    public string LocalPath { get; set; } = string.Empty;

    /// <summary>Original remote URL. Stored for diagnostics only; never re-downloaded automatically.</summary>
    public string? RemoteUrl { get; set; }

    public int SortOrder { get; set; }

    public Screenshot Clone() => new()
    {
        Id = Id,
        GameId = GameId,
        LocalPath = LocalPath,
        RemoteUrl = RemoteUrl,
        SortOrder = SortOrder,
    };
}
