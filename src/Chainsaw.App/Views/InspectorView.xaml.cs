using System.Windows.Controls;

namespace Chainsaw.App.Views;

/// <summary>
/// The inspector beside the graph (sketches IN2b, GR5, TK1a and AN1b): pure reading, bound to the graph's view model.
/// Only wiring; what is shown is worked out in <see cref="Graph.InspectorContent"/> and
/// <see cref="ViewModels.GraphViewModel"/>.
/// </summary>
public partial class InspectorView : UserControl
{
    /// <summary>Creates the inspector's view.</summary>
    public InspectorView() => InitializeComponent();
}
