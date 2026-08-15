using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DosBoxxer.App.Services;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models;

namespace DosBoxxer.App.ViewModels;

/// <summary>
/// Lets the user choose which fields a metadata refresh may touch and whether manual edits are
/// protected. This is the "field selection" merge strategy required before overwriting data.
/// </summary>
public sealed partial class RefreshMetadataViewModel : ViewModelBase
{
    private readonly Game _game;
    private readonly IDialogService _dialogs;

    [ObservableProperty]
    private bool _overwriteManualEdits;

    [ObservableProperty]
    private string _providerGameId;

    [ObservableProperty]
    private string? _validationMessage;

    public RefreshMetadataViewModel(Game game, IDialogService dialogs, ILocalizationService localization)
        : base(localization)
    {
        _game = game;
        _dialogs = dialogs;
        _providerGameId = game.ScreenScraperId ?? string.Empty;

        foreach (var field in GameFields.Individual)
        {
            Fields.Add(new FieldSelectionViewModel(field, game.ManualFields.HasFlag(field), localization));
        }
    }

    public ObservableCollection<FieldSelectionViewModel> Fields { get; } = new();

    public string Message => L("Refresh.Message", _game.Title);

    public bool HasProviderId => !string.IsNullOrWhiteSpace(ProviderGameId);

    public bool HasValidationMessage => !string.IsNullOrEmpty(ValidationMessage);

    public RefreshMetadataChoice? Result { get; private set; }

    public event EventHandler<bool>? CloseRequested;

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var field in Fields)
        {
            field.IsSelected = true;
        }
    }

    [RelayCommand]
    private async Task SearchAgainAsync()
    {
        var searchTerm = string.IsNullOrWhiteSpace(_game.OriginalTitle) ? _game.Title : _game.OriginalTitle;
        var hit = await _dialogs.ShowMetadataSearchAsync(searchTerm).ConfigureAwait(true);

        if (hit is not null)
        {
            ProviderGameId = hit.ProviderGameId;
            ValidationMessage = null;
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        if (string.IsNullOrWhiteSpace(ProviderGameId))
        {
            ValidationMessage = L("Refresh.NoProviderId");
            return;
        }

        var selected = Fields.Where(f => f.IsSelected).Aggregate(GameField.None, (acc, f) => acc | f.Field);

        if (selected == GameField.None)
        {
            ValidationMessage = L("Refresh.NothingChanged");
            return;
        }

        Result = new RefreshMetadataChoice
        {
            Fields = selected,
            OverwriteManualEdits = OverwriteManualEdits,
            ProviderGameId = ProviderGameId.Trim(),
        };

        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);

    partial void OnProviderGameIdChanged(string value) => OnPropertyChanged(nameof(HasProviderId));

    partial void OnValidationMessageChanged(string? value) => OnPropertyChanged(nameof(HasValidationMessage));

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(Message));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var field in Fields)
            {
                field.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
