using System.Windows.Controls;

namespace Chainsaw.App.Views;

/// <summary>
/// The checks and suggestions one at a time (sketch KF2b), where the inspector stands. Only wiring; what happens lives in
/// <see cref="ViewModels.ChecksViewModel"/>.
/// </summary>
public partial class ChecksPanel : UserControl
{
    /// <summary>Creates the panel.</summary>
    public ChecksPanel() => InitializeComponent();
}
