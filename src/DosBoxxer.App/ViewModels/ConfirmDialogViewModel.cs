using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DosBoxxer.Core.Abstractions;

namespace DosBoxxer.App.ViewModels;

/// <summary>
/// Shared dialog for messages, confirmations and the "remove game" prompt with its optional
/// "also delete cached media" checkbox.
/// </summary>
public sealed partial class ConfirmDialogViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _extraOptionValue;

    public ConfirmDialogViewModel(ILocalizationService localization)
        : base(localization)
    {
        ConfirmText = L("Common.Ok");
        CancelText = L("Common.Cancel");
    }

    public string DialogTitle { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public string? Hint { get; set; }

    public bool HasHint => !string.IsNullOrEmpty(Hint);

    public bool ShowCancel { get; set; } = true;

    public bool IsError { get; set; }

    public string? ExtraOptionText { get; set; }

    public bool HasExtraOption => !string.IsNullOrEmpty(ExtraOptionText);

    public string ConfirmText { get; set; }

    public string CancelText { get; set; }

    public event EventHandler<bool>? CloseRequested;

    [RelayCommand]
    private void Confirm() => CloseRequested?.Invoke(this, true);

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(this, false);
}
