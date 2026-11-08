using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The message in the middle of the screen (sketch BV4a): a window of its own, on top of every other, with the card
/// and nothing darkened behind it. Esc closes it, Enter takes its action: <em>Visa</em> in the settings' preview,
/// otherwise it starts Chainsaw, or brings the running one forward, and closes. The rings stand still when Windows
/// has its animations turned off.
/// </summary>
public partial class AlertCardWindow : Window
{
    /// <summary>How long one ring takes to run out.</summary>
    private static readonly Duration RingTime = new(TimeSpan.FromSeconds(2.2));

    /// <summary>How far a ring grows.</summary>
    private const double RingScale = 2.1;

    /// <summary>The card.</summary>
    private readonly AlertCard _card;

    /// <summary>Called by the preview's action, or <see langword="null"/>.</summary>
    private readonly Action? _show;

    /// <summary>Creates the window for a card.</summary>
    /// <param name="card">The card.</param>
    /// <param name="show">What the preview's <em>Visa</em> does, or <see langword="null"/> to only close.</param>
    public AlertCardWindow(AlertCard card, Action? show = null)
    {
        _card = card;
        _show = show;
        InitializeComponent();
        DataContext = card;
        Loaded += (_, _) =>
        {
            StartRings();
            ActionButton.Focus();
        };
    }

    /// <summary>Lets every ring run out from the eye in turn, a third of the time apart.</summary>
    private void StartRings()
    {
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        Ellipse[] rings = [RingA, RingB, RingC];
        for (var index = 0; index < rings.Length; index++)
        {
            var delay = TimeSpan.FromTicks(RingTime.TimeSpan.Ticks * index / rings.Length);
            var grow = new DoubleAnimation(0.6, RingScale, RingTime)
            {
                BeginTime = delay,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            var fade = new DoubleAnimation(0.75, 0, RingTime)
            {
                BeginTime = delay,
                RepeatBehavior = RepeatBehavior.Forever,
            };
            var scale = (ScaleTransform)rings[index].RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            rings[index].BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>Esc closes; Enter takes the action.</summary>
    /// <param name="sender">The window.</param>
    /// <param name="e">The key.</param>
    private void OnKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
        else if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Act();
        }
    }

    /// <summary>Lets the card be moved by its face.</summary>
    /// <param name="sender">The card.</param>
    /// <param name="e">The press.</param>
    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not Button && e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    /// <summary>Closes the card.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnClose(object sender, RoutedEventArgs e) => Close();

    /// <summary>Takes the card's action.</summary>
    /// <param name="sender">The button.</param>
    /// <param name="e">The click.</param>
    private void OnAction(object sender, RoutedEventArgs e) => Act();

    /// <summary>
    /// Shows Bevakning in the preview, or starts Chainsaw without arguments, which brings a running one forward (it
    /// hands over an empty link), and closes.
    /// </summary>
    private void Act()
    {
        if (_card.OpensChainsaw && Environment.ProcessPath is { } program)
        {
            try
            {
                Process.Start(new ProcessStartInfo(program) { UseShellExecute = false })?.Dispose();
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException)
            {
                // Chainsaw could not be started; the card closes, and Chainsaw is opened as usual.
            }
        }
        else
        {
            _show?.Invoke();
        }

        Close();
    }
}
