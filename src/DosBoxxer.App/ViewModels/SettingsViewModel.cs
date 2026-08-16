using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DosBoxxer.App.Services;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.App.ViewModels;

/// <summary>
/// Settings dialog. Passwords are held only in memory here and handed to
/// <see cref="ISettingsService"/>, which forwards them to the secret store; they never touch
/// <c>settings.json</c> and are never logged.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IGameMetadataProvider _metadataProvider;
    private readonly IMediaDownloader _mediaDownloader;
    private readonly IMetadataCache _metadataCache;
    private readonly IDialogService _dialogs;
    private readonly IPlatformService _platform;
    private readonly IImageLoader _imageLoader;
    private readonly IAppPaths _paths;
    private readonly ILogger<SettingsViewModel> _logger;

    private readonly string _originalLanguage;

    [ObservableProperty]
    private string _dosBoxExecutablePath = string.Empty;

    [ObservableProperty]
    private string _baseDosBoxConfigPath = string.Empty;

    [ObservableProperty]
    private string _dosBoxAdditionalArguments = string.Empty;

    [ObservableProperty]
    private bool _closeDosBoxAfterExit;

    [ObservableProperty]
    private MetadataProviderOptionViewModel? _selectedProvider;

    // -- MobyGames --
    [ObservableProperty]
    private string _mobyGamesApiKey = string.Empty;

    [ObservableProperty]
    private string _mobyGamesPlatformId = string.Empty;

    /// <summary>Bound to a NumericUpDown, whose Value is a decimal.</summary>
    [ObservableProperty]
    private decimal _mobyGamesMaxScreenshots = 6;

    // -- IGDB --
    [ObservableProperty]
    private string _igdbClientId = string.Empty;

    [ObservableProperty]
    private string _igdbClientSecret = string.Empty;

    [ObservableProperty]
    private string _igdbPlatformId = string.Empty;

    [ObservableProperty]
    private decimal _igdbMaxScreenshots = 6;

    // -- RAWG --
    [ObservableProperty]
    private string _rawgApiKey = string.Empty;

    [ObservableProperty]
    private string _rawgPlatformId = string.Empty;

    [ObservableProperty]
    private decimal _rawgMaxScreenshots = 6;

    [ObservableProperty]
    private LanguageOptionViewModel? _selectedLanguage;

    [ObservableProperty]
    private CoverSizeOptionViewModel? _selectedCoverSize;

    [ObservableProperty]
    private bool _doubleClickPlays = true;

    [ObservableProperty]
    private bool _confirmBeforeRemoving = true;

    [ObservableProperty]
    private bool _autoFetchMetadata = true;

    [ObservableProperty]
    private bool _cacheScreenshots = true;

    [ObservableProperty]
    private bool _rememberWindowState = true;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private string _cacheSizeText = string.Empty;

    public SettingsViewModel(
        ISettingsService settings,
        IGameMetadataProvider metadataProvider,
        IMediaDownloader mediaDownloader,
        IMetadataCache metadataCache,
        IDialogService dialogs,
        IPlatformService platform,
        IImageLoader imageLoader,
        IAppPaths paths,
        ILocalizationService localization,
        ILogger<SettingsViewModel> logger)
        : base(localization)
    {
        _settings = settings;
        _metadataProvider = metadataProvider;
        _mediaDownloader = mediaDownloader;
        _metadataCache = metadataCache;
        _dialogs = dialogs;
        _platform = platform;
        _imageLoader = imageLoader;
        _paths = paths;
        _logger = logger;

        foreach (var language in localization.AvailableLanguages)
        {
            Languages.Add(new LanguageOptionViewModel(language));
        }

        foreach (var size in new[] { CoverSize.Small, CoverSize.Medium, CoverSize.Large })
        {
            CoverSizes.Add(new CoverSizeOptionViewModel(size, localization));
        }

        var current = settings.Current;
        _originalLanguage = current.Language;

        _dosBoxExecutablePath = current.DosBoxExecutablePath ?? string.Empty;
        _baseDosBoxConfigPath = current.BaseDosBoxConfigPath ?? string.Empty;
        _dosBoxAdditionalArguments = current.DosBoxAdditionalArguments ?? string.Empty;
        _closeDosBoxAfterExit = current.CloseDosBoxAfterExit;

        Providers.Add(new MetadataProviderOptionViewModel("mobygames", "MobyGames"));
        Providers.Add(new MetadataProviderOptionViewModel("igdb", "IGDB"));
        Providers.Add(new MetadataProviderOptionViewModel("rawg", "RAWG"));
        _selectedProvider = Providers.FirstOrDefault(p => p.Key == current.MetadataProviderKey) ?? Providers[0];

        _mobyGamesApiKey = current.MobyGames.ApiKey ?? string.Empty;
        _mobyGamesPlatformId = current.MobyGames.PlatformId.ToString(CultureInfo.InvariantCulture);
        _mobyGamesMaxScreenshots = current.MobyGames.MaxScreenshots;

        _igdbClientId = current.Igdb.ClientId ?? string.Empty;
        _igdbClientSecret = current.Igdb.ClientSecret ?? string.Empty;
        _igdbPlatformId = current.Igdb.PlatformId.ToString(CultureInfo.InvariantCulture);
        _igdbMaxScreenshots = current.Igdb.MaxScreenshots;

        _rawgApiKey = current.Rawg.ApiKey ?? string.Empty;
        _rawgPlatformId = current.Rawg.PlatformId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _rawgMaxScreenshots = current.Rawg.MaxScreenshots;

        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == current.Language) ?? Languages[0];
        _selectedCoverSize = CoverSizes.FirstOrDefault(c => c.Size == current.CoverSize) ?? CoverSizes[1];
        _doubleClickPlays = current.DoubleClickAction == DoubleClickAction.PlayGame;
        _confirmBeforeRemoving = current.ConfirmBeforeRemoving;
        _autoFetchMetadata = current.AutoFetchMetadata;
        _cacheScreenshots = current.CacheScreenshots;
        _rememberWindowState = current.RememberWindowState;
    }

    public ObservableCollection<MetadataProviderOptionViewModel> Providers { get; } = new();

    public ObservableCollection<LanguageOptionViewModel> Languages { get; } = new();

    public ObservableCollection<CoverSizeOptionViewModel> CoverSizes { get; } = new();

    /// <summary>Section visibility follows the selected provider so the dialog stays focused.</summary>
    public bool IsMobyGamesSelected => SelectedProvider?.Key == "mobygames";

    public bool IsIgdbSelected => SelectedProvider?.Key == "igdb";

    public bool IsRawgSelected => SelectedProvider?.Key == "rawg";

    public string DataFolder => _paths.DataRoot;

    public bool HasStatusMessage => !string.IsNullOrEmpty(StatusMessage);

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    public event EventHandler<bool>? CloseRequested;

    public async Task InitializeAsync() => await UpdateCacheSizeAsync().ConfigureAwait(true);

    [RelayCommand]
    private async Task BrowseDosBoxExecutableAsync()
    {
        var file = await _dialogs
            .PickFileAsync(L("Settings.DosBoxExecutable"), FilePickerKind.Executable)
            .ConfigureAwait(true);

        if (file is not null)
        {
            DosBoxExecutablePath = file;
        }
    }

    [RelayCommand]
    private async Task BrowseBaseConfigAsync()
    {
        var file = await _dialogs
            .PickFileAsync(L("Settings.BaseConfig"), FilePickerKind.DosBoxConfig)
            .ConfigureAwait(true);

        if (file is not null)
        {
            BaseDosBoxConfigPath = file;
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        // Apply the API key the user just typed so the test uses it.
        ApplyProvidersToSettings();

        IsTesting = true;
        StatusMessage = L("Settings.Testing");
        ValidationMessage = null;

        try
        {
            var result = await _metadataProvider.TestConnectionAsync().ConfigureAwait(true);

            if (result.IsSuccess)
            {
                StatusMessage = L("Settings.ConnectionOk", result.Value ?? string.Empty);
            }
            else
            {
                StatusMessage = null;
                ValidationMessage = result.Error switch
                {
                    MetadataErrorKind.NotConfigured => L("Error.NotConfigured"),
                    MetadataErrorKind.InvalidCredentials => L("Error.InvalidApiKey"),
                    MetadataErrorKind.RateLimited => L("Error.RateLimited"),
                    MetadataErrorKind.NetworkUnavailable => L("Error.NoInternet"),
                    MetadataErrorKind.ProviderUnavailable => L("Error.ProviderUnreachable"),
                    _ => L("Error.Unexpected"),
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Connection test failed");
            StatusMessage = null;
            ValidationMessage = L("Error.Unexpected");
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private async Task ClearCacheAsync()
    {
        var confirmed = await _dialogs
            .ConfirmAsync(L("Confirm.ClearCacheTitle"), L("Confirm.ClearCacheMessage"))
            .ConfigureAwait(true);

        if (!confirmed)
        {
            return;
        }

        await _mediaDownloader.ClearCacheAsync().ConfigureAwait(true);
        await _metadataCache.ClearAsync().ConfigureAwait(true);
        _imageLoader.Clear();

        StatusMessage = L("Settings.CacheCleared");
        await UpdateCacheSizeAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        if (!_platform.OpenDirectory(_paths.DataRoot))
        {
            ValidationMessage = L("Error.OpenFolderFailed");
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ValidationMessage = null;

        if (!string.IsNullOrWhiteSpace(DosBoxExecutablePath) && !PathHelper.FileExistsSafe(DosBoxExecutablePath))
        {
            ValidationMessage = L("Settings.FileNotFound");
            return;
        }

        if (!string.IsNullOrWhiteSpace(BaseDosBoxConfigPath) && !PathHelper.FileExistsSafe(BaseDosBoxConfigPath))
        {
            ValidationMessage = L("Settings.FileNotFound");
            return;
        }

        var settings = _settings.Current;

        settings.DosBoxExecutablePath = NullIfEmpty(DosBoxExecutablePath);
        settings.BaseDosBoxConfigPath = NullIfEmpty(BaseDosBoxConfigPath);
        settings.DosBoxAdditionalArguments = NullIfEmpty(DosBoxAdditionalArguments);
        settings.CloseDosBoxAfterExit = CloseDosBoxAfterExit;

        ApplyProvidersToSettings();

        settings.Language = SelectedLanguage?.Code ?? _originalLanguage;
        settings.CoverSize = SelectedCoverSize?.Size ?? CoverSize.Medium;
        settings.DoubleClickAction = DoubleClickPlays ? DoubleClickAction.PlayGame : DoubleClickAction.ShowDetails;
        settings.ConfirmBeforeRemoving = ConfirmBeforeRemoving;
        settings.AutoFetchMetadata = AutoFetchMetadata;
        settings.CacheScreenshots = CacheScreenshots;
        settings.RememberWindowState = RememberWindowState;

        try
        {
            await _settings.SaveAsync(settings).ConfigureAwait(true);
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving the settings failed");
            ValidationMessage = L("Error.Unexpected");
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        // Undo a live language preview when the dialog is cancelled.
        if (Localization.CurrentLanguage != _originalLanguage)
        {
            Localization.SetLanguage(_originalLanguage);
        }

        CloseRequested?.Invoke(this, false);
    }

    private void ApplyProvidersToSettings()
    {
        var current = _settings.Current;
        current.MetadataProviderKey = SelectedProvider?.Key ?? "mobygames";

        // -- MobyGames --
        var moby = current.MobyGames;
        moby.ApiKey = NullIfEmpty(MobyGamesApiKey);
        moby.MaxScreenshots = Math.Clamp((int)MobyGamesMaxScreenshots, 0, 20);
        moby.PlatformId = ParsePlatformId(MobyGamesPlatformId, 2);

        // -- IGDB --
        var igdb = current.Igdb;
        igdb.ClientId = NullIfEmpty(IgdbClientId);
        igdb.ClientSecret = NullIfEmpty(IgdbClientSecret);
        igdb.MaxScreenshots = Math.Clamp((int)IgdbMaxScreenshots, 0, 20);
        igdb.PlatformId = ParsePlatformId(IgdbPlatformId, 13);

        // -- RAWG --
        var rawg = current.Rawg;
        rawg.ApiKey = NullIfEmpty(RawgApiKey);
        rawg.MaxScreenshots = Math.Clamp((int)RawgMaxScreenshots, 0, 20);
        rawg.PlatformId = int.TryParse(RawgPlatformId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawgPlatform) && rawgPlatform > 0
            ? rawgPlatform
            : null;
    }

    private static int ParsePlatformId(string value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : fallback;

    partial void OnSelectedProviderChanged(MetadataProviderOptionViewModel? value)
    {
        OnPropertyChanged(nameof(IsMobyGamesSelected));
        OnPropertyChanged(nameof(IsIgdbSelected));
        OnPropertyChanged(nameof(IsRawgSelected));
        StatusMessage = null;
        ValidationMessage = null;
    }

    private async Task UpdateCacheSizeAsync()
    {
        var bytes = await _mediaDownloader.GetCacheSizeAsync().ConfigureAwait(true);
        CacheSizeText = L("Settings.CacheSize", FormatSize(bytes));
    }

    private string FormatSize(long bytes)
    {
        if (bytes >= 1024 * 1024)
        {
            return L("Unit.Megabytes", (bytes / (1024.0 * 1024.0)).ToString("F1", Localization.CurrentCulture));
        }

        return bytes >= 1024
            ? L("Unit.Kilobytes", (bytes / 1024.0).ToString("F0", Localization.CurrentCulture))
            : L("Unit.Bytes", bytes);
    }

    /// <summary>Switching the drop-down applies the language immediately, as promised in the UI.</summary>
    partial void OnSelectedLanguageChanged(LanguageOptionViewModel? value)
    {
        if (value is not null && value.Code != Localization.CurrentLanguage)
        {
            Localization.SetLanguage(value.Code);
        }
    }

    partial void OnStatusMessageChanged(string? value) => OnPropertyChanged(nameof(HasStatusMessage));

    partial void OnValidationMessageChanged(string? value) => OnPropertyChanged(nameof(HasValidationMessage));

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        _ = UpdateCacheSizeAsync();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var size in CoverSizes)
            {
                size.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
