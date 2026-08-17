using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Infrastructure.Savegame;
using DosBoxxer.Core.Models.Savegame;

namespace DosBoxxer.App.ViewModels;

/// <summary>A ranked catalog suggestion row.</summary>
public sealed class SavegameSuggestionViewModel : ViewModelBase
{
    public SavegameSuggestionViewModel(TitleMatch match, ILocalizationService localization)
        : base(localization) => Match = match;

    public TitleMatch Match { get; }

    public string Title => Match.Title;

    public string Pattern => Match.Catalog.Entry.RelativePattern;

    public string ConfidenceLabel => Match.Confidence switch
    {
        MatchConfidence.VeryLikely => L("Savegame.ConfidenceVeryLikely"),
        MatchConfidence.Likely => L("Savegame.ConfidenceLikely"),
        MatchConfidence.Similar => L("Savegame.ConfidenceSimilar"),
        _ => L("Savegame.ConfidenceWeak"),
    };

    public string ScoreText => (Match.Score * 100).ToString("0", CultureInfo.CurrentCulture) + "%";
}

/// <summary>One editable savegame path/pattern row.</summary>
public sealed partial class SavegameEntryRowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string _pattern;

    [ObservableProperty]
    private bool _isGlob;

    public SavegameEntryRowViewModel(SavegameEntry entry, ILocalizationService localization)
        : base(localization)
    {
        _pattern = entry.RelativePattern;
        _isGlob = entry.Kind == SavegameEntryKind.Glob;
    }

    public bool IsValid => SavegamePathParser.NormalizeRelative(Pattern) is not null;

    public bool IsInvalid => !string.IsNullOrWhiteSpace(Pattern) && !IsValid;

    public SavegameEntry? ToEntry()
    {
        var normalized = SavegamePathParser.NormalizeRelative(Pattern);
        if (normalized is null)
        {
            return null;
        }

        var isGlob = IsGlob || normalized.Contains('*') || normalized.Contains('?');
        return new SavegameEntry(normalized, isGlob ? SavegameEntryKind.Glob : SavegameEntryKind.Path);
    }

    partial void OnPatternChanged(string value)
    {
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(IsInvalid));
    }
}

/// <summary>
/// Reusable editor for a game's savegame configuration, shared by the Add-Game wizard and the Edit
/// dialog. It offers catalog suggestions (phonetic search), a "no match / manual" path and manual
/// entry editing, and produces a <see cref="SavegameConfig"/>.
/// </summary>
public sealed partial class SavegameConfigEditorViewModel : ViewModelBase
{
    private readonly ISavegameCatalog _catalog;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private string _searchTerm = string.Empty;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private SavegameSuggestionViewModel? _selectedSuggestion;

    [ObservableProperty]
    private string? _matchedTitle;

    [ObservableProperty]
    private bool _skipSavegames;

    public SavegameConfigEditorViewModel(ISavegameCatalog catalog, ILocalizationService localization)
        : base(localization)
    {
        _catalog = catalog;
        Entries.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasEntries));
    }

    public ObservableCollection<SavegameSuggestionViewModel> Suggestions { get; } = new();

    public ObservableCollection<SavegameEntryRowViewModel> Entries { get; } = new();

    public bool HasEntries => Entries.Count > 0;

    public bool HasSuggestions => Suggestions.Count > 0;

    public bool HasMatchedTitle => !string.IsNullOrEmpty(MatchedTitle);

    /// <summary>Set true once the user has manually edited the entries.</summary>
    public bool IsManual { get; private set; }

    /// <summary>Initialises the editor. Runs an initial catalog search for a fresh game.</summary>
    public async Task PrepareAsync(string initialTitle, SavegameConfig? existing)
    {
        if (existing is not null && (existing.IsConfigured || existing.Entries.Count > 0))
        {
            MatchedTitle = existing.MatchedCatalogTitle;
            IsManual = existing.IsManual;
            SkipSavegames = existing is { IsConfigured: false, Entries.Count: 0 };

            foreach (var entry in existing.Entries)
            {
                Entries.Add(new SavegameEntryRowViewModel(entry, Localization));
            }
        }

        SearchTerm = string.IsNullOrWhiteSpace(existing?.MatchedCatalogTitle) ? initialTitle : existing.MatchedCatalogTitle!;

        if (Entries.Count == 0 && !string.IsNullOrWhiteSpace(SearchTerm))
        {
            await SearchAsync().ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        if (string.IsNullOrWhiteSpace(SearchTerm))
        {
            return;
        }

        IsSearching = true;
        try
        {
            var term = SearchTerm.Trim();
            // Catalog search is CPU-bound; run it off the UI thread.
            var matches = await Task.Run(() => _catalog.FindMatches(term, 8), token).ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            Suggestions.Clear();
            foreach (var match in matches)
            {
                Suggestions.Add(new SavegameSuggestionViewModel(match, Localization));
            }

            OnPropertyChanged(nameof(HasSuggestions));
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>Applies the currently selected suggestion's paths to the configuration.</summary>
    [RelayCommand]
    private void ApplySuggestion(SavegameSuggestionViewModel? suggestion)
    {
        var chosen = suggestion ?? SelectedSuggestion;
        if (chosen is null)
        {
            return;
        }

        SkipSavegames = false;
        MatchedTitle = chosen.Title;
        IsManual = false;

        Entries.Clear();
        Entries.Add(new SavegameEntryRowViewModel(chosen.Match.Catalog.Entry.Clone(), Localization));
        OnPropertyChanged(nameof(HasEntries));
    }

    /// <summary>"None of these match" — switch to a blank manual configuration.</summary>
    [RelayCommand]
    private void UseNoMatch()
    {
        SkipSavegames = false;
        MatchedTitle = null;
        IsManual = true;
        SelectedSuggestion = null;

        if (Entries.Count == 0)
        {
            AddEntry();
        }
    }

    [RelayCommand]
    private void AddEntry()
    {
        SkipSavegames = false;
        IsManual = true;
        Entries.Add(new SavegameEntryRowViewModel(new SavegameEntry(string.Empty, SavegameEntryKind.Path), Localization));
        OnPropertyChanged(nameof(HasEntries));
    }

    [RelayCommand]
    private void RemoveEntry(SavegameEntryRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        Entries.Remove(row);
        IsManual = true;
        MatchedTitle = null;
        OnPropertyChanged(nameof(HasEntries));
        row.Dispose();
    }

    /// <summary>Builds the persisted configuration from the current editor state.</summary>
    public SavegameConfig Build()
    {
        var config = new SavegameConfig
        {
            MatchedCatalogTitle = MatchedTitle,
            IsManual = IsManual,
        };

        if (SkipSavegames)
        {
            config.IsConfigured = false;
            return config;
        }

        foreach (var row in Entries)
        {
            var entry = row.ToEntry();
            if (entry is not null)
            {
                config.Entries.Add(entry);
            }
        }

        // Configured once the user made an explicit choice: any valid entry or a chosen catalog title.
        config.IsConfigured = config.Entries.Count > 0 || MatchedTitle is not null;
        return config;
    }

    partial void OnMatchedTitleChanged(string? value) => OnPropertyChanged(nameof(HasMatchedTitle));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _searchCts?.Cancel();
            _searchCts?.Dispose();
            foreach (var row in Entries)
            {
                row.Dispose();
            }

            foreach (var suggestion in Suggestions)
            {
                suggestion.Dispose();
            }
        }

        base.Dispose(disposing);
    }
}
