using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The view Flöde (sketch FL1a). Only wiring; what happens lives in <see cref="FlowViewModel"/>.
/// </summary>
public partial class FlowView : UserControl
{
    /// <summary>The view's own margin, as the sketch places it beside the pill.</summary>
    private static readonly Thickness RoomMargin = new(84, 16, 20, 16);

    /// <summary>Creates the view.</summary>
    public FlowView()
    {
        InitializeComponent();
        Chart.SizeChanged += (_, _) => Model?.Resize(Chart.ActualWidth, Chart.ActualHeight);
    }

    /// <summary>Gets the view model, once attached.</summary>
    private FlowViewModel? Model => DataContext as FlowViewModel;

    /// <summary>Attaches the view model.</summary>
    /// <param name="model">The view model.</param>
    public void Attach(FlowViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        model.Resize(Chart.ActualWidth, Chart.ActualHeight);
    }

    /// <summary>Makes room on the right for the Guide's panel, so that the side panel stays in sight.</summary>
    /// <param name="covered">How wide the panel covering the right edge is.</param>
    public void MakeRoom(double covered) =>
        Room.Margin = new Thickness(RoomMargin.Left, RoomMargin.Top, RoomMargin.Right + covered, RoomMargin.Bottom);

    /// <summary>G shows the chosen way in the graph, as the button says.</summary>
    /// <param name="e">The key.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (!e.Handled && Model is { } model && Keyboard.Modifiers == ModifierKeys.None && e.Key == Key.G
            && model.ShowPathCommand.CanExecute(null))
        {
            model.ShowPathCommand.Execute(null);
            e.Handled = true;
        }
    }
}
