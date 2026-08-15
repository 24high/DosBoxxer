using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DosBoxxer.App.Services;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models;

namespace DosBoxxer.App.ViewModels;

/// <summary>
/// One tile in the library grid. Wraps a <see cref="Game"/> and owns the asynchronous loading
/// of its thumbnail.
/// </summary>
public sealed partial class GameCardViewModel : ViewModelBase
{
    private readonly IImageLoader _imageLoader;
    private bool _thumbnailRequested;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private int _coverWidth = 168;

    public GameCardViewModel(Game game, IImageLoader imageLoader, ILocalizationService localization, int coverWidth)
        : base(localization)
    {
        Game = game;
        _imageLoader = imageLoader;
        _coverWidth = coverWidth;
    }

    public Game Game { get; }

    public Guid Id => Game.Id;

    public string Title => Game.Title;

    public string? SubTitle => Game.ReleaseYear?.ToString(Localization.CurrentCulture);

    public bool IsFavorite => Game.IsFavorite;

    public int CoverHeight => (int)Math.Round(CoverWidth * 4.0 / 3.0);

    /// <summary>Two capital letters used by the placeholder when no image is available.</summary>
    public string Initials
    {
        get
        {
            var words = Game.Title
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(w => char.IsLetterOrDigit(w[0]))
                .Take(2)
                .ToArray();

            return words.Length switch
            {
                0 => "?",
                1 => words[0].Length >= 2 ? words[0][..2].ToUpperInvariant() : words[0].ToUpperInvariant(),
                _ => (words[0][..1] + words[1][..1]).ToUpperInvariant(),
            };
        }
    }

    public string AccessibleName => L("Accessibility.GameCard", Game.Title);

    public bool HasThumbnail => Thumbnail is not null;

    /// <summary>
    /// Cover art first, then the first screenshot, then the letter placeholder — exactly the
    /// fallback chain the library grid needs.
    /// </summary>
    private string? PreferredImagePath =>
        !string.IsNullOrWhiteSpace(Game.CoverImagePath)
            ? Game.CoverImagePath
            : Game.Screenshots.Count > 0
                ? Game.Screenshots[0].LocalPath
                : null;

    /// <summary>Starts loading the thumbnail once; safe to call repeatedly.</summary>
    public async Task EnsureThumbnailAsync()
    {
        if (_thumbnailRequested)
        {
            return;
        }

        _thumbnailRequested = true;

        var path = PreferredImagePath;
        if (path is null)
        {
            return;
        }

        // Decode slightly above the display width so the image stays sharp on HiDPI screens.
        var bitmap = await _imageLoader.LoadThumbnailAsync(path, CoverWidth * 2).ConfigureAwait(true);

        Thumbnail = bitmap;
        OnPropertyChanged(nameof(HasThumbnail));
    }

    public void RefreshFromModel()
    {
        _thumbnailRequested = false;
        Thumbnail = null;

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(SubTitle));
        OnPropertyChanged(nameof(IsFavorite));
        OnPropertyChanged(nameof(Initials));
        OnPropertyChanged(nameof(HasThumbnail));
    }

    partial void OnCoverWidthChanged(int value)
    {
        OnPropertyChanged(nameof(CoverHeight));
        _thumbnailRequested = false;
        _ = EnsureThumbnailAsync();
    }

    partial void OnThumbnailChanged(Bitmap? value) => OnPropertyChanged(nameof(HasThumbnail));

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(AccessibleName));
    }
}
