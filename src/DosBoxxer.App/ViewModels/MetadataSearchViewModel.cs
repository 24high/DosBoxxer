using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models.Metadata;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.App.ViewModels;

/// <summary>Standalone provider search, used when linking an existing game to a new entry.</summary>
public sealed partial class MetadataSearchViewModel : ViewModelBase
{
    private readonly IGameMetadataProvider _provider;
    private readonly ILogger<MetadataSearchViewModel> _logger;

    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _searchTerm;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private SearchResultViewModel? _selectedResult;

    [ObservableProperty]
    private string? _infoMessage;

    public MetadataSearchViewModel(
        string initialSearchTerm,
        IGameMetadataProvider provider,
        ILocalizationService localization,
        ILogger<MetadataSearchViewModel> logger)
        : base(localization)
    {
        _provider = provider;
        _logger = logger;
        _searchTerm = initialSearchTerm;
    }

    public ObservableCollection<SearchResultViewModel> Results { get; } = new();

    public bool HasResults => Results.Count > 0;

    public bool HasInfoMessage => !string.IsNullOrEmpty(InfoMessage);

    public MetadataSearchResult? Selection { get; private set; }

    public event EventHandler<bool>? CloseRequested;

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

        foreach (var result in Results)
        {
            result.Dispose();
        }

        Results.Clear();
        OnPropertyChanged(nameof(HasResults));

        try
        {
            var result = await _provider.SearchAsync(SearchTerm.Trim(), _searchCts.Token).ConfigureAwait(true);

            if (!result.IsSuccess || result.Value is null)
            {
                InfoMessage = result.Error switch
                {
                    MetadataErrorKind.NotConfigured => L("Error.NotConfigured"),
                    MetadataErrorKind.InvalidCredentials => L("Error.InvalidApiKey"),
                    MetadataErrorKind.RateLimited => L("Error.RateLimited"),
                    MetadataErrorKind.NoResults => L("Error.NoSearchResults"),
                    MetadataErrorKind.NetworkUnavailable => L("Error.NoInternet"),
                    MetadataErrorKind.ProviderUnavailable => L("Error.ProviderUnreachable"),
                    _ => L("Error.Unexpected"),
                };

                return;
            }

            foreach (var hit in result.Value)
            {
                Results.Add(new SearchResultViewModel(hit, Localization));
            }

            SelectedResult = Results.FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            // Replaced by a newer search.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Metadata search failed");
            InfoMessage = L("Error.Unexpected");
        }
        finally
        {
            IsSearching = false;
            OnPropertyChanged(nameof(HasResults));
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        Selection = SelectedResult?.Result;
        CloseRequested?.Invoke(this, Selection is not null);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    partial void OnInfoMessageChanged(string? value) => OnPropertyChanged(nameof(HasInfoMessage));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();

            foreach (var result in Results)
            {
                result.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
