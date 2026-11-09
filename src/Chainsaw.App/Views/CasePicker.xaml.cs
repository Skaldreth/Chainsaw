using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The case picker under the case in the title bar (sketch ÄV1a). The view only glides in, wires the keys and keeps
/// the chosen row in sight; what happens lives in <see cref="CasePickerViewModel"/>.
/// </summary>
public partial class CasePicker : UserControl
{
    /// <summary>How long the picker takes to glide in.</summary>
    private static readonly Duration GlideTime = new(TimeSpan.FromMilliseconds(140));

    /// <summary>How far above its place the picker starts when it glides in, in pixels.</summary>
    private const double GlideDistance = -6;

    /// <summary>The picker's view model, once it is given.</summary>
    private CasePickerViewModel? _picker;

    /// <summary>Creates the picker.</summary>
    public CasePicker()
    {
        InitializeComponent();
    }

    /// <summary>Gives the picker its view model, and follows it to glide in and keep the chosen row in sight.</summary>
    /// <param name="picker">The view model.</param>
    public void Attach(CasePickerViewModel picker)
    {
        _picker = picker;
        DataContext = picker;
        picker.PropertyChanged += OnPickerChanged;
    }

    /// <summary>Puts the caret in the search field, once the picker is laid out.</summary>
    public void TakeFocus() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            if (_picker is { IsOpen: true })
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
        });

    /// <summary>Glides in when the picker opens, and brings the chosen row into sight when the choice moves.</summary>
    /// <param name="sender">The view model.</param>
    /// <param name="e">The property that changed.</param>
    private void OnPickerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CasePickerViewModel.IsOpen) && _picker is { IsOpen: true })
        {
            GlideIn();
        }
        else if (e.PropertyName == nameof(CasePickerViewModel.Chosen))
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, ShowChosen);
        }
    }

    /// <summary>Fades the picker in from a few pixels above, unless Windows has animations turned off.</summary>
    private void GlideIn()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            BeginAnimation(OpacityProperty, null);
            Glide.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty, null);
            return;
        }

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, GlideTime) { EasingFunction = ease });
        Glide.BeginAnimation(
            System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(GlideDistance, 0, GlideTime) { EasingFunction = ease });
    }

    /// <summary>Scrolls the list so that the chosen row is in sight, when its row is laid out.</summary>
    private void ShowChosen()
    {
        if (_picker?.Chosen is { } chosen
            && List.ItemContainerGenerator.ContainerFromItem(chosen) is FrameworkElement row)
        {
            row.BringIntoView();
        }
    }

    /// <summary>Up and Down move the choice, Enter opens the chosen case and Esc folds the picker in.</summary>
    /// <param name="sender">The picker.</param>
    /// <param name="e">The key.</param>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (_picker is not { } picker)
        {
            return;
        }

        // With a link at the top, Ctrl+N starts a new case with it (sketch LN1a), not an empty one.
        if (picker.HasLink && e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            picker.NewCase();
            e.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Up:
                picker.Move(-1);
                break;
            case Key.Down:
                picker.Move(1);
                break;
            case Key.Enter when e.OriginalSource is not ButtonBase:
                picker.OpenChosenCommand.Execute(null);
                break;
            case Key.Escape:
                picker.Close();
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
