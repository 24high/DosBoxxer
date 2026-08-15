using System;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using DosBoxxer.Core.Abstractions;

namespace DosBoxxer.App.Localization;

/// <summary>
/// Static access point for the localisation service. XAML markup extensions cannot resolve
/// services from the DI container, so the composition root publishes the instance here once.
/// </summary>
public static class Loc
{
    private static ILocalizationService? _instance;

    public static ILocalizationService Instance =>
        _instance ?? throw new InvalidOperationException(
            "The localisation service has not been initialised. Call Loc.Initialize during startup.");

    public static bool IsInitialized => _instance is not null;

    public static void Initialize(ILocalizationService service) => _instance = service;

    public static string Get(string key) => IsInitialized ? Instance.Get(key) : key;
}

/// <summary>
/// XAML markup extension: <c>{loc:Localize Details.Play}</c>.
///
/// It returns a binding to a cached <see cref="LocalizedString"/> rather than a plain string,
/// which is what makes switching the language at runtime update every visible label without
/// recreating any view.
/// </summary>
public sealed class LocalizeExtension : MarkupExtension
{
    public LocalizeExtension()
    {
    }

    public LocalizeExtension(string key) => Key = key;

    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (!Loc.IsInitialized)
        {
            // Designer / unit test fallback: show the key instead of crashing.
            return Key;
        }

        return new Binding
        {
            Source = Loc.Instance.GetEntry(Key),
            Path = nameof(LocalizedString.Value),
            Mode = BindingMode.OneWay,
        };
    }
}
