using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The exits' panel under the exit in the title bar (sketch UT1a). The view only wires the keys and the keyboard's
/// place; what happens lives in <see cref="ExitPanelViewModel"/>. The WireGuard file's text never reaches the view.
/// </summary>
public partial class ExitPanel : UserControl
{
    /// <summary>The panel's view model, once it is given.</summary>
    private ExitPanelViewModel? _panel;

    /// <summary>Creates the panel.</summary>
    public ExitPanel()
    {
        InitializeComponent();
    }

    /// <summary>Gives the panel its view model, and follows its steps to move the keyboard.</summary>
    /// <param name="panel">The view model.</param>
    public void Attach(ExitPanelViewModel panel)
    {
        _panel = panel;
        DataContext = panel;
        panel.PropertyChanged += OnPanelChanged;
    }

    /// <summary>Gives the keyboard to the panel's first control, once it is laid out.</summary>
    public void TakeFocus() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_panel is { IsOpen: true })
            {
                FocusFirst();
            }
        });

    /// <summary>Moves the keyboard to the name when the step that adds a file opens, and back after.</summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">The property that changed.</param>
    private void OnPanelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ExitPanelViewModel.IsAdding) || _panel is not { IsOpen: true } panel)
        {
            return;
        }

        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (panel.IsAdding)
            {
                NameBox.Focus();
                NameBox.SelectAll();
            }
            else if (panel.IsOpen)
            {
                AddButton.Focus();
            }
        });
    }

    /// <summary>Gives the keyboard to the first exit's button, or to <em>Lägg till WireGuard-fil …</em>.</summary>
    private void FocusFirst()
    {
        if (!MoveFocus(new TraversalRequest(FocusNavigationDirection.First)))
        {
            AddButton.Focus();
        }
    }

    /// <summary>Esc leaves the step that adds a file, or folds the panel in.</summary>
    /// <param name="sender">The panel.</param>
    /// <param name="e">The key.</param>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || _panel is not { } panel)
        {
            return;
        }

        if (panel.IsAdding)
        {
            panel.CancelAdd();
        }
        else
        {
            panel.Close();
        }

        e.Handled = true;
    }
}
