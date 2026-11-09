using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Chainsaw.App.Views;

/// <summary>
/// Taking back an approved wallet, confirmed in the graph (sketch PL1b). Only wiring: it takes the keyboard on
/// <em>Ångra</em> when it is shown, so that Enter and Esc answer at once; what happens lives in
/// <see cref="ViewModels.ChecksViewModel"/>.
/// </summary>
public partial class WalletUndoConfirm : UserControl
{
    /// <summary>Creates the card and its box.</summary>
    public WalletUndoConfirm()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(Confirm));
            }
        };
    }
}
