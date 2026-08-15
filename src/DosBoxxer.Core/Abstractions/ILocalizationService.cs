using System.ComponentModel;
using System.Globalization;

namespace DosBoxxer.Core.Abstractions;

public sealed record LanguageOption(string Code, string NativeName, string EnglishName);

/// <summary>
/// A single localised string that raises <see cref="INotifyPropertyChanged"/> when the
/// application language changes. XAML binds to <see cref="Value"/>, which makes runtime
/// language switching work without recreating any view.
/// </summary>
public sealed class LocalizedString : INotifyPropertyChanged
{
    private readonly Func<string> _resolver;
    private string _value;

    public LocalizedString(Func<string> resolver)
    {
        _resolver = resolver;
        _value = resolver();
    }

    public string Value
    {
        get => _value;
        private set
        {
            if (_value == value)
            {
                return;
            }

            _value = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }
    }

    public void Refresh() => Value = _resolver();

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => Value;
}

public interface ILocalizationService
{
    CultureInfo CurrentCulture { get; }

    string CurrentLanguage { get; }

    IReadOnlyList<LanguageOption> AvailableLanguages { get; }

    event EventHandler? LanguageChanged;

    /// <summary>Returns the translated string, or the key itself when it is unknown.</summary>
    string this[string key] { get; }

    string Get(string key);

    /// <summary>Composite formatting with the current culture.</summary>
    string Format(string key, params object?[] args);

    /// <summary>Returns a cached, self-updating string for XAML bindings.</summary>
    LocalizedString GetEntry(string key);

    void SetLanguage(string languageCode);
}
