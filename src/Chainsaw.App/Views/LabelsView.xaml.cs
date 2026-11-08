using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The view Etiketter (sketch ET1a). Only wiring: the keys G, the arrows and Esc, the field for the reason, and copying
/// the address; what happens lives in <see cref="LabelsViewModel"/>.
/// </summary>
public partial class LabelsView : UserControl
{
    /// <summary>Creates the view.</summary>
    public LabelsView() => InitializeComponent();

    /// <summary>Gets the view model.</summary>
    private LabelsViewModel? Model => DataContext as LabelsViewModel;

    /// <summary>Attaches the view model, and puts the keyboard in the field for the reason when it folds out.</summary>
    /// <param name="model">The view model.</param>
    public void Attach(LabelsViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        DataContext = model;
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LabelsViewModel.IsRetracting) && model.IsRetracting)
            {
                Dispatcher.BeginInvoke(() => Reason.Focus(), DispatcherPriority.Input);
            }
        };
    }

    /// <summary>
    /// The view's keys outside a text field: G shows the chosen label in the graph, the arrows move the choice, and Esc
    /// folds the field for taking back in.
    /// </summary>
    /// <param name="e">The key.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Handled || Model is not { } model || e.OriginalSource is TextBoxBase
            || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        e.Handled = e.Key switch
        {
            Key.G => Run(model.ShowInGraphCommand, null),
            Key.Down => Step(model, 1),
            Key.Up => Step(model, -1),
            Key.Escape when model.IsRetracting => Run(model.CancelRetractCommand, null),
            _ => false,
        };
    }

    /// <summary>Runs a command when it can run.</summary>
    /// <param name="command">The command.</param>
    /// <param name="parameter">Its parameter.</param>
    /// <returns><see langword="true"/> when it ran.</returns>
    private static bool Run(ICommand command, object? parameter)
    {
        if (!command.CanExecute(parameter))
        {
            return false;
        }

        command.Execute(parameter);
        return true;
    }

    /// <summary>Moves the choice one row.</summary>
    /// <param name="model">The view model.</param>
    /// <param name="step">One down, or minus one up.</param>
    /// <returns>Always <see langword="true"/>: the arrows belong to the list.</returns>
    private static bool Step(LabelsViewModel model, int step)
    {
        model.Move(step);
        return true;
    }

    /// <summary>Enter takes the label back, Esc folds the field in, in the field for the reason.</summary>
    /// <param name="sender">The field.</param>
    /// <param name="e">The key.</param>
    private void OnReasonKey(object sender, KeyEventArgs e)
    {
        if (Model is not { } model)
        {
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = Run(model.ConfirmRetractCommand, null);
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = Run(model.CancelRetractCommand, null);
            Focus();
        }
    }

    /// <summary>Copies the chosen label's whole address.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnCopyAddress(object sender, RoutedEventArgs e)
    {
        if (Model?.Selected is { } row)
        {
            Clipboard.SetText(row.FullAddress);
        }
    }
}
