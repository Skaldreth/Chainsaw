using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The small picture of the marking in the view Rapport (sketch RP1b): the graph as it stands, scaled to fit, with the
/// marked actors framed in the accent with a soft ring and the lines between them lit, and the rest faint. It only
/// draws; what is marked is decided in <see cref="ReportPicture"/>.
/// </summary>
public sealed class ReportMiniGraph : FrameworkElement
{
    /// <summary>Identifies <see cref="Picture"/>.</summary>
    public static readonly DependencyProperty PictureProperty = DependencyProperty.Register(
        nameof(Picture), typeof(ReportPicture), typeof(ReportMiniGraph),
        new FrameworkPropertyMetadata(ReportPicture.Empty, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>How faint what is not marked is drawn (sketch RP1b: lines .18, actors .3).</summary>
    private const double FaintLine = 0.18;

    /// <summary>How faint an actor that is not marked is drawn.</summary>
    private const double FaintNode = 0.3;

    /// <summary>The space kept inside the picture's edge.</summary>
    private const double Inset = 8;

    /// <summary>The corner of a square actor, in the graph's units (sketch GR4: 8).</summary>
    private const double SquareCorner = 8;

    /// <summary>The soft ring around a marked actor, in the graph's units.</summary>
    private const double RingReach = 7;

    /// <summary>Gets or sets the picture.</summary>
    public ReportPicture Picture
    {
        get => (ReportPicture)GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    /// <summary>Draws the picture, scaled to fit and centred.</summary>
    /// <param name="drawingContext">Where it is drawn.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var picture = Picture;
        var bounds = picture.Bounds;
        if (!picture.HasNodes || bounds.Width <= 0 || bounds.Height <= 0 || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var scale = Math.Min((ActualWidth - (2 * Inset)) / bounds.Width, (ActualHeight - (2 * Inset)) / bounds.Height);
        var dx = ((ActualWidth - (bounds.Width * scale)) / 2) - (bounds.X * scale);
        var dy = ((ActualHeight - (bounds.Height * scale)) / 2) - (bounds.Y * scale);
        drawingContext.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        drawingContext.PushTransform(new MatrixTransform(scale, 0, 0, scale, dx, dy));
        foreach (var line in picture.Lines)
        {
            DrawLine(drawingContext, line, scale);
        }

        foreach (var node in picture.Nodes)
        {
            DrawNode(drawingContext, node, scale);
        }

        drawingContext.Pop();
        drawingContext.Pop();
    }

    /// <summary>Draws a line as a soft curve from port to port.</summary>
    /// <param name="dc">Where it is drawn.</param>
    /// <param name="line">The line.</param>
    /// <param name="scale">The picture's scale, which keeps the stroke thin on the screen.</param>
    private void DrawLine(DrawingContext dc, PictureLine line, double scale)
    {
        var middle = (line.From.X + line.To.X) / 2;
        var figure = new PathFigure { StartPoint = new Point(line.From.X, line.From.Y) };
        figure.Segments.Add(new BezierSegment(
            new Point(middle, line.From.Y), new Point(middle, line.To.Y), new Point(line.To.X, line.To.Y), true));
        var geometry = new PathGeometry([figure]);
        var brush = Find(line.IsTraced ? "Brush.Accent" : "Brush.Graph.Untraced");
        dc.PushOpacity(line.IsMarked ? 1 : FaintLine);
        dc.DrawGeometry(null, new Pen(brush, (line.IsMarked ? 2 : 1.2) / scale), geometry);
        dc.Pop();
    }

    /// <summary>Draws an actor: its frame in its sort's colour, the accent and a soft ring when it is marked.</summary>
    /// <param name="dc">Where it is drawn.</param>
    /// <param name="node">The actor.</param>
    /// <param name="scale">The picture's scale.</param>
    private void DrawNode(DrawingContext dc, PictureNode node, double scale)
    {
        var frame = new Rect(node.Frame.X, node.Frame.Y, node.Frame.Width, node.Frame.Height);
        var corner = node.IsRound ? frame.Height / 2 : SquareCorner;
        var accent = Find("Brush.Accent");
        if (node.IsMarked)
        {
            var ring = new Rect(frame.X - RingReach, frame.Y - RingReach, frame.Width + (2 * RingReach),
                frame.Height + (2 * RingReach));
            dc.PushOpacity(0.35);
            dc.DrawRoundedRectangle(null, new Pen(accent, 3 / scale), ring, corner + RingReach, corner + RingReach);
            dc.Pop();
        }

        var pen = new Pen(node.IsMarked ? accent : Find(GraphPalette.KeyOf(node.Ink)), 1.5 / scale);
        if (node.IsDashed)
        {
            pen.DashStyle = new DashStyle([3, 3], 0);
        }

        dc.PushOpacity(node.IsMarked ? 1 : FaintNode);
        dc.DrawRoundedRectangle(Find("Brush.Surface"), pen, frame, corner, corner);
        dc.Pop();
    }

    /// <summary>Finds a brush of the theme.</summary>
    /// <param name="key">Its key.</param>
    /// <returns>The brush.</returns>
    private Brush Find(string key) => GraphPalette.Find(this, key);
}
