using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using DosBoxxer.App.Services;
using DosBoxxer.Core.Abstractions;

namespace DosBoxxer.App.ViewModels;

/// <summary>A single screenshot thumbnail in the details pane gallery.</summary>
public sealed partial class ScreenshotViewModel : ViewModelBase
{
    private readonly IImageLoader _imageLoader;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    public ScreenshotViewModel(
        string localPath,
        int index,
        int total,
        IImageLoader imageLoader,
        ILocalizationService localization)
        : base(localization)
    {
        LocalPath = localPath;
        Index = index;
        Total = total;
        _imageLoader = imageLoader;
    }

    public string LocalPath { get; }

    public int Index { get; }

    public int Total { get; }

    public string AccessibleName => L("Accessibility.ScreenshotImage", Index + 1, Total);

    public async Task LoadAsync()
    {
        Thumbnail = await _imageLoader.LoadThumbnailAsync(LocalPath, 480).ConfigureAwait(true);
    }

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(AccessibleName));
    }
}
