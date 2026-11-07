using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Chainsaw.App.Controls;

/// <summary>
/// A ring that grows out from what it lies around and fades, again and again, like sonar: it marks the step the user
/// stands at in the empty graph (sketch GR6c). It stands still, faint, when Windows has animations turned off, and
/// runs only while it is shown.
/// </summary>
public sealed class SonarRing : Border
{
    /// <summary>How long one wave takes.</summary>
    private static readonly Duration Wave = new(TimeSpan.FromSeconds(2.2));

    /// <summary>How far the ring grows, as a share of its size.</summary>
    private const double Growth = 1.12;

    /// <summary>How visible the ring is when it stands still.</summary>
    private const double StillOpacity = 0.3;

    /// <summary>The ring's scale, animated from the middle.</summary>
    private readonly ScaleTransform _scale = new(1, 1);

    /// <summary>Creates the ring, which never takes the mouse.</summary>
    public SonarRing()
    {
        IsHitTestVisible = false;
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _scale;
        IsVisibleChanged += (_, _) => Run();

        // Windows' setting is a static event, so the ring listens only while it is in the window and is never held.
        Loaded += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged += OnSystemChanged;
            Run();
        };
        Unloaded += (_, _) =>
        {
            SystemParameters.StaticPropertyChanged -= OnSystemChanged;
            Stop();
        };
    }

    /// <summary>Starts or stops the wave after Windows' animations were turned on or off.</summary>
    /// <param name="sender">The system parameters.</param>
    /// <param name="e">Which parameter changed.</param>
    private void OnSystemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
        {
            Dispatcher.BeginInvoke(Run);
        }
    }

    /// <summary>Runs the wave while the ring is shown and animations are on; otherwise it stands still.</summary>
    private void Run()
    {
        if (!IsLoaded || !IsVisible || !SystemParameters.ClientAreaAnimation)
        {
            Stop();
            return;
        }

        var grow = new DoubleAnimation(1, Growth, Wave) { RepeatBehavior = RepeatBehavior.Forever };
        var fade = new DoubleAnimation(0.7, 0, Wave) { RepeatBehavior = RepeatBehavior.Forever };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Stops the wave and lets the ring stand still, faint.</summary>
    private void Stop()
    {
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        BeginAnimation(OpacityProperty, null);
        Opacity = StillOpacity;
    }
}
