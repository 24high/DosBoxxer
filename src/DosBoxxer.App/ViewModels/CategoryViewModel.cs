using CommunityToolkit.Mvvm.ComponentModel;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models;

namespace DosBoxxer.App.ViewModels;

/// <summary>
/// One tile in the left hand category column: either a virtual scope (all games, favorites,
/// recently played) or a normalised genre.
/// </summary>
public sealed partial class CategoryViewModel : ViewModelBase
{
    [ObservableProperty]
    private int _count;

    [ObservableProperty]
    private bool _isSelected;

    public CategoryViewModel(
        ILocalizationService localization,
        LibraryScope scope,
        GenreKey? genre,
        string resourceKey,
        string iconKey)
        : base(localization)
    {
        Scope = scope;
        Genre = genre;
        ResourceKey = resourceKey;
        IconKey = iconKey;
    }

    public LibraryScope Scope { get; }

    public GenreKey? Genre { get; }

    public string ResourceKey { get; }

    /// <summary>Key of the <c>StreamGeometry</c> resource that renders this tile's icon.</summary>
    public string IconKey { get; }

    public string DisplayName => L(ResourceKey);

    public string AccessibleName => L("Accessibility.GenreTile", DisplayName, Count);

    /// <summary>The counter badge is hidden for empty genres to keep the column calm.</summary>
    public bool ShowCount => Count > 0;

    public LibraryQuery ToQuery(string? searchText, GameSortOrder sortOrder) => new()
    {
        Scope = Scope,
        Genre = Genre,
        SearchText = searchText,
        SortOrder = sortOrder,
    };

    partial void OnCountChanged(int value) => OnPropertyChanged(nameof(ShowCount));

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(AccessibleName));
    }
}
