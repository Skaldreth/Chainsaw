using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The legend behind the graph's <em>?</em> (sketch GR7a). Only wiring: it tells the legend's view model what the
/// pointer is over, so that the bottom row can explain it; what is shown lives in <see cref="LegendViewModel"/>.
/// </summary>
public partial class GraphLegendView : UserControl
{
    /// <summary>Creates the legend.</summary>
    public GraphLegendView() => InitializeComponent();

    /// <summary>Gets the legend's view model, once the graph's is attached.</summary>
    private LegendViewModel? Legend => (DataContext as GraphViewModel)?.Legend;

    /// <summary>Says that the pointer came over a sort, a colour, a form, a mark or a line.</summary>
    /// <param name="sender">The item's surface.</param>
    /// <param name="e">Not used.</param>
    private void OnItemEnter(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ILegendItem item })
        {
            Legend?.Hover(item);
        }
    }

    /// <summary>Says that the pointer left it.</summary>
    /// <param name="sender">The item's surface.</param>
    /// <param name="e">Not used.</param>
    private void OnItemLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ILegendItem item })
        {
            Legend?.Leave(item);
        }
    }
}
