using System.Windows;
using System.Windows.Media;

namespace Chainsaw.App.Controls;

/// <summary>
/// A cluster before and after as one bar (sketch KV1a): the track, the part that stays, and the part that changes, in
/// yellow and dashed when it leaves and in green when it comes back.
/// </summary>
public sealed class ChangeBarView : FrameworkElement
{
    /// <summary>The part of the track that stays, from 0 to 1.</summary>
    public static readonly DependencyProperty KeptProperty = DependencyProperty.Register(
        nameof(Kept), typeof(double), typeof(ChangeBarView), Rendered(0.0));

    /// <summary>The part of the track that changes, from 0 to 1.</summary>
    public static readonly DependencyProperty ChangedProperty = DependencyProperty.Register(
        nameof(Changed), typeof(double), typeof(ChangeBarView), Rendered(0.0));

    /// <summary>Whether the cluster grows: the change is green rather than yellow and dashed.</summary>
    public static readonly DependencyProperty GrowsProperty = DependencyProperty.Register(
        nameof(Grows), typeof(bool), typeof(ChangeBarView), Rendered(false));

    /// <summary>Gives the metadata of a property that the bar is drawn anew for.</summary>
    /// <param name="value">The default value.</param>
    /// <returns>The metadata.</returns>
    private static FrameworkPropertyMetadata Rendered(object value) =>
        new(value, FrameworkPropertyMetadataOptions.AffectsRender);

    /// <summary>Gets or sets the part that stays.</summary>
    public double Kept
    {
        get => (double)GetValue(KeptProperty);
        set => SetValue(KeptProperty, value);
    }

    /// <summary>Gets or sets the part that changes.</summary>
    public double Changed
    {
        get => (double)GetValue(ChangedProperty);
        set => SetValue(ChangedProperty, value);
    }

    /// <summary>Gets or sets whether the cluster grows.</summary>
    public bool Grows
    {
        get => (bool)GetValue(GrowsProperty);
        set => SetValue(GrowsProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var width = ActualWidth;
        var height = ActualHeight;
        var radius = height / 2;
        var track = new Rect(0, 0, width, height);
        drawingContext.DrawRoundedRectangle(Find("Brush.SurfaceSelected"), null, track, radius, radius);
        var kept = Math.Clamp(Kept, 0, 1) * width;
        drawingContext.DrawRoundedRectangle(Find("Brush.TextMuted"), null, new Rect(0, 0, kept, height), radius, radius);
        var changed = Math.Clamp(Changed, 0, 1) * width;
        if (changed <= 0)
        {
            return;
        }

        var ink = Find(Grows ? "Brush.Success" : "Brush.Kind.Spent");
        var part = new Rect(kept, 0, Math.Min(changed, width - kept), height);
        if (Grows)
        {
            drawingContext.DrawRoundedRectangle(ink, null, part, radius, radius);
            return;
        }

        var dashed = new Pen(ink, 1) { DashStyle = new DashStyle([3, 2], 0) };
        part.Inflate(-0.5, -0.5);
        drawingContext.DrawRoundedRectangle(null, dashed, part, radius, radius);
    }

    /// <summary>Finds a brush in the theme.</summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The brush, or transparent when it is missing.</returns>
    private Brush Find(string key) => TryFindResource(key) as Brush ?? Brushes.Transparent;
}
