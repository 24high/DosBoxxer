using System;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using DosBoxxer.App.Localization;
using DosBoxxer.App.Services;
using DosBoxxer.App.ViewModels;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Infrastructure;
using DosBoxxer.Core.Infrastructure.Database;
using DosBoxxer.Core.Infrastructure.DosBox;
using DosBoxxer.Core.Infrastructure.Media;
using DosBoxxer.Core.Infrastructure.MobyGames;
using DosBoxxer.Core.Infrastructure.Repositories;
using DosBoxxer.Core.Infrastructure.ScreenScraper;
using DosBoxxer.Core.Infrastructure.Settings;
using DosBoxxer.Core.Localization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.App;

/// <summary>
/// Single place where the object graph is wired. Runs before Avalonia starts, so the blocking
/// initialisation below cannot deadlock against the UI dispatcher.
/// </summary>
public static class CompositionRoot
{
    public static ServiceProvider Build()
    {
        var paths = new AppPaths();
        paths.EnsureCreated();

        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider(paths.LogsDirectory));
#if DEBUG
            builder.AddSimpleConsole(options => options.SingleLine = true);
#endif
        });

        // ---- platform / storage -------------------------------------------------------
        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton<ISecretStore, FileSecretStore>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IPlatformService, PlatformService>();

        // ---- database -----------------------------------------------------------------
        services.AddSingleton<IDbConnectionFactory>(_ => new SqliteConnectionFactory(paths));
        services.AddSingleton<IDatabaseInitializer, DatabaseInitializer>();
        services.AddSingleton<IGameRepository, GameRepository>();

        // ---- DOSBox -------------------------------------------------------------------
        services.AddSingleton<IExecutableScanner, ExecutableScanner>();
        services.AddSingleton<IDosBoxConfigBuilder, DosBoxConfigBuilder>();
        services.AddSingleton<IDosBoxLauncher, DosBoxLauncher>();

        // ---- metadata -----------------------------------------------------------------
        // MobyGames is the active metadata provider. The ScreenScraper client is still
        // registered so the alternative provider remains available, but MobyGames supplies both
        // IGameMetadataProvider and the IMediaHttpClient used to download cover art.
        services.AddHttpClient(MobyGamesClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(MobyGamesClient.BaseAddress);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DosBoxxer", "1.0"));
        });

        services.AddHttpClient(ScreenScraperClient.HttpClientName, client =>
        {
            client.BaseAddress = new Uri(ScreenScraperClient.BaseAddress);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("DosBoxxer", "1.0"));
        });

        services.AddSingleton<MobyGamesClient>();
        services.AddSingleton<ScreenScraperClient>();
        services.AddSingleton<IMetadataCache, FileMetadataCache>();
        services.AddSingleton<IGameMetadataProvider, MobyGamesMetadataProvider>();
        services.AddSingleton<IMediaHttpClient>(sp => sp.GetRequiredService<MobyGamesClient>());
        services.AddSingleton<IMediaDownloader, MediaDownloader>();
        services.AddSingleton<IMetadataMerger, MetadataMerger>();
        services.AddSingleton<IGameLibraryService, GameLibraryService>();

        // ---- UI services --------------------------------------------------------------
        services.AddSingleton<IImageLoader, ImageLoader>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<MainWindowViewModel>();

        return services.BuildServiceProvider();
    }

    /// <summary>
    /// Startup sequence required by the specification: create directories, initialise the
    /// database, load the settings and apply the language. The game library itself is loaded
    /// later, asynchronously, so a large library never blocks the first paint.
    /// </summary>
    public static async Task InitializeAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");
        logger.LogInformation("DosBoxxer starting up on {OS}", Environment.OSVersion);

        var paths = services.GetRequiredService<IAppPaths>();
        paths.EnsureCreated();

        var settings = services.GetRequiredService<ISettingsService>();
        await settings.LoadAsync().ConfigureAwait(false);

        var localization = services.GetRequiredService<ILocalizationService>();
        localization.SetLanguage(settings.Current.Language);
        Loc.Initialize(localization);

        try
        {
            var database = services.GetRequiredService<IDatabaseInitializer>();
            await database.InitializeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database initialisation failed");
        }

        logger.LogInformation("Startup finished, data directory is {Path}", paths.DataRoot);
    }
}
