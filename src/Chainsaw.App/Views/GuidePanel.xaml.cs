using System.Windows.Controls;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The Guide's panel beside the work (sketch GD1b). Only wiring; what happens lives in
/// <see cref="GuidePanelViewModel"/>.
/// </summary>
public partial class GuidePanel : UserControl
{
    /// <summary>Creates the panel.</summary>
    public GuidePanel() => InitializeComponent();

    /// <summary>Attaches the panel's view model.</summary>
    /// <param name="model">The view model.</param>
    public void Attach(GuidePanelViewModel model) => DataContext = model;
}
