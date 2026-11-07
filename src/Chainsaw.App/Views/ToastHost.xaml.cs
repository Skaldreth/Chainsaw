using System.Windows;
using System.Windows.Controls;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>The toaster's place at the lower right (sketch TO1a). Only wiring; the stack lives in <see cref="Toaster"/>.</summary>
public partial class ToastHost : UserControl
{
    /// <summary>Creates the host.</summary>
    public ToastHost() => InitializeComponent();

    /// <summary>Attaches the toaster.</summary>
    /// <param name="toaster">The toaster.</param>
    public void Attach(Toaster toaster) => DataContext = toaster;
}
