using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DosBoxxer.App.Services;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Models;

namespace DosBoxxer.App.ViewModels;

/// <summary>
/// Read model for the details pane. Every optional field is paired with a <c>Has…</c> flag so
/// the view can hide the whole row instead of rendering an empty label.
/// </summary>
public sealed partial class GameDetailsViewModel : ViewModelBase
{
    private readonly IImageLoader _imageLoader;

    [ObservableProperty]
    private Game? _game;

    [ObservableProperty]
    private Bitmap? _cover;

    public GameDetailsViewModel(IImageLoader imageLoader, ILocalizationService localization)
        : base(localization)
    {
        _imageLoader = imageLoader;
    }

    public ObservableCollection<ScreenshotViewModel> Screenshots { get; } = new();

    public bool HasGame => Game is not null;

    public string Title => Game?.Title ?? string.Empty;

    public string Publisher => Game?.Publisher ?? string.Empty;

    public bool HasPublisher => !string.IsNullOrWhiteSpace(Game?.Publisher);

    public string Developer => Game?.Developer ?? string.Empty;

    public bool HasDeveloper => !string.IsNullOrWhiteSpace(Game?.Developer);

    public string ReleaseYear => Game?.ReleaseYear?.ToString(Localization.CurrentCulture) ?? string.Empty;

    public bool HasReleaseYear => Game?.ReleaseYear is not null;

    public string Genres => Game is null
        ? string.Empty
        : string.Join(" • ", Game.Genres.Select(g => L(GenreKeys.ResourceKey(g))));

    public bool HasGenres => Game is { Genres.Count: > 0 };

    public string Platform => Game?.Platform ?? string.Empty;

    public bool HasPlatform => !string.IsNullOrWhiteSpace(Game?.Platform);

    public string Players => Game?.Players ?? string.Empty;

    public bool HasPlayers => !string.IsNullOrWhiteSpace(Game?.Players);

    public string Description => Game?.Description ?? string.Empty;

    public bool HasDescription => !string.IsNullOrWhiteSpace(Game?.Description);

    public bool HasScreenshots => Screenshots.Count > 0;

    public bool HasCover => Cover is not null;

    public string DateAdded => Game is null
        ? string.Empty
        : Game.DateAdded.ToLocalTime().ToString("d", Localization.CurrentCulture);

    public string LastPlayed => Game?.LastPlayed is null
        ? L("Common.Never")
        : Game.LastPlayed.Value.ToLocalTime().ToString("g", Localization.CurrentCulture);

    public string PlayCount => Game?.PlayCount.ToString(Localization.CurrentCulture) ?? "0";

    public string PlayTime => FormatDuration(Game?.TotalPlayTimeSeconds ?? 0);

    public string GameDirectory => Game?.GameDirectory ?? string.Empty;

    public string LaunchFile => Game?.RelativeLaunchFile ?? string.Empty;

    public bool IsFavorite => Game?.IsFavorite ?? false;

    public string FavoriteLabel => IsFavorite ? L("Details.RemoveFromFavorites") : L("Details.AddToFavorites");

    public string Initials
    {
        get
        {
            if (Game is null)
            {
                return "?";
            }

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

    public IReadOnlyList<string> ScreenshotPaths => Screenshots.Select(s => s.LocalPath).ToList();

    public async Task SetGameAsync(Game? game)
    {
        Game = game;
        Cover = null;

        foreach (var screenshot in Screenshots)
        {
            screenshot.Dispose();
        }

        Screenshots.Clear();

        RaiseAll();

        if (game is null)
        {
            return;
        }

        var coverPath = !string.IsNullOrWhiteSpace(game.CoverImagePath)
            ? game.CoverImagePath
            : game.Screenshots.Count > 0
                ? game.Screenshots[0].LocalPath
                : null;

        var total = game.Screenshots.Count;
        for (var i = 0; i < total; i++)
        {
            var vm = new ScreenshotViewModel(game.Screenshots[i].LocalPath, i, total, _imageLoader, Localization);
            Screenshots.Add(vm);
        }

        OnPropertyChanged(nameof(HasScreenshots));
        OnPropertyChanged(nameof(ScreenshotPaths));

        Cover = await _imageLoader.LoadThumbnailAsync(coverPath, 640).ConfigureAwait(true);
        OnPropertyChanged(nameof(HasCover));

        foreach (var screenshot in Screenshots)
        {
            await screenshot.LoadAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Re-reads every projected value from the current model.</summary>
    public void RaiseAll() => OnPropertyChanged(string.Empty);

    private string FormatDuration(long totalSeconds)
    {
        if (totalSeconds <= 0)
        {
            return L("Unit.Minutes", 0);
        }

        var span = TimeSpan.FromSeconds(totalSeconds);

        if (span.TotalHours >= 1)
        {
            return L("Unit.HoursMinutes", (int)span.TotalHours, span.Minutes);
        }

        if (span.TotalMinutes >= 1)
        {
            return L("Unit.Minutes", (int)span.TotalMinutes);
        }

        return L("Unit.Seconds", (int)span.TotalSeconds);
    }

    partial void OnCoverChanged(Bitmap? value) => OnPropertyChanged(nameof(HasCover));

    partial void OnGameChanged(Game? value) => OnPropertyChanged(nameof(HasGame));

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var screenshot in Screenshots)
            {
                screenshot.Dispose();
            }

            Screenshots.Clear();
        }

        base.Dispose(disposing);
    }
}
