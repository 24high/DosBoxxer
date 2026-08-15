using System;
using Avalonia;
using Microsoft.Extensions.DependencyInjection;

namespace DosBoxxer.App;

internal static class Program
{
    // Avalonia requires an STA thread on Windows and must be started after the service graph
    // is ready, so that the first window can be created fully initialised.
    [STAThread]
    public static void Main(string[] args)
    {
        var services = CompositionRoot.Build();

        try
        {
            // Safe to block here: the Avalonia dispatcher does not exist yet.
            CompositionRoot.InitializeAsync(services).GetAwaiter().GetResult();

            App.Services = services;
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("DosBoxxer failed to start: " + ex);
            throw;
        }
        finally
        {
            services.Dispose();
        }
    }

    /// <summary>Used by the Avalonia designer and by <see cref="Main"/>.</summary>
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
