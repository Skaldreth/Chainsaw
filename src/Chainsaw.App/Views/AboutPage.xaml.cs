using System.Windows;
using System.Windows.Controls;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// <em>Om Chainsaw</em> (sketch ST2a), the same in the settings and over the lock screen. The view only puts the
/// fingerprint on the clipboard; what is shown lives in <see cref="AboutViewModel"/>.
/// </summary>
public partial class AboutPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public AboutPage() => InitializeComponent();

    /// <summary>Copies the fingerprint, in its groups, and says so.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnCopy(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AboutViewModel { HasKey: true } about)
        {
            return;
        }

        try
        {
            Clipboard.SetText(about.Fingerprint);
            about.MarkCopied();
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // The clipboard is held by another program; the button stays Kopiera.
        }
    }
}
