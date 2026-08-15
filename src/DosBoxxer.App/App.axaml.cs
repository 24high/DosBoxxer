using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DosBoxxer.App.ViewModels;
using DosBoxxer.App.Views;
using DosBoxxer.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace DosBoxxer.App;

public partial class App : Application
{
    /// <summary>Set by <see cref="Program"/> before Avalonia starts.</summary>
    public static IServiceProvider? Services { get; set; }

    public static T GetService<T>()
        where T : notnull =>
        Services is null
            ? throw new InvalidOperationException("The service provider has not been initialised.")
            : Services.GetRequiredService<T>();

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && Services is not null)
        {
            var viewModel = Services.GetRequiredService<MainWindowViewModel>();
            var settings = Services.GetRequiredService<ISettingsService>();

            var window = new MainWindow
            {
                DataContext = viewModel,
            };

            window.ApplyStoredWindowState(settings.Current);
            desktop.MainWindow = window;

            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
