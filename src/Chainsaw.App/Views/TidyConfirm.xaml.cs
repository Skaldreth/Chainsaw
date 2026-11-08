using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Chainsaw.App.Views;

/// <summary>
/// The question before the graph is sorted. Only wiring: it takes the keyboard on <em>Sortera</em> when it is shown,
/// so that Enter and Esc answer at once; what happens lives in <see cref="ViewModels.GraphViewModel"/>.
/// </summary>
public partial class TidyConfirm : UserControl
{
    /// <summary>Creates the question.</summary>
    public TidyConfirm()
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
