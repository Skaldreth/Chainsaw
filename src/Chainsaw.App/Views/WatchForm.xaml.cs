using System.Windows.Controls;

namespace Chainsaw.App.Views;

/// <summary>
/// The watch form (sketches BV1a and BV2a). Only wiring; what happens lives in
/// <see cref="ViewModels.WatchFormViewModel"/>.
/// </summary>
public partial class WatchForm : UserControl
{
    /// <summary>Creates the form.</summary>
    public WatchForm() => InitializeComponent();
}
