using CommunityToolkit.Mvvm.ComponentModel;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models;

namespace DosBoxxer.App.ViewModels;

/// <summary>Localised entry of the sort order drop-down.</summary>
public sealed class SortOptionViewModel : ViewModelBase
{
    public SortOptionViewModel(GameSortOrder order, ILocalizationService localization)
        : base(localization) => Order = order;

    public GameSortOrder Order { get; }

    public string DisplayName => L(GameSortOrders.ResourceKey(Order));

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(DisplayName));
    }
}

/// <summary>Localised entry of the cover size drop-down.</summary>
public sealed class CoverSizeOptionViewModel : ViewModelBase
{
    public CoverSizeOptionViewModel(CoverSize size, ILocalizationService localization)
        : base(localization) => Size = size;

    public CoverSize Size { get; }

    public string DisplayName => L("CoverSize." + Size);

    /// <summary>Pixel width of a cover tile for this setting.</summary>
    public int Width => Size switch
    {
        CoverSize.Small => 128,
        CoverSize.Large => 216,
        _ => 168,
    };

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(DisplayName));
    }
}

/// <summary>Localised entry of the language drop-down.</summary>
public sealed class LanguageOptionViewModel : ObservableObject
{
    public LanguageOptionViewModel(LanguageOption option) => Option = option;

    public LanguageOption Option { get; }

    public string Code => Option.Code;

    /// <summary>Shown in the language's own script, which is what users look for.</summary>
    public string DisplayName => Option.NativeName;
}

/// <summary>A genre checkbox in the edit dialog.</summary>
public sealed partial class GenreSelectionViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isSelected;

    public GenreSelectionViewModel(GenreKey genre, bool isSelected, ILocalizationService localization)
        : base(localization)
    {
        Genre = genre;
        _isSelected = isSelected;
    }

    public GenreKey Genre { get; }

    public string DisplayName => L(GenreKeys.ResourceKey(Genre));

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(DisplayName));
    }
}

/// <summary>A metadata field checkbox in the refresh dialog.</summary>
public sealed partial class FieldSelectionViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isSelected = true;

    public FieldSelectionViewModel(GameField field, bool isManual, ILocalizationService localization)
        : base(localization)
    {
        Field = field;
        IsManual = isManual;
    }

    public GameField Field { get; }

    /// <summary>True when the user edited this field by hand; shown as a hint in the dialog.</summary>
    public bool IsManual { get; }

    public string DisplayName => L(GameFields.ResourceKey(Field));

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(DisplayName));
    }
}
