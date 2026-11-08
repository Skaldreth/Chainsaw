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
    public void Attach(GuidePanelViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        model.CopyWanted += (_, text) => CopyText(text);
    }

    /// <summary>Puts an id on the clipboard; a clipboard held by another program is left alone.</summary>
    /// <param name="text">The id.</param>
    private static void CopyText(string text) => Interop.CaseClipboard.TrySetText(text);

    /// <summary>
    /// Shows the fade at the bottom while there is more text below, and lets it go at the end, so that the reader sees
    /// that the text goes on instead of a hard cut.
    /// </summary>
    /// <param name="sender">The scroller.</param>
    /// <param name="e">The change.</param>
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        const double end = 1;
        MoreBelow.Opacity = Scroller.VerticalOffset < Scroller.ScrollableHeight - end ? 1 : 0;
    }
}
