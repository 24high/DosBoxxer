using System;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DosBoxxer.Core.Abstractions;

namespace DosBoxxer.App.ViewModels;

public sealed class ChoiceOption
{
    public required int Index { get; init; }

    public required string Label { get; init; }

    public bool IsPrimary { get; init; }
}

/// <summary>
/// A small dialog offering up to a handful of labelled choices (e.g. Retry / Start anyway /
/// Cancel). The chosen index is read back after the window closes; -1 means cancelled.
/// </summary>
public sealed partial class ChoiceDialogViewModel : ViewModelBase
{
    public ChoiceDialogViewModel(ILocalizationService localization)
        : base(localization)
    {
    }

    public string DialogTitle { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public bool IsError { get; set; }

    public List<ChoiceOption> Options { get; } = new();

    public int SelectedIndex { get; private set; } = -1;

    public event EventHandler<bool>? CloseRequested;

    [RelayCommand]
    private void Choose(int index)
    {
        SelectedIndex = index;
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        SelectedIndex = -1;
        CloseRequested?.Invoke(this, false);
    }
}
