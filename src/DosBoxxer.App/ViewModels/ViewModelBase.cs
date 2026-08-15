using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DosBoxxer.Core.Abstractions;

namespace DosBoxxer.App.ViewModels;

/// <summary>
/// Base class for every ViewModel. It owns the localisation subscription so that ViewModel
/// produced strings (status messages, formatted values) update when the language changes.
/// </summary>
public abstract class ViewModelBase : ObservableObject, IDisposable
{
    private bool _disposed;

    protected ViewModelBase(ILocalizationService localization)
    {
        Localization = localization;
        Localization.LanguageChanged += HandleLanguageChanged;
    }

    public ILocalizationService Localization { get; }

    protected string L(string key) => Localization.Get(key);

    protected string L(string key, params object?[] args) => Localization.Format(key, args);

    /// <summary>
    /// Called on the thread that raised the event. Override to re-emit computed strings; use
    /// <see cref="OnPropertyChanged(string?)"/> with <c>null</c> to refresh everything.
    /// </summary>
    protected virtual void OnLanguageChanged() => OnPropertyChanged(string.Empty);

    private void HandleLanguageChanged(object? sender, EventArgs e) => OnLanguageChanged();

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            Localization.LanguageChanged -= HandleLanguageChanged;
        }

        _disposed = true;
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
