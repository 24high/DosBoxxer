using Avalonia.Input;
using DosBoxxer.App.ViewModels;

namespace DosBoxxer.App.Views;

public partial class LightboxWindow : DialogWindow
{
    public LightboxWindow() => InitializeComponent();

    /// <summary>Arrow keys page through the gallery, matching the on-screen buttons.</summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (DataContext is LightboxViewModel viewModel)
        {
            switch (e.Key)
            {
                case Key.Left:
                    viewModel.PreviousCommand.Execute(null);
                    e.Handled = true;
                    return;

                case Key.Right:
                    viewModel.NextCommand.Execute(null);
                    e.Handled = true;
                    return;
            }
        }

        base.OnKeyDown(e);
    }
}
