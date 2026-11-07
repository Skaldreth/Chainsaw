using System.Windows.Controls;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The view Guide (F1, sketch GD1a in a simple form). Only wiring; what happens lives in <see cref="GuideViewModel"/>.
/// </summary>
public partial class GuideView : UserControl
{
    /// <summary>Creates the page.</summary>
    public GuideView() => InitializeComponent();

    /// <summary>Attaches the view model.</summary>
    /// <param name="model">The view model.</param>
    public void Attach(GuideViewModel model) => DataContext = model;
}
