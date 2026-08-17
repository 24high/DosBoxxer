using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models.Cloud;

namespace DosBoxxer.App.ViewModels;

public sealed class ResolutionOption
{
    public required ConflictResolution Value { get; init; }

    public required string Label { get; init; }
}

/// <summary>One conflicting file with its metadata and the chosen resolution.</summary>
public sealed partial class ConflictRowViewModel : ViewModelBase
{
    [ObservableProperty]
    private ResolutionOption _selectedOption;

    public ConflictRowViewModel(SyncPlanItem item, IReadOnlyList<ResolutionOption> options, ILocalizationService localization)
        : base(localization)
    {
        Item = item;
        Options = options;

        // Sensible default: prefer whichever side is newer, so the common case is one click.
        var localNewer = (item.LocalModifiedUtc ?? DateTimeOffset.MinValue) >= (item.RemoteModifiedUtc ?? DateTimeOffset.MinValue);
        _selectedOption = options.First(o => o.Value == (localNewer ? ConflictResolution.UseLocal : ConflictResolution.UseCloud));
    }

    public SyncPlanItem Item { get; }

    public IReadOnlyList<ResolutionOption> Options { get; }

    public string RelativePath => Item.RelativePath;

    public string LocalModified => Format(Item.LocalModifiedUtc);

    public string CloudModified => Format(Item.RemoteModifiedUtc);

    public string LocalSize => FormatSize(Item.LocalSize);

    public string CloudSize => FormatSize(Item.RemoteSize);

    public ConflictResolution Resolution => SelectedOption.Value;

    public void Select(ConflictResolution resolution) =>
        SelectedOption = Options.First(o => o.Value == resolution);

    private static string Format(DateTimeOffset? value) =>
        value.HasValue ? value.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "—";

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0)
        {
            return "—";
        }

        string[] units = { "B", "KB", "MB", "GB" };
        double size = bytes;
        var unit = 0;
        while (size >= 1024 && unit < units.Length - 1)
        {
            size /= 1024;
            unit++;
        }

        return $"{size:0.#} {units[unit]}";
    }
}

/// <summary>
/// Lets the user resolve savegame conflicts before a sync writes anything. Each row can be set to
/// keep the local or the cloud copy (or skip it); quick actions apply one choice to every conflict.
/// </summary>
public sealed partial class SyncConflictViewModel : ViewModelBase
{
    public SyncConflictViewModel(IReadOnlyList<SyncPlanItem> conflicts, ILocalizationService localization)
        : base(localization)
    {
        var options = new[]
        {
            new ResolutionOption { Value = ConflictResolution.UseLocal, Label = L("Conflict.UseLocal") },
            new ResolutionOption { Value = ConflictResolution.UseCloud, Label = L("Conflict.UseCloud") },
            new ResolutionOption { Value = ConflictResolution.Skip, Label = L("Conflict.Skip") },
        };

        foreach (var conflict in conflicts)
        {
            Rows.Add(new ConflictRowViewModel(conflict, options, localization));
        }
    }

    public ObservableCollection<ConflictRowViewModel> Rows { get; } = new();

    /// <summary>Set when the user applied a decision; <c>null</c> when the sync was cancelled.</summary>
    public ConflictDecision? Result { get; private set; }

    public event EventHandler<bool>? CloseRequested;

    [RelayCommand]
    private void UseLocalForAll()
    {
        foreach (var row in Rows)
        {
            row.Select(ConflictResolution.UseLocal);
        }
    }

    [RelayCommand]
    private void UseCloudForAll()
    {
        foreach (var row in Rows)
        {
            row.Select(ConflictResolution.UseCloud);
        }
    }

    [RelayCommand]
    private void Apply()
    {
        var map = new Dictionary<string, ConflictResolution>(StringComparer.Ordinal);
        foreach (var row in Rows)
        {
            map[row.RelativePath] = row.Resolution;
        }

        Result = new ConflictDecision { PerFile = map };
        CloseRequested?.Invoke(this, true);
    }

    [RelayCommand]
    private void Cancel()
    {
        Result = null;
        CloseRequested?.Invoke(this, false);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var row in Rows)
            {
                row.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
