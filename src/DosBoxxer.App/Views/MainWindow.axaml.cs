using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using DosBoxxer.App.ViewModels;
using DosBoxxer.Core.Models;

namespace DosBoxxer.App.Views;

/// <summary>
/// Code-behind is limited to the things Avalonia can only do from the view: focusing a control,
/// translating input gestures into ViewModel commands and persisting the window geometry.
/// No business logic lives here.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Below this width the details pane is folded away automatically.</summary>
    private const double CompactWidthThreshold = 1080;

    public MainWindow()
    {
        InitializeComponent();

        Opened += OnOpened;
        Closing += OnClosing;
        KeyDown += OnWindowKeyDown;
        SizeChanged += OnWindowSizeChanged;
    }

    private MainWindowViewModel? ViewModel => DataContext as MainWindowViewModel;

    /// <summary>Restores the persisted geometry before the window becomes visible.</summary>
    public void ApplyStoredWindowState(AppSettings settings)
    {
        if (!settings.RememberWindowState)
        {
            return;
        }

        var state = settings.Window;

        if (state.Width > 200 && state.Height > 200)
        {
            Width = state.Width;
            Height = state.Height;
        }

        if (state.X.HasValue && state.Y.HasValue)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = new PixelPoint((int)state.X.Value, (int)state.Y.Value);
        }

        if (state.IsMaximized)
        {
            WindowState = WindowState.Maximized;
        }
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        UpdateCompactLayout();

        if (ViewModel is not null)
        {
            await ViewModel.InitializeAsync();
        }
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        await ViewModel.SaveWindowStateAsync(
            Width,
            Height,
            Position.X,
            Position.Y,
            WindowState == WindowState.Maximized);
    }

    private void OnWindowSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateCompactLayout();

    private void UpdateCompactLayout()
    {
        if (ViewModel is not null)
        {
            ViewModel.IsCompactLayout = Bounds.Width > 0 && Bounds.Width < CompactWidthThreshold;
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        var ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);

        switch (e.Key)
        {
            case Key.F when ctrl:
                this.FindControl<TextBox>("SearchBox")?.Focus();
                e.Handled = true;
                break;

            case Key.N when ctrl:
                if (ViewModel?.AddGameCommand.CanExecute(null) == true)
                {
                    ViewModel.AddGameCommand.Execute(null);
                }

                e.Handled = true;
                break;

            case Key.F5:
                if (ViewModel?.LoadLibraryCommand.CanExecute(null) == true)
                {
                    ViewModel.LoadLibraryCommand.Execute(null);
                }

                e.Handled = true;
                break;

            case Key.Escape:
                if (ViewModel is not null && ViewModel.HasError)
                {
                    ViewModel.DismissErrorCommand.Execute(null);
                    e.Handled = true;
                }

                break;
        }
    }

    private void OnGameListDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (ViewModel?.ActivateGameCommand.CanExecute(null) == true)
        {
            ViewModel.ActivateGameCommand.Execute(null);
        }
    }

    private void OnGameListKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is null || ViewModel.SelectedCard is null)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter:
                ViewModel.ActivateGameCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.Delete:
                ViewModel.RemoveGameCommand.Execute(null);
                e.Handled = true;
                break;
        }
    }
}
