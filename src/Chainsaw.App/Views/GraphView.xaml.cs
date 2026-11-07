using System.Windows.Controls;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The graph's page (sketch GR4): the surface, its corner's buttons, the legend and the inspector. Only wiring; what
/// happens lives in <see cref="GraphViewModel"/>.
/// </summary>
public partial class GraphView : UserControl
{
    /// <summary>Creates the page.</summary>
    public GraphView() => InitializeComponent();

    /// <summary>Attaches the graph's view model to the page and its surface.</summary>
    /// <param name="model">The view model.</param>
    public void Attach(GraphViewModel model)
    {
        DataContext = model;
        Canvas.Attach(model);
    }
}
