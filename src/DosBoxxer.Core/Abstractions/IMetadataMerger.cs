using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Metadata;

namespace DosBoxxer.Core.Abstractions;

/// <summary>
/// Applies provider metadata onto an existing <see cref="Game"/> while honouring manual edits.
/// Media downloads are handled separately; the merger only touches textual fields and genres.
/// </summary>
public interface IMetadataMerger
{
    MetadataMergeResult Merge(Game target, GameMetadata metadata, MetadataMergeOptions options);
}
