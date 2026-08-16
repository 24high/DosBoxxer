using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using DosBoxxer.App.ViewModels;
using DosBoxxer.App.Views;
using DosBoxxer.Core.Abstractions;
using DosBoxxer.Core.Helpers;
using DosBoxxer.Core.Models;
using DosBoxxer.Core.Models.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DosBoxxer.App.Services;

/// <summary>
/// Window and file-picker layer. This is the only place in the application that knows about
/// Avalonia windows; ViewModels only ever see <see cref="IDialogService"/>.
/// </summary>
public sealed class DialogService : IDialogService
{
    private readonly IServiceProvider _services;
    private readonly ILocalizationService _localization;
    private readonly ILogger<DialogService> _logger;

    public DialogService(IServiceProvider services, ILocalizationService localization, ILogger<DialogService> logger)
    {
        _services = services;
        _localization = localization;
        _logger = logger;
    }

    private static Window? Owner =>
        Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop
            ? desktop.Windows.Count > 0
                ? desktop.Windows[^1]
                : desktop.MainWindow
            : null;

    // ---- file system pickers ---------------------------------------------------------------

    public async Task<string?> PickFolderAsync(string title, string? startPath = null)
    {
        var owner = Owner;
        if (owner?.StorageProvider is not { CanPickFolder: true } provider)
        {
            return null;
        }

        var options = new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            SuggestedStartLocation = await TryGetStartFolderAsync(provider, startPath).ConfigureAwait(true),
        };

        var result = await provider.OpenFolderPickerAsync(options).ConfigureAwait(true);

        if (result.Count == 0)
        {
            return null;
        }

        var path = result[0].TryGetLocalPath();
        return string.IsNullOrEmpty(path) ? null : PathHelper.Normalize(path);
    }

    public async Task<string?> PickFileAsync(string title, FilePickerKind kind, string? startPath = null)
    {
        var owner = Owner;
        if (owner?.StorageProvider is not { CanOpen: true } provider)
        {
            return null;
        }

        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = BuildFilters(kind),
            SuggestedStartLocation = await TryGetStartFolderAsync(provider, startPath).ConfigureAwait(true),
        };

        var result = await provider.OpenFilePickerAsync(options).ConfigureAwait(true);

        if (result.Count == 0)
        {
            return null;
        }

        var path = result[0].TryGetLocalPath();
        return string.IsNullOrEmpty(path) ? null : PathHelper.Normalize(path);
    }

    private static async Task<IStorageFolder?> TryGetStartFolderAsync(IStorageProvider provider, string? startPath)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return null;
        }

        var directory = Directory.Exists(startPath) ? startPath : Path.GetDirectoryName(startPath);

        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return null;
        }

        try
        {
            return await provider.TryGetFolderFromPathAsync(directory).ConfigureAwait(true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return null;
        }
    }

    private IReadOnlyList<FilePickerFileType> BuildFilters(FilePickerKind kind) => kind switch
    {
        FilePickerKind.Image => new[]
        {
            new FilePickerFileType(_localization.Get("Edit.Cover"))
            {
                Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp", "*.gif" },
                MimeTypes = new[] { "image/png", "image/jpeg", "image/webp", "image/bmp", "image/gif" },
                AppleUniformTypeIdentifiers = new[] { "public.image" },
            },
            FilePickerFileTypes.All,
        },

        // DOS executables use upper case names as often as lower case; the picker patterns are
        // matched case insensitively on Windows but not on Linux, so both are listed.
        FilePickerKind.Executable => new[]
        {
            new FilePickerFileType(_localization.Get("Edit.LaunchFile"))
            {
                Patterns = new[] { "*.exe", "*.EXE", "*.bat", "*.BAT", "*.com", "*.COM", "*" },
            },
            FilePickerFileTypes.All,
        },

        FilePickerKind.DosBoxConfig => new[]
        {
            new FilePickerFileType("dosbox.conf")
            {
                Patterns = new[] { "*.conf", "*.CONF", "*.cfg", "*.ini" },
            },
            FilePickerFileTypes.All,
        },

        _ => new[] { FilePickerFileTypes.All },
    };

    // ---- message / confirmation dialogs ------------------------------------------------------

    public async Task ShowMessageAsync(string title, string message, bool isError = false)
    {
        var viewModel = new ConfirmDialogViewModel(_localization)
        {
            DialogTitle = title,
            Message = message,
            ShowCancel = false,
            IsError = isError,
            ConfirmText = _localization.Get("Common.Ok"),
        };

        await ShowDialogAsync<ConfirmDialogWindow>(viewModel).ConfigureAwait(true);
    }

    public async Task<bool> ConfirmAsync(string title, string message, string? hint = null)
    {
        var viewModel = new ConfirmDialogViewModel(_localization)
        {
            DialogTitle = title,
            Message = message,
            Hint = hint,
            ShowCancel = true,
            ConfirmText = _localization.Get("Common.Yes"),
            CancelText = _localization.Get("Common.No"),
        };

        return await ShowDialogAsync<ConfirmDialogWindow>(viewModel).ConfigureAwait(true);
    }

    public async Task<RemoveGameChoice> ConfirmRemoveGameAsync(Game game)
    {
        var viewModel = new ConfirmDialogViewModel(_localization)
        {
            DialogTitle = _localization.Get("Confirm.RemoveGameTitle"),
            Message = _localization.Format("Confirm.RemoveGameMessage", game.Title),
            Hint = _localization.Get("Confirm.RemoveGameHint"),
            ExtraOptionText = _localization.Get("Confirm.RemoveCachedMedia"),
            ShowCancel = true,
            ConfirmText = _localization.Get("Common.Remove"),
            CancelText = _localization.Get("Common.Cancel"),
        };

        var confirmed = await ShowDialogAsync<ConfirmDialogWindow>(viewModel).ConfigureAwait(true);

        return new RemoveGameChoice
        {
            Confirmed = confirmed,
            DeleteCachedMedia = confirmed && viewModel.ExtraOptionValue,
        };
    }

    // ---- feature dialogs ---------------------------------------------------------------------

    public async Task<Game?> ShowAddGameWizardAsync()
    {
        var viewModel = ActivatorUtilities.CreateInstance<AddGameWizardViewModel>(_services);
        var window = new AddGameWizardWindow { DataContext = viewModel };

        var completed = await ShowWindowAsync(window, viewModel).ConfigureAwait(true);
        var game = completed ? viewModel.CreatedGame : null;

        viewModel.Dispose();
        return game;
    }

    public async Task<BulkWizardResult> ShowAddGameWizardAsync(BulkWizardContext context)
    {
        var viewModel = ActivatorUtilities.CreateInstance<AddGameWizardViewModel>(_services);
        var window = new AddGameWizardWindow { DataContext = viewModel };

        // Pre-seed the folder and kick off the scan before the window is shown.
        await viewModel.BeginBatchAsync(context).ConfigureAwait(true);

        await ShowWindowAsync(window, viewModel).ConfigureAwait(true);

        var result = new BulkWizardResult
        {
            Decision = viewModel.BatchDecision,
            Game = viewModel.BatchDecision == BulkWizardDecision.Added ? viewModel.CreatedGame : null,
        };

        viewModel.Dispose();
        return result;
    }

    public async Task<bool> ShowEditGameAsync(Game game)
    {
        var viewModel = ActivatorUtilities.CreateInstance<EditGameViewModel>(_services, game);
        var window = new EditGameWindow { DataContext = viewModel };

        await viewModel.InitializeAsync().ConfigureAwait(true);

        var saved = await ShowWindowAsync(window, viewModel).ConfigureAwait(true);

        viewModel.Dispose();
        return saved;
    }

    public async Task<bool> ShowSettingsAsync()
    {
        var viewModel = ActivatorUtilities.CreateInstance<SettingsViewModel>(_services);
        var window = new SettingsWindow { DataContext = viewModel };

        await viewModel.InitializeAsync().ConfigureAwait(true);

        var saved = await ShowWindowAsync(window, viewModel).ConfigureAwait(true);

        viewModel.Dispose();
        return saved;
    }

    public async Task ShowLightboxAsync(IReadOnlyList<string> imagePaths, int startIndex)
    {
        if (imagePaths.Count == 0)
        {
            return;
        }

        var imageLoader = _services.GetRequiredService<IImageLoader>();
        var viewModel = new LightboxViewModel(imagePaths, startIndex, imageLoader, _localization);
        var window = new LightboxWindow { DataContext = viewModel };

        void OnClose(object? sender, EventArgs e) => window.Close();

        viewModel.CloseRequested += OnClose;

        await viewModel.InitializeAsync().ConfigureAwait(true);

        // Owned by the top-most window so a dialog opened from a dialog nests correctly.
        var owner = Owner;
        if (owner is null || ReferenceEquals(owner, window))
        {
            window.Show();
        }
        else
        {
            await window.ShowDialog(owner).ConfigureAwait(true);
        }

        viewModel.CloseRequested -= OnClose;
        viewModel.Dispose();
    }

    public async Task<RefreshMetadataChoice?> ShowRefreshMetadataAsync(Game game)
    {
        var viewModel = new RefreshMetadataViewModel(game, this, _localization);
        var window = new RefreshMetadataWindow { DataContext = viewModel };

        var confirmed = await ShowWindowAsync(window, viewModel).ConfigureAwait(true);
        var result = confirmed ? viewModel.Result : null;

        viewModel.Dispose();
        return result;
    }

    public async Task<MetadataSearchResult?> ShowMetadataSearchAsync(string initialSearchTerm)
    {
        var provider = _services.GetRequiredService<IGameMetadataProvider>();
        var logger = _services.GetRequiredService<ILoggerFactory>().CreateLogger<MetadataSearchViewModel>();

        var viewModel = new MetadataSearchViewModel(initialSearchTerm, provider, _localization, logger);
        var window = new MetadataSearchWindow { DataContext = viewModel };

        var confirmed = await ShowWindowAsync(window, viewModel).ConfigureAwait(true);
        var selection = confirmed ? viewModel.Selection : null;

        viewModel.Dispose();
        return selection;
    }

    // ---- plumbing ----------------------------------------------------------------------------

    private async Task<bool> ShowDialogAsync<TWindow>(ConfirmDialogViewModel viewModel)
        where TWindow : Window, new()
    {
        var window = new TWindow { DataContext = viewModel };
        return await ShowWindowAsync(window, viewModel).ConfigureAwait(true);
    }

    /// <summary>
    /// Shows <paramref name="window"/> modally and resolves with the value the ViewModel passed
    /// to its close event. The event subscription is always removed again.
    /// </summary>
    private async Task<bool> ShowWindowAsync<TViewModel>(Window window, TViewModel viewModel)
        where TViewModel : class
    {
        var completion = new TaskCompletionSource<bool>();
        var result = false;

        void OnCloseRequested(object? sender, bool value)
        {
            result = value;
            window.Close();
        }

        // The close event is declared per ViewModel type; reflection-free wiring is done by
        // the small switch below so this helper stays usable for every dialog.
        switch (viewModel)
        {
            case AddGameWizardViewModel wizard:
                wizard.CloseRequested += OnCloseRequested;
                break;
            case EditGameViewModel edit:
                edit.CloseRequested += OnCloseRequested;
                break;
            case SettingsViewModel settings:
                settings.CloseRequested += OnCloseRequested;
                break;
            case RefreshMetadataViewModel refresh:
                refresh.CloseRequested += OnCloseRequested;
                break;
            case MetadataSearchViewModel search:
                search.CloseRequested += OnCloseRequested;
                break;
            case ConfirmDialogViewModel confirm:
                confirm.CloseRequested += OnCloseRequested;
                break;
            default:
                _logger.LogWarning("Unknown dialog view model type {Type}", viewModel.GetType().Name);
                break;
        }

        void OnClosed(object? sender, EventArgs e) => completion.TrySetResult(result);

        window.Closed += OnClosed;

        // Owned by the top-most window so a dialog opened from a dialog nests correctly.
        var owner = Owner;
        if (owner is null || ReferenceEquals(owner, window))
        {
            window.Show();
        }
        else
        {
            await window.ShowDialog(owner).ConfigureAwait(true);
        }

        var completed = await completion.Task.ConfigureAwait(true);

        window.Closed -= OnClosed;

        switch (viewModel)
        {
            case AddGameWizardViewModel wizard:
                wizard.CloseRequested -= OnCloseRequested;
                break;
            case EditGameViewModel edit:
                edit.CloseRequested -= OnCloseRequested;
                break;
            case SettingsViewModel settings:
                settings.CloseRequested -= OnCloseRequested;
                break;
            case RefreshMetadataViewModel refresh:
                refresh.CloseRequested -= OnCloseRequested;
                break;
            case MetadataSearchViewModel search:
                search.CloseRequested -= OnCloseRequested;
                break;
            case ConfirmDialogViewModel confirm:
                confirm.CloseRequested -= OnCloseRequested;
                break;
        }

        return completed;
    }
}
