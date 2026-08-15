using Avalonia.Controls;
using Avalonia.Input;

namespace DosBoxxer.App.Views;

/// <summary>
/// Shared base for every modal dialog: centred on the owner, no resize grip clutter and
/// Escape closes the window, as required by the keyboard specification.
/// </summary>
public class DialogWindow : Window
{
    protected DialogWindow()
    {
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled)
        {
            Close();
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }
}
