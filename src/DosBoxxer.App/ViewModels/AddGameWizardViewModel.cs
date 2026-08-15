using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DosBoxxer.App.Services;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Metadata;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.App.ViewModels;

/// <summary>Row in the executable list of wizard step 2.</summary>
public sealed class ExecutableCandidateViewModel : ViewModelBase
{
    public ExecutableCandidateViewModel(ExecutableCandidate candidate, ILocalizationService localization)
        : base(localization) => Candidate = candidate;

    public ExecutableCandidate Candidate { get; }

    public string FileName => Candidate.FileName;

    public string RelativePath => Candidate.RelativePath;

    public string Kind => Candidate.KindLabel;

    public bool LooksLikeUtility => Candidate.LooksLikeUtility;

    public string UtilityHint => L("Wizard.LikelyUtility");

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(UtilityHint));
    }
}

/// <summary>Row in the metadata result list of wizard step 3.</summary>
public sealed class SearchResultViewModel : ViewModelBase
{
    public SearchResultViewModel(MetadataSearchResult result, ILocalizationService localization)
        : base(localization) => Result = result;

    public MetadataSearchResult Result { get; }

    public string Title => Result.Title;

    public string Subtitle
    {
        get
        {
            var parts = new[] { Result.ReleaseYear?.ToString(Localization.CurrentCulture), Result.Publisher, Result.SystemName }
                .Where(p => !string.IsNullOrWhiteSpace(p));

            return string.Join(" • ", parts);
        }
    }

    public bool HasSubtitle => !string.IsNullOrEmpty(Subtitle);

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(Subtitle));
    }
}

/// <summary>
/// Four step "Add Game" wizard: folder → launch file → metadata → review.
/// Nothing is written to the database before the final step is confirmed.
/// </summary>
public sealed partial class AddGameWizardViewModel : ViewModelBase
{
    public const int StepCount = 4;

    private readonly IExecutableScanner _scanner;
    private readonly IGameMetadataProvider _metadataProvider;
    private readonly IGameLibraryService _library;
    private readonly IGameRepository _repository;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly IImageLoader _imageLoader;
    private readonly ILogger<AddGameWizardViewModel> _logger;

    private CancellationTokenSource? _scanCts;
    private CancellationTokenSource? _searchCts;
    private GameMetadata? _metadata;

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private string _gameFolder = string.Empty;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private ExecutableCandidateViewModel? _selectedCandidate;

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private SearchResultViewModel? _selectedResult;

    [ObservableProperty]
    private string _gameTitle = string.Empty;

    [ObservableProperty]
    private string? _validationMessage;

    [ObservableProperty]
    private string? _infoMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private Bitmap? _previewCover;

    [ObservableProperty]
    private bool _metadataSkipped;

    public AddGameWizardViewModel(
        IExecutableScanner scanner,
        IGameMetadataProvider metadataProvider,
        IGameLibraryService library,
        IGameRepository repository,
        IDialogService dialogs,
        ISettingsService settings,
        IImageLoader imageLoader,
        ILocalizationService localization,
        ILogger<AddGameWizardViewModel> logger)
        : base(localization)
    {
        _scanner = scanner;
        _metadataProvider = metadataProvider;
        _library = library;
        _repository = repository;
        _dialogs = dialogs;
        _settings = settings;
        _imageLoader = imageLoader;
        _logger = logger;
    }

    public ObservableCollection<ExecutableCandidateViewModel> Candidates { get; } = new();

    public ObservableCollection<SearchResultViewModel> SearchResults { get; } = new();

    /// <summary>Set when the wizard finished successfully; the window returns it to the caller.</summary>
    public Game? CreatedGame { get; private set; }

    /// <summary>Raised when the wizard wants the hosting window to close.</summary>
    public event EventHandler<bool>? CloseRequested;

    // ---- derived state --------------------------------------------------------------------

    public string StepIndicator => L("Wizard.StepIndicator", CurrentStep, StepCount);

    public bool IsStep1 => CurrentStep == 1;

    public bool IsStep2 => CurrentStep == 2;

    public bool IsStep3 => CurrentStep == 3;

    public bool IsStep4 => CurrentStep == 4;

    public bool CanGoBack => CurrentStep > 1 && !IsBusy;

    public bool ShowNext => CurrentStep < StepCount;

    public bool ShowSkipMetadata => CurrentStep == 3;

    public bool HasCandidates => Candidates.Count > 0;

    public bool ShowNoCandidates => !IsScanning && CurrentStep == 2 && Candidates.Count == 0;

    public bool HasSearchResults => SearchResults.Count > 0;

    public bool ShowNoResults => !IsSearching && CurrentStep == 3 && SearchResults.Count == 0 && _searchAttempted;

    public bool IsMetadataProviderConfigured => _metadataProvider.IsConfigured;

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    public bool HasInfoMessage => !string.IsNullOrEmpty(InfoMessage);

    public bool HasPreviewCover => PreviewCover is not null;

    // Review values
    public string ReviewTitle => GameTitle;

    public string ReviewLaunchFile => SelectedCandidate?.RelativePath ?? string.Empty;

    public string ReviewFolder => GameFolder;

    public string ReviewYear => _metadata?.ReleaseYear?.ToString(Localization.CurrentCulture) ?? string.Empty;

    public bool HasReviewYear => _metadata?.ReleaseYear is not null;

    public string ReviewPublisher => _metadata?.Publisher ?? string.Empty;

    public bool HasReviewPublisher => !string.IsNullOrWhiteSpace(_metadata?.Publisher);

    public string ReviewDeveloper => _metadata?.Developer ?? string.Empty;

    public bool HasReviewDeveloper => !string.IsNullOrWhiteSpace(_metadata?.Developer);

    public string ReviewGenres => _metadata is null
        ? string.Empty
        : string.Join(" • ", _metadata.Genres.Select(g => L(GenreKeys.ResourceKey(g))));

    public bool HasReviewGenres => _metadata is { Genres.Count: > 0 };

    public string ReviewMetadataState => _metadata is null ? L("Wizard.NoMetadataSelected") : string.Empty;

    public bool HasNoMetadata => _metadata is null;

    private bool _searchAttempted;

    // ---- commands -------------------------------------------------------------------------

    [RelayCommand]
    private async Task BrowseFolderAsync()
    {
        var folder = await _dialogs
            .PickFolderAsync(L("Wizard.Step1Title"), string.IsNullOrWhiteSpace(GameFolder) ? null : GameFolder)
            .ConfigureAwait(true);

        if (folder is not null)
        {
            GameFolder = folder;
        }
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        ValidationMessage = null;

        switch (CurrentStep)
        {
            case 1:
                if (string.IsNullOrWhiteSpace(GameFolder))
                {
                    ValidationMessage = L("Wizard.SelectFolderPrompt");
                    return;
                }

                if (!PathHelper.DirectoryExistsSafe(GameFolder))
                {
                    ValidationMessage = L("Wizard.FolderMissing");
                    return;
                }

                CurrentStep = 2;
                await ScanAsync().ConfigureAwait(true);
                return;

            case 2:
                if (SelectedCandidate is null)
                {
                    ValidationMessage = L("Wizard.SelectExecutablePrompt");
                    return;
                }

                if (await IsDuplicateAsync().ConfigureAwait(true))
                {
                    ValidationMessage = L("Wizard.AlreadyInLibrary");
                    return;
                }

                PrepareStep3();
                CurrentStep = 3;

                if (_settings.Current.AutoFetchMetadata && _metadataProvider.IsConfigured)
                {
                    await SearchAsync().ConfigureAwait(true);
                }

                return;

            case 3:
                await LoadSelectedMetadataAsync().ConfigureAwait(true);
                CurrentStep = 4;
                return;
        }
    }

    [RelayCommand]
    private void Back()
    {
        ValidationMessage = null;

        if (CurrentStep > 1)
        {
            CurrentStep--;
        }
    }

    [RelayCommand]
    private void SkipMetadata()
    {
        _metadata = null;
        MetadataSkipped = true;
        SelectedResult = null;
        PreviewCover = null;
        CurrentStep = 4;
        RaiseReview();
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchTerm))
        {
            return;
        }

        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();

        IsSearching = true;
        InfoMessage = null;
        ValidationMessage = null;
        _searchAttempted = true;

        SearchResults.Clear();
        OnPropertyChanged(nameof(HasSearchResults));

        try
        {
            var result = await _metadataProvider
                .SearchAsync(SearchTerm.Trim(), _searchCts.Token)
                .ConfigureAwait(true);

            if (!result.IsSuccess || result.Value is null)
            {
                InfoMessage = DescribeMetadataError(result.Error);
                return;
            }

            foreach (var hit in result.Value)
            {
                SearchResults.Add(new SearchResultViewModel(hit, Localization));
            }

            SelectedResult = SearchResults.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            // A newer search replaced this one.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metadata search failed");
            InfoMessage = L("Error.Unexpected");
        }
        finally
        {
            IsSearching = false;
            OnPropertyChanged(nameof(HasSearchResults));
            OnPropertyChanged(nameof(ShowNoResults));
        }
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        if (SelectedCandidate is null || string.IsNullOrWhiteSpace(GameFolder))
        {
            return;
        }

        IsBusy = true;
        InfoMessage = L("Wizard.Adding");

        try
        {
            var request = new AddGameRequest
            {
                GameDirectory = GameFolder,
                LaunchFile = SelectedCandidate.Candidate.FullPath,
                Title = string.IsNullOrWhiteSpace(GameTitle)
                    ? Path.GetFileName(PathHelper.Normalize(GameFolder))
                    : GameTitle.Trim(),
                Metadata = _metadata,
            };

            var progress = new Progress<string>(_ => InfoMessage = L("Wizard.DownloadingMedia"));

            CreatedGame = await _library.AddGameAsync(request, progress).ConfigureAwait(true);

            CloseRequested?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Adding the game failed");
            ValidationMessage = L("Error.DatabaseError");
        }
        finally
        {
            IsBusy = false;
            InfoMessage = null;
        }
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    // ---- helpers --------------------------------------------------------------------------

    private async Task<bool> IsDuplicateAsync()
    {
        if (SelectedCandidate is null)
        {
            return false;
        }

        var existing = await _repository
            .FindByLaunchFileAsync(PathHelper.Normalize(SelectedCandidate.Candidate.FullPath))
            .ConfigureAwait(true);

        return existing is not null;
    }

    private async Task ScanAsync()
    {
        _scanCts?.Cancel();
        _scanCts?.Dispose();
        _scanCts = new CancellationTokenSource();

        IsScanning = true;
        Candidates.Clear();
        SelectedCandidate = null;
        OnPropertyChanged(nameof(HasCandidates));

        try
        {
            var results = await _scanner.ScanAsync(GameFolder, _scanCts.Token).ConfigureAwait(true);

            foreach (var candidate in results)
            {
                Candidates.Add(new ExecutableCandidateViewModel(candidate, Localization));
            }

            SelectedCandidate = Candidates.FirstOrDefault(c => !c.LooksLikeUtility) ?? Candidates.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer scan.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Scanning the game folder failed");
            ValidationMessage = L("Error.Unexpected");
        }
        finally
        {
            IsScanning = false;
            OnPropertyChanged(nameof(HasCandidates));
            OnPropertyChanged(nameof(ShowNoCandidates));
        }
    }

    private void PrepareStep3()
    {
        if (!string.IsNullOrWhiteSpace(SearchTerm))
        {
            return;
        }

        var fromFolder = TitleCleaner.FromDirectoryName(Path.GetFileName(PathHelper.Normalize(GameFolder)));

        if (string.IsNullOrWhiteSpace(fromFolder) && SelectedCandidate is not null)
        {
            fromFolder = TitleCleaner.FromDirectoryName(SelectedCandidate.FileName);
        }

        SearchTerm = fromFolder;
        GameTitle = fromFolder;
    }

    private async Task LoadSelectedMetadataAsync()
    {
        if (SelectedResult is null)
        {
            _metadata = null;
            RaiseReview();
            return;
        }

        IsBusy = true;
        InfoMessage = L("Status.SearchingMetadata");

        try
        {
            var result = await _metadataProvider
                .GetGameAsync(SelectedResult.Result.ProviderGameId, _settings.Current.Language)
                .ConfigureAwait(true);

            if (!result.IsSuccess || result.Value is null)
            {
                InfoMessage = DescribeMetadataError(result.Error);
                _metadata = null;
            }
            else
            {
                _metadata = result.Value;
                MetadataSkipped = false;

                if (!string.IsNullOrWhiteSpace(_metadata.Title))
                {
                    GameTitle = _metadata.Title;
                }

                InfoMessage = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Loading metadata failed");
            InfoMessage = L("Error.Unexpected");
            _metadata = null;
        }
        finally
        {
            IsBusy = false;
            RaiseReview();
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
        _ => L("Error.Unexpected"),
    };

    private void RaiseReview()
    {
        OnPropertyChanged(nameof(ReviewTitle));
        OnPropertyChanged(nameof(ReviewLaunchFile));
        OnPropertyChanged(nameof(ReviewFolder));
        OnPropertyChanged(nameof(ReviewYear));
        OnPropertyChanged(nameof(HasReviewYear));
        OnPropertyChanged(nameof(ReviewPublisher));
        OnPropertyChanged(nameof(HasReviewPublisher));
        OnPropertyChanged(nameof(ReviewDeveloper));
        OnPropertyChanged(nameof(HasReviewDeveloper));
        OnPropertyChanged(nameof(ReviewGenres));
        OnPropertyChanged(nameof(HasReviewGenres));
        OnPropertyChanged(nameof(ReviewMetadataState));
        OnPropertyChanged(nameof(HasNoMetadata));
    }

    partial void OnCurrentStepChanged(int value)
    {
        OnPropertyChanged(nameof(StepIndicator));
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep4));
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(ShowNext));
        OnPropertyChanged(nameof(ShowSkipMetadata));
        OnPropertyChanged(nameof(ShowNoCandidates));
        OnPropertyChanged(nameof(ShowNoResults));
    }

    partial void OnGameTitleChanged(string value) => OnPropertyChanged(nameof(ReviewTitle));

    partial void OnValidationMessageChanged(string? value) => OnPropertyChanged(nameof(HasValidationMessage));

    partial void OnInfoMessageChanged(string? value) => OnPropertyChanged(nameof(HasInfoMessage));

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(CanGoBack));

    partial void OnPreviewCoverChanged(Bitmap? value) => OnPropertyChanged(nameof(HasPreviewCover));

    partial void OnSelectedCandidateChanged(ExecutableCandidateViewModel? value) =>
        OnPropertyChanged(nameof(ReviewLaunchFile));

    partial void OnIsScanningChanged(bool value) => OnPropertyChanged(nameof(ShowNoCandidates));

    partial void OnIsSearchingChanged(bool value) => OnPropertyChanged(nameof(ShowNoResults));

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        RaiseReview();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _scanCts?.Cancel();
            _scanCts?.Dispose();
            _searchCts?.Cancel();
            _searchCts?.Dispose();

            foreach (var candidate in Candidates)
            {
                candidate.Dispose();
            }

            foreach (var result in SearchResults)
            {
                result.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
