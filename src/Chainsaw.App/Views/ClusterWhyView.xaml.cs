using System.Windows.Controls;

namespace Chainsaw.App.Views;

/// <summary>
/// The heuristics behind a chosen cluster in the inspector and drawing a version back (sketch KV1a). Only wiring;
/// the logic is in <see cref="ViewModels.ClusterWhyViewModel"/>.
/// </summary>
public partial class ClusterWhyView : UserControl
{
    /// <summary>Creates the view.</summary>
    public ClusterWhyView() => InitializeComponent();
}
