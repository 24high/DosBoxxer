using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DosBoxxer.App.Services;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.App.ViewModels;

/// <summary>
/// Edit dialog with General / Metadata / Media / DOSBox / Advanced sections.
///
/// It works on a copy of the game and only writes back when the user saves. Every field the
/// user actually changes is recorded in <see cref="Game.ManualFields"/> so a later metadata
/// refresh will not silently overwrite it.
/// </summary>
public sealed partial class EditGameViewModel : ViewModelBase
{
    private readonly Game _original;
    private readonly IGameLibraryService _library;
    private readonly IDialogService _dialogs;
    private readonly IImageLoader _imageLoader;
    private readonly IDosBoxConfigBuilder _configBuilder;
    private readonly ISettingsService _settings;
    private readonly IAppPaths _paths;
    private readonly ILogger<EditGameViewModel> _logger;

    private readonly string _initialTitle;
    private readonly string? _initialPublisher;
    private readonly string? _initialDeveloper;
    private readonly int? _initialYear;
    private readonly string? _initialDescription;
    private readonly string? _initialPlayers;
    private readonly GenreKey[] _initialGenres;

    [ObservableProperty]
    private string _title;

    [ObservableProperty]
    private string _sortTitle;

    [ObservableProperty]
    private string _gameDirectory;

    [ObservableProperty]
    private string _launchFile;

    [ObservableProperty]
    private string _publisher;

    [ObservableProperty]
    private string _developer;

    [ObservableProperty]
    private string _releaseYear;

    [ObservableProperty]
    private string _description;

    [ObservableProperty]
    private string _players;

    [ObservableProperty]
    private string _platform;

    [ObservableProperty]
    private string _screenScraperId;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private Bitmap? _coverPreview;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private string? _configPreview;

    [ObservableProperty]
    private ScreenshotViewModel? _selectedScreenshot;

    // -- DOSBox overrides ------------------------------------------------------------------
    [ObservableProperty]
    private string _cycles;

    [ObservableProperty]
    private string _core;

    [ObservableProperty]
    private string _machine;

    [ObservableProperty]
    private string _memSize;

    [ObservableProperty]
    private string _scaler;

    [ObservableProperty]
    private string _output;

    [ObservableProperty]
    private string _mixerRate;

    [ObservableProperty]
    private string _soundBlasterType;

    [ObservableProperty]
    private bool? _aspect;

    [ObservableProperty]
    private bool? _fullscreen;

    [ObservableProperty]
    private bool? _pcSpeaker;

    [ObservableProperty]
    private string _additionalConfigLines;

    [ObservableProperty]
    private string _preLaunchCommands;

    public EditGameViewModel(
        Game game,
        IGameLibraryService library,
        IDialogService dialogs,
        IImageLoader imageLoader,
        IDosBoxConfigBuilder configBuilder,
        ISettingsService settings,
        IAppPaths paths,
        ILocalizationService localization,
        ILogger<EditGameViewModel> logger)
        : base(localization)
    {
        _original = game;
        _library = library;
        _dialogs = dialogs;
        _imageLoader = imageLoader;
        _configBuilder = configBuilder;
        _settings = settings;
        _paths = paths;
        _logger = logger;

        _title = game.Title;
        _sortTitle = game.SortTitle;
        _gameDirectory = game.GameDirectory;
        _launchFile = game.LaunchFile;
        _publisher = game.Publisher ?? string.Empty;
        _developer = game.Developer ?? string.Empty;
        _releaseYear = game.ReleaseYear?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;
        _description = game.Description ?? string.Empty;
        _players = game.Players ?? string.Empty;
        _platform = game.Platform;
        _screenScraperId = game.ScreenScraperId ?? string.Empty;
        _isFavorite = game.IsFavorite;

        var dosBox = game.DosBoxSettings;
        _cycles = dosBox.Cycles ?? string.Empty;
        _core = dosBox.Core ?? string.Empty;
        _machine = dosBox.Machine ?? string.Empty;
        _memSize = dosBox.MemSize ?? string.Empty;
        _scaler = dosBox.Scaler ?? string.Empty;
        _output = dosBox.Output ?? string.Empty;
        _mixerRate = dosBox.MixerRate ?? string.Empty;
        _soundBlasterType = dosBox.SoundBlasterType ?? string.Empty;
        _aspect = dosBox.Aspect;
        _fullscreen = dosBox.Fullscreen;
        _pcSpeaker = dosBox.PcSpeaker;
        _additionalConfigLines = dosBox.AdditionalConfigLines ?? string.Empty;
        _preLaunchCommands = dosBox.PreLaunchCommands ?? string.Empty;

        _initialTitle = game.Title;
        _initialPublisher = game.Publisher;
        _initialDeveloper = game.Developer;
        _initialYear = game.ReleaseYear;
        _initialDescription = game.Description;
        _initialPlayers = game.Players;
        _initialGenres = game.Genres.ToArray();

        foreach (var genre in GenreKeys.All)
        {
            Genres.Add(new GenreSelectionViewModel(genre, game.Genres.Contains(genre), localization));
        }

        var total = game.Screenshots.Count;
        for (var i = 0; i < total; i++)
        {
            Screenshots.Add(new ScreenshotViewModel(game.Screenshots[i].LocalPath, i, total, imageLoader, localization));
        }
    }

    public ObservableCollection<GenreSelectionViewModel> Genres { get; } = new();

    public ObservableCollection<ScreenshotViewModel> Screenshots { get; } = new();

    public bool HasCoverPreview => CoverPreview is not null;

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    public bool HasConfigPreview => !string.IsNullOrEmpty(ConfigPreview);

    public string ProtectedFieldsText
    {
        get
        {
            var fields = GameFields.Individual
                .Where(f => _original.ManualFields.HasFlag(f))
                .Select(f => L(GameFields.ResourceKey(f)))
                .ToList();

            return fields.Count == 0 ? string.Empty : L("Edit.ProtectedFields", string.Join(", ", fields));
        }
    }

    public bool HasProtectedFields => !string.IsNullOrEmpty(ProtectedFieldsText);

    public event EventHandler<bool>? CloseRequested;

    public async Task InitializeAsync()
    {
        CoverPreview = await _imageLoader.LoadThumbnailAsync(_original.CoverImagePath, 400).ConfigureAwait(true);

        foreach (var screenshot in Screenshots)
        {
            await screenshot.LoadAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task BrowseGameDirectoryAsync()
    {
        var folder = await _dialogs.PickFolderAsync(L("Edit.ChooseGameFolder"), GameDirectory).ConfigureAwait(true);
        if (folder is not null)
        {
            GameDirectory = folder;
        }
    }

    [RelayCommand]
    private async Task BrowseLaunchFileAsync()
    {
        var file = await _dialogs
            .PickFileAsync(L("Edit.ChooseLaunchFile"), FilePickerKind.Executable, GameDirectory)
            .ConfigureAwait(true);

        if (file is not null)
        {
            LaunchFile = file;
        }
    }

    [RelayCommand]
    private async Task ChooseCoverAsync()
    {
        var file = await _dialogs.PickFileAsync(L("Edit.ChooseCover"), FilePickerKind.Image).ConfigureAwait(true);
        if (file is null)
        {
            return;
        }

        var stored = await _library.SetCoverFromFileAsync(_original, file).ConfigureAwait(true);
        if (stored is null)
        {
            ValidationMessage = L("Error.ImageLoadFailed");
            return;
        }

        CoverPreview = await _imageLoader.LoadThumbnailAsync(stored, 400).ConfigureAwait(true);
    }

    [RelayCommand]
    private void ClearCover()
    {
        _original.CoverImagePath = null;
        _original.ManualFields |= GameField.Cover;
        CoverPreview = null;
    }

    [RelayCommand]
    private async Task AddScreenshotAsync()
    {
        var file = await _dialogs.PickFileAsync(L("Edit.AddScreenshot"), FilePickerKind.Image).ConfigureAwait(true);
        if (file is null)
        {
            return;
        }

        var screenshot = await _library.AddScreenshotFromFileAsync(_original, file).ConfigureAwait(true);
        if (screenshot is null)
        {
            ValidationMessage = L("Error.ImageLoadFailed");
            return;
        }

        var vm = new ScreenshotViewModel(screenshot.LocalPath, Screenshots.Count, Screenshots.Count + 1, _imageLoader, Localization);
        Screenshots.Add(vm);
        await vm.LoadAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void RemoveScreenshot()
    {
        if (SelectedScreenshot is null)
        {
            return;
        }

        var path = SelectedScreenshot.LocalPath;
        var existing = _original.Screenshots.FirstOrDefault(s =>
            string.Equals(s.LocalPath, path, StringComparison.Ordinal));

        if (existing is not null)
        {
            _original.Screenshots.Remove(existing);
            _original.ManualFields |= GameField.Screenshots;
        }

        SelectedScreenshot.Dispose();
        Screenshots.Remove(SelectedScreenshot);
        SelectedScreenshot = null;
    }

    [RelayCommand]
    private async Task PreviewConfigAsync()
    {
        try
        {
            var request = new DosBoxConfigRequest
            {
                BaseConfigPath = _settings.Current.BaseDosBoxConfigPath,
                GameDirectory = GameDirectory,
                LaunchFile = LaunchFile,
                Overrides = BuildDosBoxSettings(),
                AppendExit = _settings.Current.CloseDosBoxAfterExit,
                OutputPath = System.IO.Path.Combine(_paths.TempDirectory, "preview.conf"),
            };

            ConfigPreview = await _configBuilder.RenderAsync(request).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Rendering the configuration preview failed");
            ValidationMessage = L("Error.ConfigWriteFailed");
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        ValidationMessage = null;

        if (string.IsNullOrWhiteSpace(Title))
        {
            ValidationMessage = L("Wizard.GameTitle");
            return;
        }

        if (!PathHelper.DirectoryExistsSafe(GameDirectory))
        {
            ValidationMessage = L("Error.GameDirectoryMissing");
            return;
        }

        if (!PathHelper.FileExistsSafe(LaunchFile))
        {
            ValidationMessage = L("Error.LaunchFileMissing");
            return;
        }

        int? year = null;
        if (!string.IsNullOrWhiteSpace(ReleaseYear))
        {
            if (!int.TryParse(ReleaseYear, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
                parsed < 1950 || parsed > 2100)
            {
                ValidationMessage = L("Edit.ReleaseYear");
                return;
            }

            year = parsed;
        }

        var selectedGenres = Genres.Where(g => g.IsSelected).Select(g => g.Genre).ToList();

        // Record which fields the user actually changed, so refreshes respect them.
        var manual = _original.ManualFields;

        if (!string.Equals(Title.Trim(), _initialTitle, StringComparison.Ordinal))
        {
            manual |= GameField.Title;
        }

        if (!string.Equals(NullIfEmpty(Publisher), _initialPublisher, StringComparison.Ordinal))
        {
            manual |= GameField.Publisher;
        }

        if (!string.Equals(NullIfEmpty(Developer), _initialDeveloper, StringComparison.Ordinal))
        {
            manual |= GameField.Developer;
        }

        if (year != _initialYear)
        {
            manual |= GameField.ReleaseYear;
        }

        if (!string.Equals(NullIfEmpty(Description), _initialDescription, StringComparison.Ordinal))
        {
            manual |= GameField.Description;
        }

        if (!string.Equals(NullIfEmpty(Players), _initialPlayers, StringComparison.Ordinal))
        {
            manual |= GameField.Players;
        }

        if (!selectedGenres.OrderBy(g => g).SequenceEqual(_initialGenres.OrderBy(g => g)))
        {
            manual |= GameField.Genres;
        }

        _original.Title = Title.Trim();
        _original.SortTitle = string.IsNullOrWhiteSpace(SortTitle)
            ? TitleCleaner.ToSortTitle(_original.Title)
            : SortTitle.Trim().ToUpperInvariant();
        _original.GameDirectory = GameDirectory;
        _original.LaunchFile = LaunchFile;
        _original.Publisher = NullIfEmpty(Publisher);
        _original.Developer = NullIfEmpty(Developer);
        _original.ReleaseYear = year;
        _original.Description = NullIfEmpty(Description);
        _original.Players = NullIfEmpty(Players);
        _original.Platform = string.IsNullOrWhiteSpace(Platform) ? "PC (DOS)" : Platform.Trim();
        _original.ScreenScraperId = NullIfEmpty(ScreenScraperId);
        _original.IsFavorite = IsFavorite;
        _original.ManualFields = manual;

        _original.Genres.Clear();
        _original.Genres.AddRange(selectedGenres);

        _original.DosBoxSettings = BuildDosBoxSettings();
        _original.DosBoxSettings.GameId = _original.Id;

        try
        {
            await _library.UpdateGameAsync(_original).ConfigureAwait(true);
            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Saving the game failed");
            ValidationMessage = L("Error.DatabaseError");
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    private GameDosBoxSettings BuildDosBoxSettings() => new()
    {
        GameId = _original.Id,
        Cycles = NullIfEmpty(Cycles),
        Core = NullIfEmpty(Core),
        Machine = NullIfEmpty(Machine),
        MemSize = NullIfEmpty(MemSize),
        Scaler = NullIfEmpty(Scaler),
        Output = NullIfEmpty(Output),
        MixerRate = NullIfEmpty(MixerRate),
        SoundBlasterType = NullIfEmpty(SoundBlasterType),
        Aspect = Aspect,
        Fullscreen = Fullscreen,
        PcSpeaker = PcSpeaker,
        AdditionalConfigLines = NullIfEmpty(AdditionalConfigLines),
        PreLaunchCommands = NullIfEmpty(PreLaunchCommands),
    };

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    partial void OnCoverPreviewChanged(Bitmap? value) => OnPropertyChanged(nameof(HasCoverPreview));

    partial void OnValidationMessageChanged(string? value) => OnPropertyChanged(nameof(HasValidationMessage));

    partial void OnConfigPreviewChanged(string? value) => OnPropertyChanged(nameof(HasConfigPreview));

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(ProtectedFieldsText));
        OnPropertyChanged(nameof(HasProtectedFields));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var genre in Genres)
            {
                genre.Dispose();
            }

            foreach (var screenshot in Screenshots)
            {
                screenshot.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
