namespace DosBoxxer.Core.Models.Cloud;

/// <summary>
/// The cloud-side game directory (<c>dosboxxer/index.json</c> on Google Drive). It maps a stable,
/// unique cloud game id (also the game folder name, e.g. <c>48392</c>) to the normalised game
/// names the launcher knows: the title from the DosBoxxer settings, the title from the savegame
/// catalog configuration and the folder name of the game's main directory. Because all three can
/// be recomputed on any machine, the id — and with it the savegame folder — survives a
/// reinstallation.
/// </summary>
public sealed class CloudGameIndex
{
    /// <summary>Schema version of the serialised file.</summary>
    public int Version { get; set; } = 1;

    public List<CloudGameIndexEntry> Games { get; set; } = new();

    /// <summary>
    /// Finds the entry for a game, preferring the settings-name axis, then the catalog-name axis
    /// and finally the main-folder-name axis. Empty keys never match.
    /// </summary>
    public CloudGameIndexEntry? Find(string settingsKey, string catalogKey, string mainFolderKey)
    {
        if (settingsKey.Length > 0)
        {
            var bySettings = Games.FirstOrDefault(e => string.Equals(e.SettingsName, settingsKey, StringComparison.Ordinal));
            if (bySettings is not null)
            {
                return bySettings;
            }
        }

        if (catalogKey.Length > 0)
        {
            var byCatalog = Games.FirstOrDefault(e => string.Equals(e.CatalogName, catalogKey, StringComparison.Ordinal));
            if (byCatalog is not null)
            {
                return byCatalog;
            }
        }

        if (mainFolderKey.Length > 0)
        {
            return Games.FirstOrDefault(e => string.Equals(e.MainFolderName, mainFolderKey, StringComparison.Ordinal));
        }

        return null;
    }

    /// <summary>
    /// Refreshes the linked names of an existing entry when they changed (e.g. the user edited the
    /// title) or were not set yet. Empty keys never overwrite a stored name. Returns whether the
    /// entry was modified.
    /// </summary>
    public static bool UpdateNames(CloudGameIndexEntry entry, string settingsKey, string catalogKey, string mainFolderKey)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var changed = false;
        if (settingsKey.Length > 0 && !string.Equals(entry.SettingsName, settingsKey, StringComparison.Ordinal))
        {
            entry.SettingsName = settingsKey;
            changed = true;
        }

        if (catalogKey.Length > 0 && !string.Equals(entry.CatalogName, catalogKey, StringComparison.Ordinal))
        {
            entry.CatalogName = catalogKey;
            changed = true;
        }

        if (mainFolderKey.Length > 0 && !string.Equals(entry.MainFolderName, mainFolderKey, StringComparison.Ordinal))
        {
            entry.MainFolderName = mainFolderKey;
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Generates a new unique cloud game id (also the game folder name, e.g. <c>48392</c>). The id
    /// comes from <paramref name="idFactory"/> and is retried until unique within the index.
    /// </summary>
    public string GenerateId(Func<string> idFactory)
    {
        ArgumentNullException.ThrowIfNull(idFactory);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var candidate = idFactory();
            if (Games.All(e => !string.Equals(e.Id, candidate, StringComparison.Ordinal)))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("Could not generate a unique cloud game id.");
    }
}

/// <summary>One game entry of <see cref="CloudGameIndex"/>.</summary>
public sealed class CloudGameIndexEntry
{
    /// <summary>Stable unique id; also the name of the game folder below <c>dosboxxer/</c>.</summary>
    public required string Id { get; set; }

    /// <summary>Normalised title from the DosBoxxer settings (see <c>CloudGameKey</c>).</summary>
    public string? SettingsName { get; set; }

    /// <summary>Normalised title from the savegame catalog configuration (see <c>CloudGameKey</c>).</summary>
    public string? CatalogName { get; set; }

    /// <summary>Normalised folder name of the game's main directory (see <c>CloudGameKey</c>).</summary>
    public string? MainFolderName { get; set; }

    /// <summary>Google Drive folder id, cached so a manually renamed folder stays linked.</summary>
    public string? DriveFolderId { get; set; }
}

/// <summary>The resolved cloud location of a game's savegame folder.</summary>
public sealed class CloudGameFolder
{
    /// <summary>Provider-specific folder id used for all file operations.</summary>
    public required string FolderId { get; init; }

    /// <summary>Stable cloud game id from <c>index.json</c> (the folder name).</summary>
    public required string CloudGameId { get; init; }
}
