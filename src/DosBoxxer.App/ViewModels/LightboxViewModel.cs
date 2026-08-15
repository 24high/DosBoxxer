using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DosBoxxer.App.Services;
using DosBoxxer.Core.Abstractions;

namespace DosBoxxer.App.ViewModels;

/// <summary>Full size screenshot viewer with previous/next navigation.</summary>
public sealed partial class LightboxViewModel : ViewModelBase
{
    private readonly IReadOnlyList<string> _paths;
    private readonly IImageLoader _imageLoader;

    [ObservableProperty]
    private Bitmap? _image;

    [ObservableProperty]
    private int _currentIndex;

    [ObservableProperty]
    private bool _isLoading;

    public LightboxViewModel(
        IReadOnlyList<string> paths,
        int startIndex,
        IImageLoader imageLoader,
        ILocalizationService localization)
        : base(localization)
    {
        _paths = paths;
        _imageLoader = imageLoader;
        _currentIndex = Math.Clamp(startIndex, 0, Math.Max(0, paths.Count - 1));
    }

    public bool CanNavigate => _paths.Count > 1;

    public string Caption => L("Lightbox.Title", CurrentIndex + 1, _paths.Count);

    public event EventHandler? CloseRequested;

    public Task InitializeAsync() => LoadCurrentAsync();

    [RelayCommand]
    private async Task PreviousAsync()
    {
        if (_paths.Count == 0)
        {
            return;
        }

        CurrentIndex = (CurrentIndex - 1 + _paths.Count) % _paths.Count;
        await LoadCurrentAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (_paths.Count == 0)
        {
            return;
        }

        CurrentIndex = (CurrentIndex + 1) % _paths.Count;
        await LoadCurrentAsync().ConfigureAwait(true);
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this, EventArgs.Empty);

    private async Task LoadCurrentAsync()
    {
        if (_paths.Count == 0)
        {
            return;
        }

        IsLoading = true;
        try
        {
            Image = await _imageLoader.LoadFullAsync(_paths[CurrentIndex]).ConfigureAwait(true);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnCurrentIndexChanged(int value) => OnPropertyChanged(nameof(Caption));

    protected override void OnLanguageChanged()
    {
        base.OnLanguageChanged();
        OnPropertyChanged(nameof(Caption));
    }
}
