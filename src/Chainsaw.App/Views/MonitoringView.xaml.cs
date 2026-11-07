using System.Windows.Controls;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The view Bevakning (sketch BV1a). Only wiring; what happens lives in <see cref="MonitoringViewModel"/>.
/// </summary>
public partial class MonitoringView : UserControl
{
    /// <summary>Creates the view.</summary>
    public MonitoringView() => InitializeComponent();

    /// <summary>Attaches the view model.</summary>
    /// <param name="model">The view model.</param>
    public void Attach(MonitoringViewModel model) => DataContext = model;
}
