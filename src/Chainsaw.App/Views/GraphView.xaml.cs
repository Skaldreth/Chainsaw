using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The graph's page (sketch GR4): the surface, its corner's buttons, the legend and the inspector. Only wiring; what
/// happens lives in <see cref="GraphViewModel"/>.
/// </summary>
public partial class GraphView : UserControl
{
    /// <summary>The corner's space to the inspector when nothing lies over the page.</summary>
    private static readonly Thickness CornerMargin = new(0, 12, 16, 0);

    /// <summary>The node's menu's height, for keeping it inside the page.</summary>
    private const double MenuHeight = 220;

    /// <summary>Creates the page.</summary>
    public GraphView() => InitializeComponent();

    /// <summary>The inspector's width in sketch GR4, in device-independent pixels.</summary>
    private const double InspectorWidth = 280;

    /// <summary>The narrowest the inspector can be dragged to.</summary>
    private const double NarrowestInspector = 240;

    /// <summary>The widest the inspector can be dragged to, as a share of the page.</summary>
    private const double WidestInspectorShare = 0.5;

    /// <summary>The inspector's width as the user last dragged it, kept while Chainsaw runs.</summary>
    private double _inspectorWidth = InspectorWidth;

    /// <summary>
    /// Moves the graph's corner (the switch, the method and the amounts) out from under a panel that lies over the
    /// page's right side, such as the Guide's (sketch GD1b), so that it can still be seen and used.
    /// </summary>
    /// <param name="covered">How far from the page's right edge the panel reaches; 0 when none is open.</param>
    public void MakeRoom(double covered)
    {
        var inspector = InspectorColumn.Width.Value;
        var beyond = Math.Max(0, covered - inspector);
        Corner.Margin = CornerMargin with { Right = CornerMargin.Right + beyond };
    }

    /// <summary>Attaches the graph's view model to the page and its surface.</summary>
    /// <param name="model">The view model.</param>
    public void Attach(GraphViewModel model)
    {
        DataContext = model;
        Canvas.Attach(model);
        model.Checks.PropertyChanged += (_, e) =>
        {
            // The list's keys (J, K, Enter, X and ?) and Z work in the graph; the keyboard goes back to it.
            if (e.PropertyName is nameof(ChecksViewModel.IsOpen) or nameof(ChecksViewModel.IsFormOpen)
                && !model.Checks.IsFormOpen)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(Canvas));
            }
        };
        model.PropertyChanged += (_, e) =>
        {
            // The node's menu opens where the right click was, inside the page.
            if (e.PropertyName == nameof(GraphViewModel.MenuNode) && model.IsMenuOpen)
            {
                var at = Canvas.MenuAt;
                NodeMenu.Margin = new Thickness(
                    Math.Max(0, Math.Min(at.X, Canvas.ActualWidth - NodeMenu.Width)),
                    Math.Max(0, Math.Min(at.Y, Canvas.ActualHeight - MenuHeight)), 0, 0);
            }

            if (e.PropertyName == nameof(GraphViewModel.IsWatchFormOpen) && !model.IsWatchFormOpen
                && IsKeyboardFocusWithin)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(Canvas));
            }
        };
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(GraphViewModel.HasCase))
            {
                ShowInspector(model.HasCase);
            }
        };
        ShowInspector(model.HasCase);
        PreviewKeyDown += (_, e) => e.Handled = e.Handled || PasteStartPoint(e, model.Start);
        model.CopyWanted += (_, text) => CopyText(text);
        model.Forms.PropertyChanged += (_, e) =>
        {
            // A closed form's field collapses; the keyboard goes back to the graph, so that E, F, L, U and K work.
            if (e.PropertyName == nameof(InspectorFormsViewModel.Form) && !model.Forms.IsOpen
                && IsKeyboardFocusWithin)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(Canvas));
            }
        };
    }

    /// <summary>Shows the inspector, or folds it away when no case is open and there is nothing to inspect.</summary>
    /// <param name="shown">Whether the inspector is shown.</param>
    private void ShowInspector(bool shown)
    {
        InspectorColumn.MinWidth = shown ? NarrowestInspector : 0;
        InspectorColumn.MaxWidth = shown ? Math.Max(NarrowestInspector, ActualWidth * WidestInspectorShare) : 0;
        InspectorColumn.Width = new GridLength(shown ? _inspectorWidth : 0);
        var visibility = shown ? Visibility.Visible : Visibility.Collapsed;
        Inspector.Visibility = visibility;
        InspectorShadow.Visibility = visibility;
        InspectorEdge.Visibility = visibility;
    }

    /// <summary>
    /// Tells the model that the menu of areas closed, after a click outside it or Esc, so that the next click on its
    /// button opens it again.
    /// </summary>
    /// <param name="sender">The menu.</param>
    /// <param name="e">Nothing.</param>
    private void OnAreasMenuClosed(object? sender, EventArgs e)
    {
        if (DataContext is GraphViewModel model)
        {
            model.Checks.CloseMenu();
        }
    }

    /// <summary>Keeps the width the inspector was dragged to, so that it comes back when the inspector is shown.</summary>
    /// <param name="sender">The inspector's edge.</param>
    /// <param name="e">The drag.</param>
    private void OnInspectorResized(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        _inspectorWidth = InspectorColumn.ActualWidth;
        Canvas.InvalidateVisual();
    }

    /// <summary>
    /// Ctrl+V anywhere on the empty graph puts the text in the start point's field (sketch GR6c); a text field that has
    /// the keyboard pastes as usual.
    /// </summary>
    /// <param name="e">The key.</param>
    /// <param name="start">The empty graph.</param>
    /// <returns><see langword="true"/> when the paste was taken.</returns>
    private bool PasteStartPoint(KeyEventArgs e, GraphStartViewModel start)
    {
        if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control || !start.IsNoStartPoint
            || Keyboard.FocusedElement is TextBox)
        {
            return false;
        }

        string text;
        try
        {
            text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another program holds the clipboard; nothing is pasted.
            return false;
        }

        if (!start.Paste(text))
        {
            return false;
        }

        StartView.FocusField();
        return true;
    }

    /// <summary>Copies a node's address to the clipboard; a clipboard held by another program is left alone.</summary>
    /// <param name="text">The address.</param>
    private static void CopyText(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another program holds the clipboard; nothing is lost but the copy.
        }
    }
}
