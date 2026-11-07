using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The exits' panel under the exit in the title bar (sketch UT2b). The view only wires the keys and the keyboard's
/// place; what happens lives in <see cref="ExitPanelViewModel"/>.
/// </summary>
public partial class ExitPanel : UserControl
{
    /// <summary>The panel's view model, once it is given.</summary>
    private ExitPanelViewModel? _panel;

    /// <summary>Creates the panel.</summary>
    public ExitPanel()
    {
        InitializeComponent();
    }

    /// <summary>Gives the panel its view model.</summary>
    /// <param name="panel">The view model.</param>
    public void Attach(ExitPanelViewModel panel)
    {
        _panel = panel;
        DataContext = panel;
    }

    /// <summary>Gives the keyboard to the panel's first control, once it is laid out.</summary>
    public void TakeFocus() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_panel is { IsOpen: true })
            {
                FocusFirst();
            }
        });

    /// <summary>
    /// Gives the keyboard to the first exit's button, or to <em>Inställningar för anslutning</em> when there is none
    /// (with Windows, or an empty vault).
    /// </summary>
    private void FocusFirst()
    {
        if (!MoveFocus(new TraversalRequest(FocusNavigationDirection.First)))
        {
            SettingsButton.Focus();
        }
    }

    /// <summary>Esc folds the panel in.</summary>
    /// <param name="sender">The panel.</param>
    /// <param name="e">The key.</param>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _panel is not { } panel)
        {
            return;
        }

        panel.Close();
        e.Handled = true;
    }
}
