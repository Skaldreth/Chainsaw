using System.Windows;
using System.Windows.Controls;

namespace Chainsaw.App.Views;

/// <summary>The warning by the bin (sketch SO1a); its two buttons are passed on to the graph.</summary>
public sealed partial class TrashWarningView : UserControl
{
    /// <summary>Creates the warning.</summary>
    public TrashWarningView() => InitializeComponent();

    /// <summary>Raised by <em>Släng alla N · Enter</em>.</summary>
    public event EventHandler? Confirmed;

    /// <summary>Raised by <em>Avbryt · Esc</em>.</summary>
    public event EventHandler? Cancelled;

    /// <summary>Passes the confirmation on.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnConfirm(object sender, RoutedEventArgs e) => Confirmed?.Invoke(this, EventArgs.Empty);

    /// <summary>Passes the cancelling on.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnCancel(object sender, RoutedEventArgs e) => Cancelled?.Invoke(this, EventArgs.Empty);
}
