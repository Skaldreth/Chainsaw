using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// One toast (sketch TO1a): it slides in from the right, counts down while it is not hovered when it fades by itself,
/// and fades out when it leaves, after which the toaster takes it away. With Windows' animations off it appears and
/// goes at once, without sliding.
/// </summary>
public partial class ToastCard : UserControl
{
    /// <summary>How far the toast slides in from.</summary>
    private const double SlideFrom = 24;

    /// <summary>How long the slide in takes.</summary>
    private static readonly Duration SlideTime = new(TimeSpan.FromMilliseconds(280));

    /// <summary>How long the fade out takes.</summary>
    private static readonly Duration FadeTime = new(TimeSpan.FromMilliseconds(220));

    /// <summary>The countdown of a toast that fades by itself; paused while the pointer is over it.</summary>
    private Storyboard? _countdown;

    /// <summary>Creates the toast.</summary>
    public ToastCard()
    {
        InitializeComponent();
        Loaded += (_, _) => Arrive();
        DataContextChanged += OnDataContextChanged;
        MouseEnter += (_, _) => _countdown?.Pause(this);
        MouseLeave += (_, _) => _countdown?.Resume(this);
    }

    /// <summary>Gets the toaster that takes the toast away once it has faded: the host's.</summary>
    private Toaster? Owner =>
        System.Windows.Media.VisualTreeHelper.GetParent(this) is not null
            ? FindHost(this)?.DataContext as Toaster
            : null;

    /// <summary>Gets the toast shown.</summary>
    private Toast? Toast => DataContext as Toast;

    /// <summary>Gets whether Windows lets programs animate.</summary>
    private static bool Animates => SystemParameters.ClientAreaAnimation;

    /// <summary>Follows the toast's way out.</summary>
    /// <param name="sender">The card.</param>
    /// <param name="e">The old and new toast.</param>
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is Toast old)
        {
            old.PropertyChanged -= OnToastChanged;
        }

        if (e.NewValue is Toast toast)
        {
            toast.PropertyChanged += OnToastChanged;
        }
    }

    /// <summary>Fades the toast out when it starts to leave.</summary>
    /// <param name="sender">The toast.</param>
    /// <param name="e">The property.</param>
    private void OnToastChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ViewModels.Toast.IsLeaving) && Toast is { IsLeaving: true })
        {
            Leave();
        }
    }

    /// <summary>Slides the toast in and starts its countdown.</summary>
    private void Arrive()
    {
        if (Toast is not { } toast)
        {
            return;
        }

        if (toast.IsLeaving)
        {
            Leave();
            return;
        }

        if (Animates)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Slide.BeginAnimation(
                System.Windows.Media.TranslateTransform.XProperty,
                new DoubleAnimation(SlideFrom, 0, SlideTime) { EasingFunction = ease });
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, SlideTime));
        }

        if (!toast.IsSticky)
        {
            StartCountdown(toast.TimeShown);
        }
    }

    /// <summary>Runs the thin line in the bottom edge down; when it is out, the toast closes.</summary>
    /// <param name="time">How long the toast stands.</param>
    private void StartCountdown(TimeSpan time)
    {
        var shrink = new DoubleAnimation(1, 0, new Duration(time));
        Storyboard.SetTarget(shrink, Bar);
        Storyboard.SetTargetProperty(
            shrink, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
        _countdown = new Storyboard();
        _countdown.Children.Add(shrink);
        _countdown.Completed += (_, _) => Toast?.Close();
        _countdown.Begin(this, isControllable: true);
    }

    /// <summary>Fades the toast out, then has the toaster take it away.</summary>
    private void Leave()
    {
        _countdown?.Stop(this);
        _countdown = null;
        IsHitTestVisible = false;
        if (!Animates || !IsLoaded)
        {
            Remove();
            return;
        }

        var fade = new DoubleAnimation(0, FadeTime);
        fade.Completed += (_, _) => Remove();
        Root.BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Finds the host the card stands in.</summary>
    /// <param name="from">Where to start.</param>
    /// <returns>The host, or <see langword="null"/>.</returns>
    private static ToastHost? FindHost(DependencyObject from)
    {
        for (var at = from; at is not null; at = System.Windows.Media.VisualTreeHelper.GetParent(at))
        {
            if (at is ToastHost host)
            {
                return host;
            }
        }

        return null;
    }

    /// <summary>Takes the toast away from the toaster.</summary>
    private void Remove()
    {
        if (Toast is { } toast)
        {
            toast.PropertyChanged -= OnToastChanged;
            Owner?.Remove(toast);
        }
    }
}
