using System.Windows.Controls;
using System.Windows.Input;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The view Bevakning (sketch BV1a). Only wiring; what happens lives in <see cref="MonitoringViewModel"/>.
/// </summary>
public partial class MonitoringView : UserControl
{
    /// <summary>Creates the view.</summary>
    public MonitoringView() => InitializeComponent();

    /// <summary>Attaches the view model.</summary>
    /// <param name="model">The view model.</param>
    public void Attach(MonitoringViewModel model) => DataContext = model;

    /// <summary>
    /// B opens <em>Ny bevakning …</em>, but never while a field is being written in, where B is a letter.
    /// </summary>
    /// <param name="e">The key.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.None
            && Keyboard.FocusedElement is not TextBox && DataContext is MonitoringViewModel model)
        {
            model.New();
            e.Handled = true;
        }
    }
}
