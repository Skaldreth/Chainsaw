using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The inspector's forms (sketch IN1a). Only wiring: when a form opens, its first field takes the keyboard, so that L,
/// U or K in the graph can be followed by typing at once; what happens lives in <see cref="InspectorFormsViewModel"/>.
/// </summary>
public partial class InspectorFormsView : UserControl
{
    /// <summary>The forms whose opening is followed.</summary>
    private InspectorFormsViewModel? _forms;

    /// <summary>Creates the forms' view.</summary>
    public InspectorFormsView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Follow(DataContext as InspectorFormsViewModel);
    }

    /// <summary>Follows the forms now shown.</summary>
    /// <param name="forms">The forms, or <see langword="null"/>.</param>
    private void Follow(InspectorFormsViewModel? forms)
    {
        if (_forms is not null)
        {
            _forms.PropertyChanged -= OnFormsChanged;
        }

        _forms = forms;
        if (_forms is not null)
        {
            _forms.PropertyChanged += OnFormsChanged;
        }
    }

    /// <summary>Gives the keyboard to the opened form's first field, once it is drawn.</summary>
    /// <param name="sender">The forms.</param>
    /// <param name="e">What changed.</param>
    private void OnFormsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(InspectorFormsViewModel.Form) || _forms is null)
        {
            return;
        }

        IInputElement? first = _forms.Form switch
        {
            InspectorForm.Label => NameBox,
            InspectorForm.Split => SplitReasonBox,
            InspectorForm.CrossChain => TxBox,
            _ => null,
        };
        if (first is not null)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Input, () => Keyboard.Focus(first));
        }
    }
}
