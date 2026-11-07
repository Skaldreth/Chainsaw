using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Chainsaw.App.Views;

/// <summary>
/// The inspector's history (sketch AN1b). Only wiring: when a row's form folds out, its reason takes the keyboard, so
/// that Enter stores and Esc cancels at once; what happens lives in
/// <see cref="ViewModels.InspectorHistoryViewModel"/>.
/// </summary>
public partial class InspectorHistoryView : UserControl
{
    /// <summary>Creates the history's view.</summary>
    public InspectorHistoryView() => InitializeComponent();

    /// <summary>Gives the keyboard to a reason field that has just become visible, once it is drawn.</summary>
    /// <param name="sender">The field.</param>
    /// <param name="e">Whether it is visible.</param>
    private void OnReasonVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is TextBox field && e.NewValue is true)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(field));
        }
    }
}
