using System.Windows.Controls;

namespace Chainsaw.App.Views;

/// <summary>The trash's own view (sketch SO1a); only wiring, the view model holds what happens.</summary>
public sealed partial class TrashView : UserControl
{
    /// <summary>Creates the view.</summary>
    public TrashView() => InitializeComponent();
}
