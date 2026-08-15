using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using DosBoxxer.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Localization;

/// <summary>
/// Resource based localisation backed by embedded JSON files
/// (<c>Localization/Strings/Strings.&lt;code&gt;.json</c>).
///
/// JSON rather than .resx keeps the translation files readable, diffable and trivially
/// extendable, and it is UTF-8 throughout, which matters for the Cyrillic, Chinese, Japanese
/// and Devanagari translations.
///
/// Runtime language switching works because every UI binding goes through a cached
/// <see cref="LocalizedString"/> that raises <c>PropertyChanged</c> when the language changes —
/// no view has to be recreated.
/// </summary>
public sealed class LocalizationService : ILocalizationService
{
    public const string DefaultLanguage = "en";

    private const string ResourcePrefix = "DosBoxxer.Core.Localization.Strings.Strings.";
    private const string ResourceSuffix = ".json";

    private static readonly LanguageOption[] SupportedLanguages =
    {
        new("en", "English", "English"),
        new("de", "Deutsch", "German"),
        new("fr", "Français", "French"),
        new("it", "Italiano", "Italian"),
        new("es", "Español", "Spanish"),
        new("ru", "Русский", "Russian"),
        new("zh-Hans", "简体中文", "Chinese (Simplified)"),
        new("ja", "日本語", "Japanese"),
        new("hi", "हिन्दी", "Hindi"),
    };

    private readonly ILogger<LocalizationService> _logger;
    private readonly ConcurrentDictionary<string, LocalizedString> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _loaded = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyDictionary<string, string> _current;
    private IReadOnlyDictionary<string, string> _fallback;

    public LocalizationService(ILogger<LocalizationService> logger)
    {
        _logger = logger;

        _fallback = Load(DefaultLanguage);
        _current = _fallback;
        CurrentLanguage = DefaultLanguage;
        CurrentCulture = CultureInfo.InvariantCulture;
    }

    public CultureInfo CurrentCulture { get; private set; }

    public string CurrentLanguage { get; private set; }

    public IReadOnlyList<LanguageOption> AvailableLanguages => SupportedLanguages;

    public event EventHandler? LanguageChanged;

    public string this[string key] => Get(key);

    public string Get(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            return string.Empty;
        }

        if (_current.TryGetValue(key, out var value))
        {
            return value;
        }

        if (_fallback.TryGetValue(key, out var fallbackValue))
        {
            return fallbackValue;
        }

        return key;
    }

    public string Format(string key, params object?[] args)
    {
        var template = Get(key);

        if (args.Length == 0)
        {
            return template;
        }

        try
        {
            return string.Format(CurrentCulture, template, args);
        }
        catch (FormatException)
        {
            _logger.LogWarning("Localised string '{Key}' has a malformed format placeholder", key);
            return template;
        }
    }

    public LocalizedString GetEntry(string key) =>
        _entries.GetOrAdd(key, k => new LocalizedString(() => Get(k)));

    public void SetLanguage(string languageCode)
    {
        var resolved = Resolve(languageCode);

        if (string.Equals(resolved, CurrentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _current = Load(resolved);
        CurrentLanguage = resolved;

        try
        {
            CurrentCulture = CultureInfo.GetCultureInfo(resolved);
        }
        catch (CultureNotFoundException)
        {
            _logger.LogWarning("Culture '{Language}' is not available on this system, using the invariant culture", resolved);
            CurrentCulture = CultureInfo.InvariantCulture;
        }

        foreach (var entry in _entries.Values)
        {
            entry.Refresh();
        }

        _logger.LogInformation("Application language changed to {Language}", resolved);
        LanguageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Maps an arbitrary language tag onto one of the supported languages: exact match first,
    /// then primary subtag (so <c>de-AT</c> becomes <c>de</c> and <c>zh-CN</c> becomes
    /// <c>zh-Hans</c>), otherwise English.
    /// </summary>
    public static string Resolve(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return DefaultLanguage;
        }

        var candidate = languageCode.Trim();

        var exact = SupportedLanguages.FirstOrDefault(
            l => string.Equals(l.Code, candidate, StringComparison.OrdinalIgnoreCase));

        if (exact is not null)
        {
            return exact.Code;
        }

        var primary = candidate.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(primary))
        {
            return DefaultLanguage;
        }

        if (string.Equals(primary, "zh", StringComparison.OrdinalIgnoreCase))
        {
            return "zh-Hans";
        }

        var byPrimary = SupportedLanguages.FirstOrDefault(
            l => string.Equals(l.Code, primary, StringComparison.OrdinalIgnoreCase));

        return byPrimary?.Code ?? DefaultLanguage;
    }

    private IReadOnlyDictionary<string, string> Load(string languageCode)
    {
        if (_loaded.TryGetValue(languageCode, out var cached))
        {
            return cached;
        }

        var assembly = typeof(LocalizationService).Assembly;
        var resourceName = ResourcePrefix + languageCode + ResourceSuffix;

        IReadOnlyDictionary<string, string> result;

        try
        {
            using var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream is null)
            {
                _logger.LogError("Translation resource '{Resource}' is missing from the assembly", resourceName);
                result = new Dictionary<string, string>(StringComparer.Ordinal);
            }
            else
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
                result = parsed is null
                    ? new Dictionary<string, string>(StringComparer.Ordinal)
                    : new Dictionary<string, string>(parsed, StringComparer.Ordinal);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            _logger.LogError(ex, "Translation resource '{Resource}' could not be parsed", resourceName);
            result = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        _loaded[languageCode] = result;
        return result;
    }

    /// <summary>Diagnostic helper used by the test suite to verify translation completeness.</summary>
    public IReadOnlyDictionary<string, string> GetAllStrings(string languageCode) => Load(Resolve(languageCode));

    /// <summary>Returns the assembly resource names of all embedded translation files.</summary>
    public static IReadOnlyList<string> GetEmbeddedLanguageCodes() =>
        typeof(LocalizationService).Assembly
            .GetManifestResourceNames()
            .Where(n => n.StartsWith(ResourcePrefix, StringComparison.Ordinal) && n.EndsWith(ResourceSuffix, StringComparison.Ordinal))
            .Select(n => n[ResourcePrefix.Length..^ResourceSuffix.Length])
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
}
