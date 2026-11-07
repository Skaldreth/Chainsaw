using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Chainsaw.App.Views;

/// <summary>
/// The small form for an own area (sketch KF1b). Only wiring: it takes the keyboard when it is shown, so that the name
/// can be written at once; what happens lives in <see cref="ViewModels.ChecksViewModel"/>.
/// </summary>
public partial class AreaForm : UserControl
{
    /// <summary>Creates the form.</summary>
    public AreaForm()
    {
        InitializeComponent();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Input,
                    () => MoveFocus(new TraversalRequest(FocusNavigationDirection.First)));
            }
        };
    }
}
