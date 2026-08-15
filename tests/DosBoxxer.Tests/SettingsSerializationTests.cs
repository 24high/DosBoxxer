using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Infrastructure;
using DosBoxxer.Core.Infrastructure.Settings;
using DosBoxxer.Core.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DosBoxxer.Tests;

public sealed class SettingsSerializationTests
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void AppSettings_RoundTripsThroughJson()
    {
        var settings = new AppSettings
        {
            DosBoxExecutablePath = "/usr/bin/dosbox",
            BaseDosBoxConfigPath = "/home/user/.dosbox/dosbox.conf",
            DosBoxAdditionalArguments = "-noconsole",
            CloseDosBoxAfterExit = true,
            Language = "de",
            CoverSize = CoverSize.Large,
            DoubleClickAction = DoubleClickAction.ShowDetails,
            ConfirmBeforeRemoving = false,
            AutoFetchMetadata = false,
            CacheScreenshots = false,
            RememberWindowState = false,
            ShowDetailsPane = false,
            SortOrder = GameSortOrder.YearDescending,
        };

        settings.ScreenScraper.DeveloperId = "dev";
        settings.ScreenScraper.SoftwareName = "DosBoxxer";
        settings.ScreenScraper.UserName = "user";
        settings.ScreenScraper.SystemId = 135;
        settings.ScreenScraper.PreferredRegion = "eu";
        settings.ScreenScraper.MaxScreenshots = 4;
        settings.Window.Width = 1200;
        settings.Window.Height = 800;
        settings.Window.X = 20;
        settings.Window.Y = 30;
        settings.Window.IsMaximized = true;

        var json = JsonSerializer.Serialize(settings, Options);
        var restored = JsonSerializer.Deserialize<AppSettings>(json, Options);

        Assert.NotNull(restored);
        Assert.Equal("/usr/bin/dosbox", restored!.DosBoxExecutablePath);
        Assert.Equal("de", restored.Language);
        Assert.Equal(CoverSize.Large, restored.CoverSize);
        Assert.Equal(DoubleClickAction.ShowDetails, restored.DoubleClickAction);
        Assert.Equal(GameSortOrder.YearDescending, restored.SortOrder);
        Assert.Equal(135, restored.ScreenScraper.SystemId);
        Assert.Equal("eu", restored.ScreenScraper.PreferredRegion);
        Assert.Equal(4, restored.ScreenScraper.MaxScreenshots);
        Assert.Equal(1200, restored.Window.Width);
        Assert.True(restored.Window.IsMaximized);
    }

    [Fact]
    public void Passwords_AreNeverSerialisedIntoTheSettingsJson()
    {
        var settings = new AppSettings();
        settings.ScreenScraper.DeveloperId = "dev";
        settings.ScreenScraper.DeveloperPassword = "super-secret-dev";
        settings.ScreenScraper.UserPassword = "super-secret-user";

        var json = JsonSerializer.Serialize(settings, Options);

        Assert.DoesNotContain("super-secret-dev", json);
        Assert.DoesNotContain("super-secret-user", json);
        Assert.Contains("dev", json);
    }

    [Fact]
    public void ScreenScraper_IsAlwaysQueryableAndReportsAccessTiers()
    {
        var settings = new ScreenScraperSettings();

        // Out of the box: no credentials, yet a request can be formed (anonymous access).
        Assert.True(settings.CanQuery);
        Assert.False(settings.HasDeveloperAccess);
        Assert.False(settings.HasUserAccount);

        settings.UserName = "user";
        settings.UserPassword = "pass";
        Assert.True(settings.HasUserAccount);
        Assert.False(settings.HasDeveloperAccess);

        settings.DeveloperId = "dev";
        settings.DeveloperPassword = "devpass";
        Assert.True(settings.HasDeveloperAccess);

        // Clearing the software name is the only thing that disables querying.
        settings.SoftwareName = string.Empty;
        Assert.False(settings.CanQuery);
    }

    [Fact]
    public void Defaults_AreSensible()
    {
        var settings = new AppSettings();

        Assert.Equal("en", settings.Language);
        Assert.Equal(CoverSize.Medium, settings.CoverSize);
        Assert.Equal(DoubleClickAction.PlayGame, settings.DoubleClickAction);
        Assert.True(settings.ConfirmBeforeRemoving);
        Assert.True(settings.AutoFetchMetadata);
        Assert.Equal(135, settings.ScreenScraper.SystemId);
        Assert.Equal("DosBoxxer", settings.ScreenScraper.SoftwareName);
    }

    [Fact]
    public void Clone_ProducesAnIndependentCopy()
    {
        var settings = new AppSettings { Language = "fr" };
        settings.ScreenScraper.DeveloperId = "a";

        var clone = settings.Clone();
        clone.Language = "it";
        clone.ScreenScraper.DeveloperId = "b";

        Assert.Equal("fr", settings.Language);
        Assert.Equal("a", settings.ScreenScraper.DeveloperId);
    }

    [Fact]
    public async Task SettingsService_PersistsAndReloads()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();

        var secrets = new FileSecretStore(paths, NullLogger<FileSecretStore>.Instance);
        var service = new SettingsService(paths, secrets, NullLogger<SettingsService>.Instance);

        await service.LoadAsync();

        var settings = service.Current;
        settings.Language = "ja";
        settings.DosBoxExecutablePath = "/usr/bin/dosbox";
        settings.ScreenScraper.DeveloperId = "dev-id";
        settings.ScreenScraper.DeveloperPassword = "dev-password";

        await service.SaveAsync(settings);

        Assert.True(File.Exists(paths.SettingsFile));
        var raw = await File.ReadAllTextAsync(paths.SettingsFile);
        Assert.DoesNotContain("dev-password", raw);

        // A fresh service instance must see the same values, including the secret.
        var reloadSecrets = new FileSecretStore(paths, NullLogger<FileSecretStore>.Instance);
        var reloaded = new SettingsService(paths, reloadSecrets, NullLogger<SettingsService>.Instance);
        await reloaded.LoadAsync();

        Assert.Equal("ja", reloaded.Current.Language);
        Assert.Equal("/usr/bin/dosbox", reloaded.Current.DosBoxExecutablePath);
        Assert.Equal("dev-id", reloaded.Current.ScreenScraper.DeveloperId);
        Assert.Equal("dev-password", reloaded.Current.ScreenScraper.DeveloperPassword);
    }

    [Fact]
    public async Task SettingsService_FallsBackToDefaultsOnCorruptFile()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();

        await File.WriteAllTextAsync(paths.SettingsFile, "{ this is not valid json");

        var secrets = new FileSecretStore(paths, NullLogger<FileSecretStore>.Instance);
        var service = new SettingsService(paths, secrets, NullLogger<SettingsService>.Instance);

        await service.LoadAsync();

        Assert.Equal("en", service.Current.Language);
    }

    [Fact]
    public async Task SecretStore_EncryptsValuesAtRest()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();

        var store = new FileSecretStore(paths, NullLogger<FileSecretStore>.Instance);
        await store.SetAsync(SecretKeys.ScreenScraperUserPassword, "plain-text-secret");

        var raw = await File.ReadAllTextAsync(paths.SecretsFile);
        Assert.DoesNotContain("plain-text-secret", raw);

        var reopened = new FileSecretStore(paths, NullLogger<FileSecretStore>.Instance);
        Assert.Equal("plain-text-secret", await reopened.GetAsync(SecretKeys.ScreenScraperUserPassword));
    }

    [Fact]
    public async Task SecretStore_RemovesValues()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();

        var store = new FileSecretStore(paths, NullLogger<FileSecretStore>.Instance);
        await store.SetAsync("k", "v");
        await store.RemoveAsync("k");

        Assert.Null(await store.GetAsync("k"));
    }

    [Fact]
    public void AppPaths_BuildsAllRequiredDirectories()
    {
        using var temp = new TempDirectory();
        var paths = new AppPaths(temp.Path);
        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.DatabaseDirectory));
        Assert.True(Directory.Exists(paths.CoversDirectory));
        Assert.True(Directory.Exists(paths.ScreenshotsDirectory));
        Assert.True(Directory.Exists(paths.MetadataCacheDirectory));
        Assert.True(Directory.Exists(paths.TempDirectory));
        Assert.True(Directory.Exists(paths.LogsDirectory));

        Assert.StartsWith(paths.CacheDirectory, paths.CoversDirectory);
        Assert.EndsWith("library.db", paths.DatabaseFile);
    }
}
