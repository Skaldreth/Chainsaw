using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chainsaw.App.Controls;

/// <summary>
/// A panel at the window's right edge that lies <em>under</em> the work (sketch IN5a), as the graph's inspector does:
/// the darker underlay, the work's shadow falling over its left edge, and the work's rounded corner at its foot. Every
/// inspector-like panel (the Guide beside the work, a view's chosen item) is one, so they all read as sunk under the
/// sheet. It tells the window how far in from the right edge it reaches, so the title bar ends where it starts and the
/// window's buttons stand on it.
/// </summary>
public sealed class UnderPanel : Decorator
{
    /// <summary>The room around the content: more on the left than the shadow is wide, so no text lies in it.</summary>
    public static readonly DependencyProperty PaddingProperty = DependencyProperty.Register(
        nameof(Padding),
        typeof(Thickness),
        typeof(UnderPanel),
        new FrameworkPropertyMetadata(new Thickness(40, 0, 26, 0), FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>How far the work's shadow falls over the panel, in pixels.</summary>
    private const double ShadowWidth = 32;

    /// <summary>The work's shadow at its darkest, at the panel's left edge.</summary>
    private const byte ShadowAlpha = 110;

    /// <summary>The work's rounded corner at the panel's foot, as the graph's sheet's.</summary>
    private const double Corner = 14;

    /// <summary>The panels shown now, so the window can find how far in they reach.</summary>
    private static readonly HashSet<UnderPanel> Shown = [];

    /// <summary>The work's shadow over the left edge, made once.</summary>
    private static readonly Brush Shadow = MakeShadow();

    /// <summary>Creates the panel; it is shown and counted while it is loaded and visible.</summary>
    public UnderPanel()
    {
        Loaded += (_, _) => Follow();
        Unloaded += (_, _) => Follow();
        IsVisibleChanged += (_, _) => Follow();
        SizeChanged += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Occurs when a panel is shown, hidden or changes size, so the window can let the title bar follow.</summary>
    public static event EventHandler? Changed;

    /// <summary>Gets or sets the room around the content.</summary>
    public Thickness Padding
    {
        get => (Thickness)GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// Gives how far in from a root's right edge the panels shown in it reach: only panels that stand against its right
    /// edge count.
    /// </summary>
    /// <param name="root">The window's content.</param>
    /// <returns>The reach, or 0 when none is shown.</returns>
    public static double Reach(FrameworkElement root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var reach = 0.0;
        foreach (var panel in Shown)
        {
            if (!panel.IsVisible || panel.ActualWidth < 1 || !root.IsAncestorOf(panel))
            {
                continue;
            }

            var left = panel.TransformToAncestor(root).Transform(default).X;
            if (left + panel.ActualWidth >= root.ActualWidth - 1)
            {
                reach = Math.Max(reach, root.ActualWidth - left);
            }
        }

        return reach;
    }

    /// <summary>Measures the content inside the room.</summary>
    /// <param name="constraint">The space offered.</param>
    /// <returns>The size wanted.</returns>
    protected override Size MeasureOverride(Size constraint)
    {
        var room = Padding;
        var across = room.Left + room.Right;
        var down = room.Top + room.Bottom;
        if (Child is null)
        {
            return new Size(across, down);
        }

        Child.Measure(new Size(Math.Max(0, constraint.Width - across), Math.Max(0, constraint.Height - down)));
        return new Size(Child.DesiredSize.Width + across, Child.DesiredSize.Height + down);
    }

    /// <summary>Places the content inside the room.</summary>
    /// <param name="arrangeSize">The space given.</param>
    /// <returns>The space used.</returns>
    protected override Size ArrangeOverride(Size arrangeSize)
    {
        var room = Padding;
        Child?.Arrange(new Rect(
            room.Left,
            room.Top,
            Math.Max(0, arrangeSize.Width - room.Left - room.Right),
            Math.Max(0, arrangeSize.Height - room.Top - room.Bottom)));
        return arrangeSize;
    }

    /// <summary>Draws the underlay, the work's shadow over the left edge and the work's rounded corner at the foot.</summary>
    /// <param name="drawingContext">The drawing context.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = RenderSize;
        drawingContext.DrawRectangle(TryFindResource("Brush.Underlay") as Brush, null, new Rect(size));
        drawingContext.DrawRectangle(Shadow, null, new Rect(0, 0, Math.Min(ShadowWidth, size.Width), size.Height));
        if (size.Height > Corner && TryFindResource("Brush.Background") is Brush work)
        {
            // The space outside a quarter circle at the foot's left corner, in the work's ground: the work's corner.
            var foot = new StreamGeometry();
            using (var context = foot.Open())
            {
                context.BeginFigure(new Point(0, size.Height - Corner), true, true);
                context.ArcTo(
                    new Point(Corner, size.Height), new Size(Corner, Corner), 0, false, SweepDirection.Counterclockwise,
                    true, true);
                context.LineTo(new Point(0, size.Height), true, true);
            }

            foot.Freeze();
            drawingContext.DrawGeometry(work, null, foot);
        }
    }

    /// <summary>Makes the work's shadow: dark at the left edge, gone a shadow's width in.</summary>
    /// <returns>The frozen brush.</returns>
    private static Brush MakeShadow()
    {
        var brush = new LinearGradientBrush(
            [new GradientStop(Color.FromArgb(ShadowAlpha, 0, 0, 0), 0), new GradientStop(Color.FromArgb(0, 0, 0, 0), 1)],
            new Point(0, 0),
            new Point(1, 0));
        brush.Freeze();
        return brush;
    }

    /// <summary>Counts the panel while it is loaded and visible, and tells the window.</summary>
    private void Follow()
    {
        if (IsLoaded && IsVisible)
        {
            Shown.Add(this);
        }
        else
        {
            Shown.Remove(this);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
