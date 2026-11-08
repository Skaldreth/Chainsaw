using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The view Tidslinje (sketch TL3a). Only wiring; what happens lives in <see cref="TimelineViewModel"/>.
/// </summary>
public partial class TimelineView : UserControl
{
    /// <summary>The view's own margin, as the sketch places it beside the pill.</summary>
    private static readonly Thickness RoomMargin = new(84, 16, 20, 16);

    /// <summary>Creates the view.</summary>
    public TimelineView()
    {
        InitializeComponent();
        Chart.SizeChanged += (_, _) => Model?.Resize(Chart.TimeWidth);
        Chart.BundleOpened += (_, bundle) => Model?.Open(bundle);
    }

    /// <summary>Gets the view model, once attached.</summary>
    private TimelineViewModel? Model => DataContext as TimelineViewModel;

    /// <summary>Attaches the view model.</summary>
    /// <param name="model">The view model.</param>
    public void Attach(TimelineViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        model.CopyWanted += (_, text) => CopyText(text);
        model.Resize(Chart.TimeWidth);
    }

    /// <summary>Makes room on the right for the Guide's panel, so that the inspector stays in sight.</summary>
    /// <param name="covered">How wide the panel covering the right edge is.</param>
    public void MakeRoom(double covered) =>
        Room.Margin = new Thickness(RoomMargin.Left, RoomMargin.Top, RoomMargin.Right + covered, RoomMargin.Bottom);

    /// <summary>
    /// G shows the chosen event in the graph, Ctrl+− shows the days and Ctrl++ the hours, and ← and → step to the
    /// previous and the next event.
    /// </summary>
    /// <param name="e">The key.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Handled || Model is not { } model)
        {
            return;
        }

        e.Handled = (Keyboard.Modifiers, e.Key) switch
        {
            (ModifierKeys.Control, Key.OemMinus or Key.Subtract) => Run(model.ShowDaysCommand),
            (ModifierKeys.Control, Key.OemPlus or Key.Add) => Run(model.ShowHoursCommand),
            (ModifierKeys.None, Key.G) => Run(model.ShowInGraphCommand),
            (ModifierKeys.None, Key.Left) => Step(model, false),
            (ModifierKeys.None, Key.Right) => Step(model, true),
            _ => false,
        };
    }

    /// <summary>Runs a command when it can run.</summary>
    /// <param name="command">The command.</param>
    /// <returns>Whether it ran.</returns>
    private static bool Run(ICommand command)
    {
        if (!command.CanExecute(null))
        {
            return false;
        }

        command.Execute(null);
        return true;
    }

    /// <summary>Steps to the previous or next event.</summary>
    /// <param name="model">The view model.</param>
    /// <param name="forward">Whether to step forward.</param>
    /// <returns>Always <see langword="true"/>: the arrow is the view's.</returns>
    private static bool Step(TimelineViewModel model, bool forward)
    {
        model.Step(forward);
        return true;
    }

    /// <summary>Copies a fact to the clipboard; a clipboard held by another program is left alone.</summary>
    /// <param name="text">The text.</param>
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
