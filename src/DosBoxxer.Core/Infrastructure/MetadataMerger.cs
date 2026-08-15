using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Metadata;

namespace DosBoxxer.Core.Infrastructure;

/// <summary>
/// Merges provider metadata into an existing game.
///
/// Rules:
/// <list type="bullet">
/// <item>Only fields selected in <see cref="MetadataMergeOptions.Fields"/> are considered.</item>
/// <item>A field flagged in <see cref="Game.ManualFields"/> is left untouched unless the caller
/// explicitly asks for <see cref="MetadataMergeOptions.OverwriteManualEdits"/>.</item>
/// <item>An empty provider value never clears existing data unless
/// <see cref="MetadataMergeOptions.AllowClearing"/> is set.</item>
/// </list>
/// </summary>
public sealed class MetadataMerger : IMetadataMerger
{
    public MetadataMergeResult Merge(Game target, GameMetadata metadata, MetadataMergeOptions options)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(metadata);
        ArgumentNullException.ThrowIfNull(options);

        var updated = GameField.None;
        var skipped = GameField.None;

        void Apply(GameField field, Func<bool> hasValue, Action assign)
        {
            if (!options.Fields.HasFlag(field))
            {
                return;
            }

            if (!options.OverwriteManualEdits && target.ManualFields.HasFlag(field))
            {
                skipped |= field;
                return;
            }

            if (!hasValue() && !options.AllowClearing)
            {
                return;
            }

            assign();
            updated |= field;
        }

        Apply(
            GameField.Title,
            () => !string.IsNullOrWhiteSpace(metadata.Title),
            () =>
            {
                if (string.IsNullOrWhiteSpace(metadata.Title))
                {
                    return;
                }

                target.Title = metadata.Title;
                target.SortTitle = TitleCleaner.ToSortTitle(metadata.Title);
                target.OriginalTitle = metadata.Title;
            });

        Apply(
            GameField.Publisher,
            () => !string.IsNullOrWhiteSpace(metadata.Publisher),
            () => target.Publisher = Coalesce(metadata.Publisher, target.Publisher, options));

        Apply(
            GameField.Developer,
            () => !string.IsNullOrWhiteSpace(metadata.Developer),
            () => target.Developer = Coalesce(metadata.Developer, target.Developer, options));

        Apply(
            GameField.ReleaseYear,
            () => metadata.ReleaseYear.HasValue,
            () => target.ReleaseYear = metadata.ReleaseYear ?? (options.AllowClearing ? null : target.ReleaseYear));

        Apply(
            GameField.Description,
            () => !string.IsNullOrWhiteSpace(metadata.Description),
            () => target.Description = Coalesce(metadata.Description, target.Description, options));

        Apply(
            GameField.Players,
            () => !string.IsNullOrWhiteSpace(metadata.Players),
            () => target.Players = Coalesce(metadata.Players, target.Players, options));

        Apply(
            GameField.Genres,
            () => metadata.Genres.Count > 0,
            () =>
            {
                if (metadata.Genres.Count == 0 && !options.AllowClearing)
                {
                    return;
                }

                target.Genres.Clear();
                target.Genres.AddRange(metadata.Genres.Distinct());
            });

        if (!string.IsNullOrWhiteSpace(metadata.SystemName))
        {
            target.Platform = metadata.SystemName;
        }

        target.ScreenScraperId = metadata.ProviderGameId;
        target.MetadataLastUpdated = DateTimeOffset.UtcNow;

        return new MetadataMergeResult
        {
            UpdatedFields = updated,
            SkippedManualFields = skipped,
        };
    }

    private static string? Coalesce(string? incoming, string? existing, MetadataMergeOptions options)
    {
        if (!string.IsNullOrWhiteSpace(incoming))
        {
            return incoming;
        }

        return options.AllowClearing ? null : existing;
    }
}
