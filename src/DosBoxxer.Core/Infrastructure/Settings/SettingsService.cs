using System.Text.Json;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.Core.Infrastructure.Settings;

/// <summary>
/// JSON backed settings storage. Secrets are read from / written to <see cref="ISecretStore"/>
/// and are never part of the JSON document.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly IAppPaths _paths;
    private readonly ISecretStore _secretStore;
    private readonly ILogger<SettingsService> _logger;
    private readonly SemaphoreSlim _saveLock = new(1, 1);

    public SettingsService(IAppPaths paths, ISecretStore secretStore, ILogger<SettingsService> logger)
    {
        _paths = paths;
        _secretStore = secretStore;
        _logger = logger;
    }

    public AppSettings Current { get; private set; } = new();

    public event EventHandler<AppSettings>? SettingsChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        AppSettings settings;

        try
        {
            if (File.Exists(_paths.SettingsFile))
            {
                await using var stream = File.OpenRead(_paths.SettingsFile);
                settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
                               .ConfigureAwait(false)
                           ?? new AppSettings();
                _logger.LogInformation("Settings loaded from {Path}", _paths.SettingsFile);
            }
            else
            {
                settings = new AppSettings();
                _logger.LogInformation("No settings file found, using defaults");
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to read settings, falling back to defaults");
            settings = new AppSettings();
        }

        settings.ScreenScraper ??= new ScreenScraperSettings();
        settings.MobyGames ??= new MobyGamesSettings();
        settings.Window ??= new WindowStateSettings();

        settings.ScreenScraper.DeveloperPassword =
            await _secretStore.GetAsync(SecretKeys.ScreenScraperDeveloperPassword, cancellationToken).ConfigureAwait(false);
        settings.ScreenScraper.UserPassword =
            await _secretStore.GetAsync(SecretKeys.ScreenScraperUserPassword, cancellationToken).ConfigureAwait(false);
        settings.MobyGames.ApiKey =
            await _secretStore.GetAsync(SecretKeys.MobyGamesApiKey, cancellationToken).ConfigureAwait(false);

        Current = settings;
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _secretStore.SetAsync(SecretKeys.ScreenScraperDeveloperPassword, settings.ScreenScraper.DeveloperPassword, cancellationToken)
            .ConfigureAwait(false);
        await _secretStore.SetAsync(SecretKeys.ScreenScraperUserPassword, settings.ScreenScraper.UserPassword, cancellationToken)
            .ConfigureAwait(false);
        await _secretStore.SetAsync(SecretKeys.MobyGamesApiKey, settings.MobyGames.ApiKey, cancellationToken)
            .ConfigureAwait(false);

        Current = settings;
        await WriteAsync(settings, cancellationToken).ConfigureAwait(false);

        SettingsChanged?.Invoke(this, settings);
    }

    public Task SaveCurrentAsync(CancellationToken cancellationToken = default) =>
        WriteAsync(Current, cancellationToken);

    private async Task WriteAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        await _saveLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_paths.SettingsFile)!);

            // Write to a temporary file first so a crash cannot truncate the settings.
            var tempFile = _paths.SettingsFile + ".tmp";

            await using (var stream = File.Create(tempFile))
            {
                await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);
            }

            File.Move(tempFile, _paths.SettingsFile, overwrite: true);
            _logger.LogDebug("Settings written to {Path}", _paths.SettingsFile);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogError(ex, "Failed to write settings to {Path}", _paths.SettingsFile);
        }
        finally
        {
            _saveLock.Release();
        }
    }
}
