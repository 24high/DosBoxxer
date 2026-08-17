using System.Reflection;
using System.Text.Json;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models.Savegame;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Savegame;

/// <summary>
/// Loads and caches the bundled savegame path catalog and delegates fuzzy search to an
/// <see cref="ITitleMatcher"/>. The catalog file is embedded in the assembly so it is always
/// available; an optional external path (from settings) overrides it when present.
/// </summary>
public sealed class SavegameCatalog : ISavegameCatalog
{
    private const string EmbeddedResourceName = "DosBoxxer.Core.Assets.dos_savegame_pfade.json";

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly ITitleMatcher _matcher;
    private readonly ILogger<SavegameCatalog> _logger;
    private readonly string? _overridePath;
    private readonly object _gate = new();

    private IReadOnlyList<CatalogEntry>? _entries;

    public SavegameCatalog(ITitleMatcher matcher, ILogger<SavegameCatalog> logger, string? overridePath = null)
    {
        _matcher = matcher;
        _logger = logger;
        _overridePath = overridePath;
    }

    public IReadOnlyList<CatalogEntry> Entries
    {
        get
        {
            if (_entries is not null)
            {
                return _entries;
            }

            lock (_gate)
            {
                return _entries ??= Load();
            }
        }
    }

    public IReadOnlyList<TitleMatch> FindMatches(string query, int maxResults = 8) =>
        _matcher.Rank(query, Entries, maxResults);

    public CatalogEntry? FindByExactTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var target = title.Trim();
        return Entries.FirstOrDefault(e =>
            string.Equals(e.Title.Trim(), target, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<CatalogEntry> Load()
    {
        try
        {
            using var stream = OpenCatalogStream();
            if (stream is null)
            {
                _logger.LogWarning("Savegame catalog could not be located; continuing with an empty catalog");
                return Array.Empty<CatalogEntry>();
            }

            var rows = JsonSerializer.Deserialize<List<CatalogRow>>(stream, SerializerOptions);
            if (rows is null)
            {
                return Array.Empty<CatalogEntry>();
            }

            var entries = new List<CatalogEntry>(rows.Count);
            foreach (var row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.Spiel))
                {
                    continue;
                }

                var entry = SavegamePathParser.Parse(row.ManifestPath ?? row.DosPattern, row.Art);
                if (entry is null)
                {
                    continue;
                }

                entries.Add(new CatalogEntry
                {
                    Title = row.Spiel.Trim(),
                    Entry = entry,
                    RawKind = row.Art,
                    RawPattern = row.DosPattern,
                });
            }

            _logger.LogInformation("Loaded {Count} savegame catalog entries", entries.Count);
            return entries;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to load the savegame catalog; continuing with an empty catalog");
            return Array.Empty<CatalogEntry>();
        }
    }

    private Stream? OpenCatalogStream()
    {
        if (!string.IsNullOrWhiteSpace(_overridePath) && File.Exists(_overridePath))
        {
            _logger.LogInformation("Loading savegame catalog from external file {Path}", _overridePath);
            return File.OpenRead(_overridePath);
        }

        return typeof(SavegameCatalog).Assembly.GetManifestResourceStream(EmbeddedResourceName);
    }

    private sealed class CatalogRow
    {
        [System.Text.Json.Serialization.JsonPropertyName("Spiel")]
        public string? Spiel { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("DOS-Savepfad / Muster")]
        public string? DosPattern { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("Manifest-Pfad")]
        public string? ManifestPath { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("Art")]
        public string? Art { get; set; }
    }
}
