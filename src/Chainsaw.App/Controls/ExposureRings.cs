using System.Globalization;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Controls;

/// <summary>
/// The rings of the view Exponering (sketch EP1b): the subject in the centre, a dotted ring per hop, the money's lines
/// out to the services on the right and in from them on the left, the services in the visual language and the actors
/// on the way as small points. The chosen way stays clear while the rest dims. Only drawing and the click; the places
/// come from <see cref="ExposureLayout"/>, and a click hands the place's id to <see cref="PickCommand"/>.
/// </summary>
public sealed class ExposureRings : FrameworkElement
{
    /// <summary>The picture to draw.</summary>
    public static readonly DependencyProperty PictureProperty = DependencyProperty.Register(
        nameof(Picture), typeof(ExposurePicture), typeof(ExposureRings),
        new FrameworkPropertyMetadata(ExposurePicture.None, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The chosen place's id.</summary>
    public static readonly DependencyProperty SelectedIdProperty = DependencyProperty.Register(
        nameof(SelectedId), typeof(string), typeof(ExposureRings),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The chosen way: the place ids and line keys kept clear.</summary>
    public static readonly DependencyProperty LitProperty = DependencyProperty.Register(
        nameof(Lit), typeof((IReadOnlySet<string>, IReadOnlySet<string>)), typeof(ExposureRings),
        new FrameworkPropertyMetadata(
            ((IReadOnlySet<string>)new HashSet<string>(), (IReadOnlySet<string>)new HashSet<string>()),
            FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>What a click on a place does, with its id.</summary>
    public static readonly DependencyProperty PickCommandProperty = DependencyProperty.Register(
        nameof(PickCommand), typeof(ICommand), typeof(ExposureRings));

    /// <summary>How strong a line off the chosen way is.</summary>
    private const double DimLine = 0.28;

    /// <summary>How strong a place off the chosen way is.</summary>
    private const double DimPlace = 0.4;

    /// <summary>How strong an actor on the way, off the chosen way, is.</summary>
    private const double DimVia = 0.35;

    /// <summary>The subject's square in the centre.</summary>
    private const double SubjectSize = 68;

    /// <summary>How far the axis reaches above and below the centre.</summary>
    private const double AxisReach = 356;

    /// <summary>Where the sides' totals stand below the centre.</summary>
    private const double TotalsBelow = 382;

    /// <summary>The space between a node and its words.</summary>
    private const double LabelGap = 10;

    /// <summary>The sanction mark's radius.</summary>
    private const double MarkRadius = 8;

    /// <summary>The dotted rings' dashes, in multiples of their thickness: a dot and a long gap.</summary>
    private static readonly DashStyle RingDash = Frozen(new DashStyle([0, 5], 0));

    /// <summary>A dashed frame, as the graph draws one (uncertain, or the end of the trail).</summary>
    private static readonly DashStyle FrameDash = Frozen(new DashStyle(GraphDashes.Frame, 0));

    /// <summary>The place under the pointer, for the hand and the tooltip.</summary>
    private RingPlace? _hovered;

    /// <summary>Gets or sets the picture.</summary>
    public ExposurePicture Picture
    {
        get => (ExposurePicture)GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    /// <summary>Gets or sets the chosen place's id.</summary>
    public string? SelectedId
    {
        get => (string?)GetValue(SelectedIdProperty);
        set => SetValue(SelectedIdProperty, value);
    }

    /// <summary>Gets or sets the chosen way.</summary>
    public (IReadOnlySet<string> Places, IReadOnlySet<string> Lines) Lit
    {
        get => ((IReadOnlySet<string>, IReadOnlySet<string>))GetValue(LitProperty);
        set => SetValue(LitProperty, value);
    }

    /// <summary>Gets or sets what a click on a place does.</summary>
    public ICommand? PickCommand
    {
        get => (ICommand?)GetValue(PickCommandProperty);
        set => SetValue(PickCommandProperty, value);
    }

    /// <summary>Gets how much the picture is scaled to fit, and where its corner stands.</summary>
    private (double Scale, Point Corner) Fit
    {
        get
        {
            var scale = Math.Min(ActualWidth / ExposureLayout.Width, ActualHeight / ExposureLayout.Height);
            scale = scale > 0 ? scale : 1;
            var corner = new Point((ActualWidth - (ExposureLayout.Width * scale)) / 2, 0);
            return (scale, corner);
        }
    }

    /// <summary>Gets whether a way is chosen, so that the rest dims.</summary>
    private bool HasChoice => SelectedId is not null;

    /// <summary>Draws the picture.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var dc = drawingContext;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var picture = Picture ?? ExposurePicture.None;
        if (!picture.HasSubject)
        {
            return;
        }

        var (scale, corner) = Fit;
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, corner.X, corner.Y));
        DrawRings(dc, picture);
        foreach (var line in picture.Lines)
        {
            DrawLine(dc, line);
        }

        foreach (var via in picture.Via)
        {
            DrawVia(dc, via);
        }

        foreach (var service in picture.Services)
        {
            DrawService(dc, service);
        }

        DrawSubject(dc, picture);
        dc.Pop();
    }

    /// <summary>Chooses the place under a click.</summary>
    /// <param name="e">The click.</param>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        if (PlaceAt(e.GetPosition(this)) is { } place && PickCommand is { } pick && pick.CanExecute(place.Id))
        {
            pick.Execute(place.Id);
            e.Handled = true;
        }
    }

    /// <summary>Shows the hand and the place's name and amount over a place.</summary>
    /// <param name="e">The pointer.</param>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        var place = PlaceAt(e.GetPosition(this));
        if (place == _hovered)
        {
            return;
        }

        _hovered = place;
        Cursor = place is { IsService: true } ? Cursors.Hand : null;
        ToolTip = place is null ? null : string.Join(" · ", new[] { place.Name, place.Line }.Where(t => t.Length > 0));
    }

    /// <summary>Gives the place under a point on the control.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The place, or <see langword="null"/>.</returns>
    private RingPlace? PlaceAt(Point point)
    {
        var (scale, corner) = Fit;
        return (Picture ?? ExposurePicture.None).At(
            new GraphPoint((point.X - corner.X) / scale, (point.Y - corner.Y) / scale));
    }

    /// <summary>Draws the dotted rings with their hops, the axis and the two sides' totals.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="picture">The picture.</param>
    private void DrawRings(DrawingContext dc, ExposurePicture picture)
    {
        var centre = ToPoint(ExposureLayout.Centre);
        var border = GraphPalette.Find(this, "Brush.Border");
        dc.DrawLine(
            new Pen(border, 1), new Point(centre.X, centre.Y - AxisReach), new Point(centre.X, centre.Y + AxisReach));
        for (var hop = 1; hop < ExposureLayout.Rings.Count; hop++)
        {
            var lit = hop <= picture.Depth;
            var radius = ExposureLayout.Rings[hop];
            var pen = new Pen(GraphPalette.Find(this, lit ? "Brush.BorderStrong" : "Brush.Border"), 1.2)
            {
                DashStyle = RingDash,
                DashCap = PenLineCap.Round,
            };
            dc.DrawEllipse(null, pen, centre, radius, radius);
            var words = hop.ToString(CultureInfo.InvariantCulture) + " hopp";
            var ink = GraphPalette.Find(this, lit ? "Brush.TextDim" : "Brush.BorderStrong");
            WriteCentred(dc, words, 11, ink, new Point(centre.X, centre.Y + radius + 9));
        }

        var muted = GraphPalette.Find(this, "Brush.TextMuted");
        var left = Text(picture.InText, 12, muted);
        dc.DrawText(left, new Point(centre.X - 16 - left.Width, centre.Y + TotalsBelow - left.Height));
        dc.DrawText(Text(picture.OutText, 12, muted), new Point(centre.X + 16, centre.Y + TotalsBelow - left.Height));
    }

    /// <summary>Draws a line of the money: a soft curve, lit or dimmed by the choice.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="line">The line.</param>
    private void DrawLine(DrawingContext dc, RingLine line)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(line.From), false, false);
            context.QuadraticBezierTo(ToPoint(line.Bend), ToPoint(line.To), true, true);
        }

        geometry.Freeze();
        var lit = Lit.Lines.Contains(line.EdgeKey);
        var ink = GraphPalette.Brush(this, line.Ink);
        if (lit && HasChoice)
        {
            var glow = new Pen(GraphPalette.Find(this, "Brush.Accent"), line.Width + 10)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
            };
            dc.PushOpacity(0.22);
            dc.DrawGeometry(null, glow, geometry);
            dc.Pop();
        }

        dc.PushOpacity(HasChoice && !lit ? DimLine : 1);
        dc.DrawGeometry(null, new Pen(ink, line.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
            geometry);
        dc.Pop();
    }

    /// <summary>Draws an actor on the way: a small point in its sort's colour, its name above.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="place">The actor.</param>
    private void DrawVia(DrawingContext dc, RingPlace place)
    {
        var at = ToPoint(place.At);
        dc.PushOpacity(HasChoice && !Lit.Places.Contains(place.Id) ? DimVia : 1);
        var pen = new Pen(GraphPalette.Brush(this, place.Ink), 1.5)
        {
            DashStyle = place.Look.IsDashed ? FrameDash : DashStyles.Solid,
        };
        dc.DrawEllipse(GraphPalette.Find(this, "Brush.Surface"), pen, at, place.Size / 2, place.Size / 2);
        WriteCentred(dc, place.Name, 11, GraphPalette.Find(this, "Brush.TextMuted"), new Point(at.X, at.Y - 24));
        dc.Pop();
    }

    /// <summary>
    /// Draws a service: its frame in its sort's shape and colour with the sort's icon, larger the larger its share, the
    /// sanction mark in the corner, and its name and amount beside it, outwards from the axis.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="place">The service.</param>
    private void DrawService(DrawingContext dc, RingPlace place)
    {
        var chosen = place.Id == SelectedId;
        var near = chosen || Lit.Places.Contains(place.Id);
        dc.PushOpacity(HasChoice && !near ? DimPlace : 1);
        var at = ToPoint(place.At);
        var half = place.Size / 2;
        var radius = place.Look.Shape == NodeShape.Round ? half : 8;
        var box = new Rect(at.X - half, at.Y - half, place.Size, place.Size);
        var accent = GraphPalette.Find(this, "Brush.Accent");
        var ink = GraphPalette.Brush(this, place.Ink);
        if (chosen)
        {
            var halo = new Pen(accent, 4);
            dc.PushOpacity(0.4);
            dc.DrawRoundedRectangle(null, halo, Rect.Inflate(box, 6, 6), radius + 6, radius + 6);
            dc.Pop();
        }

        var frame = new Pen(chosen ? accent : ink, chosen ? 2 : 1.5)
        {
            DashStyle = place.Look.IsDashed ? FrameDash : DashStyles.Solid,
        };
        dc.DrawRoundedRectangle(GraphPalette.Find(this, "Brush.Surface"), frame, box, radius, radius);
        var icon = place.Size * 0.54;
        DrawGlyph(dc, place.Look.Icon, ink, new Point(at.X - (icon / 2), at.Y - (icon / 2)), icon);
        if (place.IsSanctioned)
        {
            DrawSanction(dc, new Point(box.Right - 2, box.Top + 2));
        }

        var outwards = place.Side == ExposureDirection.Out;
        var x = outwards ? box.Right + LabelGap : box.Left - LabelGap;
        WriteBeside(dc, place.Name, 13, GraphPalette.Find(this, "Brush.Text"), new Point(x, at.Y - 2), outwards, true);
        WriteBeside(
            dc, place.Line, 11.5, GraphPalette.Find(this, "Brush.TextMuted"), new Point(x, at.Y + 1), outwards, false);
        dc.Pop();
    }

    /// <summary>Draws the sanction mark: a bright red disc with the ban icon.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="centre">Its centre.</param>
    private void DrawSanction(DrawingContext dc, Point centre)
    {
        var (disc, icon) = GraphPalette.Mark(this, NodeBadge.Sanctioned);
        dc.DrawEllipse(disc, new Pen(GraphPalette.Find(this, "Brush.Background"), 2), centre, MarkRadius, MarkRadius);
        const double IconSize = 10;
        DrawGlyph(dc, VisualLanguage.Of(NodeBadge.Sanctioned).Icon, icon,
            new Point(centre.X - (IconSize / 2), centre.Y - (IconSize / 2)), IconSize);
    }

    /// <summary>Draws the subject in the centre: its sort's square with the icon, and its name under it.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="picture">The picture.</param>
    private void DrawSubject(DrawingContext dc, ExposurePicture picture)
    {
        var centre = ToPoint(ExposureLayout.Centre);
        var half = SubjectSize / 2;
        var ink = GraphPalette.Find(this, "Brush.TextMuted");
        var box = new Rect(centre.X - half, centre.Y - half, SubjectSize, SubjectSize);
        var radius = picture.SubjectLook.Shape == NodeShape.Round ? half : 14;
        dc.DrawRoundedRectangle(GraphPalette.Find(this, "Brush.Surface"), new Pen(ink, 1.5), box, radius, radius);
        const double IconSize = 32;
        DrawGlyph(dc, picture.SubjectLook.Icon, ink, new Point(centre.X - (IconSize / 2), centre.Y - (IconSize / 2)),
            IconSize);
        WriteCentred(dc, picture.SubjectName, 13, GraphPalette.Find(this, "Brush.Text"),
            new Point(centre.X, centre.Y + half + 8), FontWeights.SemiBold);
    }

    /// <summary>Draws an icon of the 24 grid at a size, with the line kept 1.5 px on screen, as GridIcon does.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="key">The icon's resource key.</param>
    /// <param name="ink">Its colour.</param>
    /// <param name="corner">Its top-left corner.</param>
    /// <param name="size">Its size in the picture.</param>
    private void DrawGlyph(DrawingContext dc, string key, Brush ink, Point corner, double size)
    {
        if (TryFindResource(key) is not Geometry glyph)
        {
            return;
        }

        var scale = size / GridIcon.GridSize;
        var pen = new Pen(ink, GridIcon.StrokeFor(size * Fit.Scale))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, corner.X, corner.Y));
        dc.DrawGeometry(null, pen, glyph);
        dc.Pop();
    }

    /// <summary>Writes a line centred on a point's x, from its top, with a halo of the background behind it.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="text">The text.</param>
    /// <param name="size">Its size.</param>
    /// <param name="ink">Its colour.</param>
    /// <param name="top">The centre of its top edge.</param>
    /// <param name="weight">Its weight; normal by default.</param>
    private void WriteCentred(DrawingContext dc, string text, double size, Brush ink, Point top, FontWeight? weight = null)
    {
        var formatted = Text(text, size, ink, weight);
        Halo(dc, formatted, new Point(top.X - (formatted.Width / 2), top.Y));
    }

    /// <summary>
    /// Writes a service's word beside it, outwards from the axis: from the point to the right on the paid side, ending
    /// at it on the got side; above the point for the name, below for the amount.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="text">The text.</param>
    /// <param name="size">Its size.</param>
    /// <param name="ink">Its colour.</param>
    /// <param name="at">Where it starts or ends.</param>
    /// <param name="rightwards">Whether it runs to the right of the point.</param>
    /// <param name="above">Whether it stands above the point.</param>
    private void WriteBeside(
        DrawingContext dc, string text, double size, Brush ink, Point at, bool rightwards, bool above)
    {
        if (text.Length == 0)
        {
            return;
        }

        var formatted = Text(text, size, ink);
        var x = rightwards ? at.X : at.X - formatted.Width;
        var y = above ? at.Y - formatted.Height : at.Y;
        Halo(dc, formatted, new Point(x, y));
    }

    /// <summary>Draws a text with a halo of the background, so that lines under it never cross the letters.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="formatted">The text.</param>
    /// <param name="origin">Its top-left corner.</param>
    private void Halo(DrawingContext dc, FormattedText formatted, Point origin)
    {
        var outline = formatted.BuildGeometry(origin);
        var pen = new Pen(GraphPalette.Find(this, "Brush.Background"), 4) { LineJoin = PenLineJoin.Round };
        dc.DrawGeometry(null, pen, outline);
        dc.DrawText(formatted, origin);
    }

    /// <summary>Lays out a text in the page's font.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The size.</param>
    /// <param name="ink">The ink.</param>
    /// <param name="weight">The weight; normal by default.</param>
    /// <returns>The text.</returns>
    private FormattedText Text(string text, double size, Brush ink, FontWeight? weight = null) => new(
        text,
        CultureInfo.CurrentUICulture,
        FlowDirection.LeftToRight,
        new Typeface(
            TextElement.GetFontFamily(this), FontStyles.Normal, weight ?? FontWeights.Normal, FontStretches.Normal),
        size,
        ink,
        VisualTreeHelper.GetDpi(this).PixelsPerDip);

    /// <summary>Turns a point of the picture into WPF's.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The same point.</returns>
    private static Point ToPoint(GraphPoint point) => new(point.X, point.Y);

    /// <summary>Freezes a dash style, so that it can be shared.</summary>
    /// <param name="style">The style.</param>
    /// <returns>The same style, frozen.</returns>
    private static DashStyle Frozen(DashStyle style)
    {
        style.Freeze();
        return style;
    }
}
