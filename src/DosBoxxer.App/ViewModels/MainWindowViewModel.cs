using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DosBoxxer.App.Services;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Metadata;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.App.ViewModels;

/// <summary>
/// Drives the three-column main window: categories, library grid and details pane.
/// All library mutations go through <see cref="IGameLibraryService"/>; this class only
/// coordinates state, filtering and user feedback.
/// </summary>
public sealed partial class MainWindowViewModel : ViewModelBase
{
    private static readonly TimeSpan SearchDebounce = TimeSpan.FromMilliseconds(300);

    private readonly IGameLibraryService _library;
    private readonly IDosBoxLauncher _launcher;
    private readonly ISettingsService _settings;
    private readonly IPlatformService _platform;
    private readonly IDialogService _dialogs;
    private readonly IImageLoader _imageLoader;
    private readonly ILogger<MainWindowViewModel> _logger;

    private readonly List<Game> _allGames = new();

    private CancellationTokenSource? _searchDebounceCts;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isGameRunning;

    [ObservableProperty]
    private bool _showDetailsPane = true;

    /// <summary>
    /// Set by the window when it gets narrow. In compact layout the details pane collapses so
    /// the library keeps a usable width; the user's own preference is remembered separately.
    /// </summary>
    [ObservableProperty]
    private bool _isCompactLayout;

    [ObservableProperty]
    private string _runningGameText = string.Empty;

    [ObservableProperty]
    private GameCardViewModel? _selectedCard;

    [ObservableProperty]
    private CategoryViewModel? _selectedCategory;

    [ObservableProperty]
    private SortOptionViewModel? _selectedSortOption;

    [ObservableProperty]
    private CoverSizeOptionViewModel? _selectedCoverSize;

    public MainWindowViewModel(
        IGameLibraryService library,
        IDosBoxLauncher launcher,
        ISettingsService settings,
        IPlatformService platform,
        IDialogService dialogs,
        IImageLoader imageLoader,
        ILocalizationService localization,
        ILogger<MainWindowViewModel> logger)
        : base(localization)
    {
        _library = library;
        _launcher = launcher;
        _settings = settings;
        _platform = platform;
        _dialogs = dialogs;
        _imageLoader = imageLoader;
        _logger = logger;

        Details = new GameDetailsViewModel(imageLoader, localization);

        foreach (var order in GameSortOrders.All)
        {
            SortOptions.Add(new SortOptionViewModel(order, localization));
        }

        foreach (var size in new[] { CoverSize.Small, CoverSize.Medium, CoverSize.Large })
        {
            CoverSizes.Add(new CoverSizeOptionViewModel(size, localization));
        }

        BuildCategories();

        var current = _settings.Current;
        _selectedSortOption = SortOptions.FirstOrDefault(s => s.Order == current.SortOrder) ?? SortOptions[0];
        _selectedCoverSize = CoverSizes.FirstOrDefault(c => c.Size == current.CoverSize) ?? CoverSizes[1];
        _showDetailsPane = current.ShowDetailsPane;
        _statusMessage = L("Status.Ready");
    }

    public GameDetailsViewModel Details { get; }

    public ObservableCollection<CategoryViewModel> Categories { get; } = new();

    public ObservableCollection<GameCardViewModel> Games { get; } = new();

    public ObservableCollection<SortOptionViewModel> SortOptions { get; } = new();

    public ObservableCollection<CoverSizeOptionViewModel> CoverSizes { get; } = new();

    public bool HasGames => _allGames.Count > 0;

    public bool IsLibraryEmpty => _allGames.Count == 0 && !IsBusy;

    public bool HasNoResults => _allGames.Count > 0 && Games.Count == 0;

    public bool HasSelection => SelectedCard is not null;

    public bool IsDetailsPaneVisible => ShowDetailsPane && !IsCompactLayout;

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public string GameCountText => Games.Count == 1
        ? L("Library.GameCountOne")
        : L("Library.GameCount", Games.Count);

    // ---- lifecycle ------------------------------------------------------------------------

    public async Task InitializeAsync()
    {
        await LoadLibraryAsync().ConfigureAwait(true);
    }

    private void BuildCategories()
    {
        Categories.Clear();

        Categories.Add(new CategoryViewModel(Localization, LibraryScope.AllGames, null, "Library.AllGames", "IconAllGames"));
        Categories.Add(new CategoryViewModel(Localization, LibraryScope.Favorites, null, "Library.Favorites", "IconFavorite"));
        Categories.Add(new CategoryViewModel(Localization, LibraryScope.RecentlyPlayed, null, "Library.RecentlyPlayed", "IconRecent"));

        foreach (var genre in GenreKeys.All)
        {
            Categories.Add(new CategoryViewModel(
                Localization,
                LibraryScope.Genre,
                genre,
                GenreKeys.ResourceKey(genre),
                "IconGenre" + genre));
        }

        SelectedCategory = Categories[0];
        Categories[0].IsSelected = true;
    }

    [RelayCommand]
    private async Task LoadLibraryAsync()
    {
        IsBusy = true;
        StatusMessage = L("Status.LoadingLibrary");
        ErrorMessage = null;

        try
        {
            var games = await _library.GetLibraryAsync().ConfigureAwait(true);

            _allGames.Clear();
            _allGames.AddRange(games);

            UpdateCategoryCounts();
            ApplyFilter();

            StatusMessage = L("Status.Ready");
            _logger.LogInformation("Library loaded with {Count} game(s)", _allGames.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading the library failed");
            ErrorMessage = L("Error.DatabaseError");
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(IsLibraryEmpty));
            OnPropertyChanged(nameof(HasGames));
        }
    }

    private void UpdateCategoryCounts()
    {
        foreach (var category in Categories)
        {
            category.Count = category.Scope switch
            {
                LibraryScope.AllGames => _allGames.Count,
                LibraryScope.Favorites => _allGames.Count(g => g.IsFavorite),
                LibraryScope.RecentlyPlayed => _allGames.Count(g => g.LastPlayed.HasValue),
                LibraryScope.Genre when category.Genre.HasValue =>
                    _allGames.Count(g => g.Genres.Contains(category.Genre.Value)),
                _ => 0,
            };
        }
    }

    private void ApplyFilter()
    {
        var category = SelectedCategory ?? Categories[0];
        var sortOrder = SelectedSortOption?.Order ?? GameSortOrder.TitleAscending;
        var query = category.ToQuery(SearchText, sortOrder);

        var filtered = _library.Filter(_allGames, query);
        var previousId = SelectedCard?.Id;
        var coverWidth = SelectedCoverSize?.Width ?? 168;

        foreach (var card in Games)
        {
            card.Dispose();
        }

        Games.Clear();

        foreach (var game in filtered)
        {
            var card = new GameCardViewModel(game, _imageLoader, Localization, coverWidth);
            Games.Add(card);
        }

        OnPropertyChanged(nameof(HasNoResults));
        OnPropertyChanged(nameof(GameCountText));

        SelectedCard = previousId.HasValue
            ? Games.FirstOrDefault(c => c.Id == previousId.Value)
            : null;

        // Thumbnails load in the background; the loader caps concurrency internally.
        foreach (var card in Games)
        {
            _ = card.EnsureThumbnailAsync();
        }
    }

    // ---- search ---------------------------------------------------------------------------

    partial void OnSearchTextChanged(string value)
    {
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = new CancellationTokenSource();
        var token = _searchDebounceCts.Token;

        _ = DebouncedFilterAsync(token);
    }

    private async Task DebouncedFilterAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SearchDebounce, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested)
        {
            ApplyFilter();
        }
    }

    partial void OnSelectedCategoryChanged(CategoryViewModel? value)
    {
        foreach (var category in Categories)
        {
            category.IsSelected = ReferenceEquals(category, value);
        }

        ApplyFilter();
    }

    partial void OnSelectedSortOptionChanged(SortOptionViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        _settings.Current.SortOrder = value.Order;
        _ = _settings.SaveCurrentAsync();
        ApplyFilter();
    }

    partial void OnSelectedCoverSizeChanged(CoverSizeOptionViewModel? value)
    {
        if (value is null)
        {
            return;
        }

        _settings.Current.CoverSize = value.Size;
        _ = _settings.SaveCurrentAsync();

        foreach (var card in Games)
        {
            card.CoverWidth = value.Width;
        }
    }

    partial void OnSelectedCardChanged(GameCardViewModel? value)
    {
        foreach (var card in Games)
        {
            card.IsSelected = ReferenceEquals(card, value);
        }

        OnPropertyChanged(nameof(HasSelection));
        _ = Details.SetGameAsync(value?.Game);
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(IsLibraryEmpty));

    partial void OnShowDetailsPaneChanged(bool value)
    {
        _settings.Current.ShowDetailsPane = value;
        _ = _settings.SaveCurrentAsync();
        OnPropertyChanged(nameof(IsDetailsPaneVisible));
    }

    partial void OnIsCompactLayoutChanged(bool value) => OnPropertyChanged(nameof(IsDetailsPaneVisible));

    partial void OnIsGameRunningChanged(bool value)
    {
        if (!value)
        {
            RunningGameText = string.Empty;
        }
    }

    // ---- commands -------------------------------------------------------------------------

    [RelayCommand]
    private void SelectCategory(CategoryViewModel? category)
    {
        if (category is not null)
        {
            SelectedCategory = category;
        }
    }

    [RelayCommand]
    private void DismissError() => ErrorMessage = null;

    [RelayCommand]
    private void ToggleDetailsPane() => ShowDetailsPane = !ShowDetailsPane;

    [RelayCommand]
    private async Task AddGameAsync()
    {
        var game = await _dialogs.ShowAddGameWizardAsync().ConfigureAwait(true);
        if (game is null)
        {
            return;
        }

        _allGames.Add(game);
        UpdateCategoryCounts();
        ApplyFilter();

        SelectedCard = Games.FirstOrDefault(c => c.Id == game.Id);
        StatusMessage = L("Status.GameAdded", game.Title);
        OnPropertyChanged(nameof(IsLibraryEmpty));
        OnPropertyChanged(nameof(HasGames));
    }

    [RelayCommand]
    private async Task PlayAsync(GameCardViewModel? card)
    {
        var target = card ?? SelectedCard;
        if (target is null || IsGameRunning)
        {
            return;
        }

        var game = target.Game;

        IsGameRunning = true;
        RunningGameText = L("Status.GameRunning", game.Title);
        StatusMessage = L("Status.LaunchingGame", game.Title);
        ErrorMessage = null;

        try
        {
            var result = await _launcher.LaunchAsync(game).ConfigureAwait(true);

            if (!result.Success)
            {
                ErrorMessage = DescribeLaunchFailure(result.Status);
                _logger.LogWarning("Launch failed with status {Status}", result.Status);
                return;
            }

            if (result.StartedAt.HasValue)
            {
                await _library
                    .RecordPlaySessionAsync(game, result.StartedAt.Value, result.Duration)
                    .ConfigureAwait(true);
            }

            target.RefreshFromModel();
            Details.RaiseAll();
            UpdateCategoryCounts();

            StatusMessage = L("Status.GameFinished", game.Title, FormatDuration(result.Duration));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while running the game");
            ErrorMessage = L("Error.Unexpected");
        }
        finally
        {
            IsGameRunning = false;
        }
    }

    private string DescribeLaunchFailure(LaunchStatus status) => status switch
    {
        LaunchStatus.ExecutableNotConfigured => L("Error.DosBoxNotConfigured"),
        LaunchStatus.ExecutableNotFound => L("Error.DosBoxNotFound"),
        LaunchStatus.BaseConfigNotFound => L("Error.BaseConfigNotFound"),
        LaunchStatus.GameDirectoryMissing => L("Error.GameDirectoryMissing"),
        LaunchStatus.LaunchFileMissing => L("Error.LaunchFileMissing"),
        LaunchStatus.ConfigWriteFailed => L("Error.ConfigWriteFailed"),
        LaunchStatus.StartFailed => L("Error.LaunchFailed"),
        _ => L("Error.Unexpected"),
    };

    private string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
        {
            return L("Unit.HoursMinutes", (int)duration.TotalHours, duration.Minutes);
        }

        return duration.TotalMinutes >= 1
            ? L("Unit.Minutes", (int)duration.TotalMinutes)
            : L("Unit.Seconds", (int)duration.TotalSeconds);
    }

    [RelayCommand]
    private async Task EditGameAsync(GameCardViewModel? card)
    {
        var target = card ?? SelectedCard;
        if (target is null)
        {
            return;
        }

        var saved = await _dialogs.ShowEditGameAsync(target.Game).ConfigureAwait(true);
        if (!saved)
        {
            return;
        }

        target.RefreshFromModel();
        _ = target.EnsureThumbnailAsync();
        UpdateCategoryCounts();
        ApplyFilter();
        SelectedCard = Games.FirstOrDefault(c => c.Id == target.Id);
        await Details.SetGameAsync(target.Game).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RefreshMetadataAsync(GameCardViewModel? card)
    {
        var target = card ?? SelectedCard;
        if (target is null)
        {
            return;
        }

        var choice = await _dialogs.ShowRefreshMetadataAsync(target.Game).ConfigureAwait(true);
        if (choice is null)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = L("Status.SearchingMetadata");
        ErrorMessage = null;

        try
        {
            var options = new MetadataMergeOptions
            {
                Fields = choice.Fields,
                OverwriteManualEdits = choice.OverwriteManualEdits,
            };

            var result = await _library
                .RefreshMetadataAsync(target.Game, choice.ProviderGameId, options)
                .ConfigureAwait(true);

            if (!result.IsSuccess)
            {
                ErrorMessage = DescribeMetadataError(result.Error);
                return;
            }

            target.RefreshFromModel();
            _ = target.EnsureThumbnailAsync();
            UpdateCategoryCounts();
            await Details.SetGameAsync(target.Game).ConfigureAwait(true);

            StatusMessage = L("Status.MetadataUpdated");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metadata refresh failed");
            ErrorMessage = L("Error.Unexpected");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private string DescribeMetadataError(MetadataErrorKind error) => error switch
    {
        MetadataErrorKind.NotConfigured => L("Error.NotConfigured"),
        MetadataErrorKind.InvalidCredentials => L("Error.InvalidApiKey"),
        MetadataErrorKind.RateLimited => L("Error.RateLimited"),
        MetadataErrorKind.NoResults => L("Error.NoSearchResults"),
        MetadataErrorKind.NetworkUnavailable => L("Error.NoInternet"),
        MetadataErrorKind.ProviderUnavailable => L("Error.ProviderUnreachable"),
        MetadataErrorKind.InvalidResponse => L("Error.InvalidResponse"),
        MetadataErrorKind.Cancelled => L("Status.Ready"),
        _ => L("Error.Unexpected"),
    };

    [RelayCommand]
    private async Task OpenGameFolderAsync(GameCardViewModel? card)
    {
        var target = card ?? SelectedCard;
        if (target is null)
        {
            return;
        }

        if (!_platform.OpenDirectory(target.Game.GameDirectory))
        {
            ErrorMessage = L("Error.OpenFolderFailed");
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task RemoveGameAsync(GameCardViewModel? card)
    {
        var target = card ?? SelectedCard;
        if (target is null)
        {
            return;
        }

        var game = target.Game;
        var deleteMedia = false;

        if (_settings.Current.ConfirmBeforeRemoving)
        {
            var choice = await _dialogs.ConfirmRemoveGameAsync(game).ConfigureAwait(true);
            if (!choice.Confirmed)
            {
                return;
            }

            deleteMedia = choice.DeleteCachedMedia;
        }

        try
        {
            await _library.RemoveGameAsync(game.Id, deleteMedia).ConfigureAwait(true);

            _allGames.RemoveAll(g => g.Id == game.Id);
            SelectedCard = null;
            UpdateCategoryCounts();
            ApplyFilter();

            StatusMessage = L("Status.GameRemoved", game.Title);
            OnPropertyChanged(nameof(IsLibraryEmpty));
            OnPropertyChanged(nameof(HasGames));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Removing the game failed");
            ErrorMessage = L("Error.DatabaseError");
        }
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync(GameCardViewModel? card)
    {
        var target = card ?? SelectedCard;
        if (target is null)
        {
            return;
        }

        try
        {
            await _library.SetFavoriteAsync(target.Game, !target.Game.IsFavorite).ConfigureAwait(true);
            target.RefreshFromModel();
            Details.RaiseAll();
            UpdateCategoryCounts();

            if (SelectedCategory?.Scope == LibraryScope.Favorites)
            {
                ApplyFilter();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Toggling the favorite flag failed");
            ErrorMessage = L("Error.DatabaseError");
        }
    }

    [RelayCommand]
    private async Task ChangeCoverAsync(GameCardViewModel? card)
    {
        var target = card ?? SelectedCard;
        if (target is null)
        {
            return;
        }

        var path = await _dialogs.PickFileAsync(L("Edit.ChooseCover"), FilePickerKind.Image).ConfigureAwait(true);
        if (path is null)
        {
            return;
        }

        var stored = await _library.SetCoverFromFileAsync(target.Game, path).ConfigureAwait(true);
        if (stored is null)
        {
            ErrorMessage = L("Error.ImageLoadFailed");
            return;
        }

        target.RefreshFromModel();
        _ = target.EnsureThumbnailAsync();
        await Details.SetGameAsync(target.Game).ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task ChangeLaunchFileAsync(GameCardViewModel? card)
    {
        var target = card ?? SelectedCard;
        if (target is null)
        {
            return;
        }

        var path = await _dialogs
            .PickFileAsync(L("Edit.ChooseLaunchFile"), FilePickerKind.Executable, target.Game.GameDirectory)
            .ConfigureAwait(true);

        if (path is null)
        {
            return;
        }

        target.Game.LaunchFile = path;
        await _library.UpdateGameAsync(target.Game).ConfigureAwait(true);

        Details.RaiseAll();
        StatusMessage = L("Status.Ready");
    }

    [RelayCommand]
    private async Task OpenSettingsAsync()
    {
        var changed = await _dialogs.ShowSettingsAsync().ConfigureAwait(true);
        if (!changed)
        {
            return;
        }

        var current = _settings.Current;

        var sortOption = SortOptions.FirstOrDefault(s => s.Order == current.SortOrder);
        if (sortOption is not null)
        {
            SelectedSortOption = sortOption;
        }

        var coverOption = CoverSizes.FirstOrDefault(c => c.Size == current.CoverSize);
        if (coverOption is not null)
        {
            SelectedCoverSize = coverOption;
        }

        StatusMessage = L("Settings.Saved");
    }

    [RelayCommand]
    private async Task OpenScreenshotAsync(ScreenshotViewModel? screenshot)
    {
        if (screenshot is null)
        {
            return;
        }

        await _dialogs
            .ShowLightboxAsync(Details.ScreenshotPaths, screenshot.Index)
            .ConfigureAwait(true);
    }

    /// <summary>Default action for double-click / Enter, honouring the user preference.</summary>
    [RelayCommand]
    private async Task ActivateGameAsync(GameCardViewModel? card)
    {
        var target = card ?? SelectedCard;
        if (target is null)
        {
            return;
        }

        SelectedCard = target;

        if (_settings.Current.DoubleClickAction == DoubleClickAction.PlayGame)
        {
            await PlayAsync(target).ConfigureAwait(true);
        }
        else
        {
            ShowDetailsPane = true;
        }
    }

    // ---- window state ---------------------------------------------------------------------

    public async Task SaveWindowStateAsync(double width, double height, double x, double y, bool isMaximized)
    {
        if (!_settings.Current.RememberWindowState)
        {
            return;
        }

        var window = _settings.Current.Window;

        if (!isMaximized)
        {
            window.Width = width;
            window.Height = height;
            window.X = x;
            window.Y = y;
        }

        window.IsMaximized = isMaximized;

        await _settings.SaveCurrentAsync().ConfigureAwait(false);
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        Details.RaiseAll();
        StatusMessage = L("Status.Ready");
        OnPropertyChanged(nameof(GameCountText));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchDebounceCts?.Cancel();
            _searchDebounceCts?.Dispose();
            _searchDebounceCts = null;

            foreach (var card in Games)
            {
                card.Dispose();
            }

            foreach (var category in Categories)
            {
                category.Dispose();
            }

            Details.Dispose();
        }

        base.Dispose(disposing);
    }
}
