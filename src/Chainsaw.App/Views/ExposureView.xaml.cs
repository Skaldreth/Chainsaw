using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The view Exponering (sketch EP1b). Only wiring; what happens lives in <see cref="ExposureViewModel"/>.
/// </summary>
public partial class ExposureView : UserControl
{
    /// <summary>The view's own margin, as the sketch places it beside the pill.</summary>
    private static readonly Thickness RoomMargin = new(84, 16, 20, 16);

    /// <summary>Creates the view.</summary>
    public ExposureView() => InitializeComponent();

    /// <summary>Attaches the view model.</summary>
    /// <param name="model">The view model.</param>
    public void Attach(ExposureViewModel model) => DataContext = model;

    /// <summary>Makes room on the right for the Guide's panel, so that the side panel stays in sight.</summary>
    /// <param name="covered">How wide the panel covering the right edge is.</param>
    public void MakeRoom(double covered) =>
        Room.Margin = new Thickness(RoomMargin.Left, RoomMargin.Top, RoomMargin.Right + covered, RoomMargin.Bottom);

    /// <summary>G shows the chosen way in the graph, as the button says; Esc folds the subject menu in.</summary>
    /// <param name="e">The key.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not ExposureViewModel model || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.Key == Key.G && model.ShowPathCommand.CanExecute(null))
        {
            model.ShowPathCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape && model.IsMenuOpen)
        {
            model.IsMenuOpen = false;
            e.Handled = true;
        }
    }
}
