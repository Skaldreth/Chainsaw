using System.Windows.Controls;
using System.Windows.Input;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>The view Rapport (sketch RP1b). Only wiring; what happens lives in <see cref="ReportViewModel"/>.</summary>
public partial class ReportView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ReportView() => InitializeComponent();

    /// <summary>Attaches the view model.</summary>
    /// <param name="model">The view model.</param>
    public void Attach(ReportViewModel model) => DataContext = model;

    /// <summary>Ctrl+S exports the report, as the button's tooltip says.</summary>
    /// <param name="e">The key.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (!e.Handled && e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control
            && DataContext is ReportViewModel model && model.ExportCommand.CanExecute(null))
        {
            model.ExportCommand.Execute(null);
            e.Handled = true;
        }
    }
}
