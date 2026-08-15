using DosBoxxer.Core.Models;

namespace DosBoxxer.Core.Abstractions;

public interface ISettingsService
{
    /// <summary>The live settings instance. Never <c>null</c> after <see cref="LoadAsync"/>.</summary>
    AppSettings Current { get; }

    event EventHandler<AppSettings>? SettingsChanged;

    Task LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists <paramref name="settings"/> and raises <see cref="SettingsChanged"/>.</summary>
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);

    /// <summary>Persists the current instance (used for window state and similar side updates).</summary>
    Task SaveCurrentAsync(CancellationToken cancellationToken = default);
}
