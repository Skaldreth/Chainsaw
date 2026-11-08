using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The Sankey of the view Flöde (sketch FL1a): the columns' titles, a bar per actor, the bands split in strands in the
/// colour of where the money ended, striped and dashed beyond something uncertain, and what stays as a short stub in
/// the accent. The chosen strands stay clear while the rest dims. Only drawing and the click; the places come from
/// <see cref="FlowPicture"/>.
/// </summary>
public sealed class FlowChart : FrameworkElement
{
    /// <summary>The picture to draw.</summary>
    public static readonly DependencyProperty PictureProperty = DependencyProperty.Register(
        nameof(Picture), typeof(FlowPicture), typeof(FlowChart),
        new FrameworkPropertyMetadata(FlowPicture.None, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The parts that run through the choice.</summary>
    public static readonly DependencyProperty LitProperty = DependencyProperty.Register(
        nameof(Lit), typeof(IReadOnlySet<FlowLot>), typeof(FlowChart),
        new FrameworkPropertyMetadata(new HashSet<FlowLot>(), FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>What a click does, with the step's or the actor's key.</summary>
    public static readonly DependencyProperty PickCommandProperty = DependencyProperty.Register(
        nameof(PickCommand), typeof(ICommand), typeof(FlowChart));

    /// <summary>How strong a strand off the choice is.</summary>
    private const double DimStrand = 0.2;

    /// <summary>How strong a strand is when nothing is chosen.</summary>
    private const double PlainStrand = 0.5;

    /// <summary>How strong a chosen strand is.</summary>
    private const double LitStrand = 0.8;

    /// <summary>The hit under the pointer, for the hand and the tooltip.</summary>
    private FlowHit? _hovered;

    /// <summary>Creates the chart.</summary>
    public FlowChart()
    {
        ClipToBounds = true;
        Ink = new ChartDrawing(this);
    }

    /// <summary>Gets or sets the picture.</summary>
    public FlowPicture Picture
    {
        get => (FlowPicture)GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    /// <summary>Gets or sets the parts that run through the choice.</summary>
    public IReadOnlySet<FlowLot> Lit
    {
        get => (IReadOnlySet<FlowLot>)GetValue(LitProperty);
        set => SetValue(LitProperty, value);
    }

    /// <summary>Gets or sets what a click does.</summary>
    public ICommand? PickCommand
    {
        get => (ICommand?)GetValue(PickCommandProperty);
        set => SetValue(PickCommandProperty, value);
    }

    /// <summary>Gets the brushes, the text and the icons.</summary>
    private ChartDrawing Ink { get; }

    /// <summary>Draws the Sankey.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var dc = drawingContext;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var picture = Picture ?? FlowPicture.None;
        if (!picture.HasBars)
        {
            return;
        }

        var lit = Lit ?? new HashSet<FlowLot>();
        foreach (var column in picture.Columns)
        {
            var title = Ink.Text(column.Title, 11, Ink.Brush("Brush.TextDim"));
            var x = column.X == 0 ? column.X : column.X + (FlowLayout.BarWidth / 2) - (title.Width / 2);
            dc.DrawText(title, new Point(x, 8));
        }

        foreach (var strand in picture.Strands)
        {
            DrawStrand(dc, strand, lit);
        }

        foreach (var stub in picture.Stubs)
        {
            DrawStub(dc, stub, lit);
        }

        foreach (var bar in picture.Bars)
        {
            DrawBar(dc, bar);
        }
    }

    /// <summary>Chooses the band or actor under a click.</summary>
    /// <param name="e">The click.</param>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        var point = e.GetPosition(this);
        if ((Picture ?? FlowPicture.None).At(point.X, point.Y) is { } hit
            && PickCommand is { } pick && pick.CanExecute(hit.Key))
        {
            pick.Execute(hit.Key);
            e.Handled = true;
        }
    }

    /// <summary>Shows the hand and the actor's name over a bar or a band.</summary>
    /// <param name="e">The pointer.</param>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        var hit = (Picture ?? FlowPicture.None).At(point.X, point.Y);
        if (hit == _hovered)
        {
            return;
        }

        _hovered = hit;
        Cursor = hit is null ? null : Cursors.Hand;
        ToolTip = hit is { ToolTip.Length: > 0 } ? hit.ToolTip : null;
    }

    /// <summary>
    /// Draws a strand as a soft band from one bar to the next: filled in its colour, or striped with a dashed edge
    /// beyond something uncertain.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="strand">The strand.</param>
    /// <param name="lit">The parts chosen.</param>
    private void DrawStrand(DrawingContext dc, FlowStrand strand, IReadOnlySet<FlowLot> lit)
    {
        var geometry = Band(strand);
        var chosen = lit.Contains(strand.Lot);
        var any = lit.Count > 0;
        var strength = any ? (chosen ? LitStrand : DimStrand) : PlainStrand;
        if (!strand.IsGuess)
        {
            dc.PushOpacity(strength);
            dc.DrawGeometry(Ink.Brush(strand.Brush), null, geometry);
            dc.Pop();
            return;
        }

        dc.PushOpacity(any && !chosen ? 0.3 : 1);
        dc.DrawGeometry(Stripes(strand.Brush), null, geometry);
        dc.Pop();
        dc.PushOpacity(any && !chosen ? DimStrand : LitStrand);
        dc.DrawGeometry(null, Ink.Pen(strand.Brush, 1, [3, 3]), geometry);
        dc.Pop();
    }

    /// <summary>Draws what stays at an actor: a short accent stub with its amount beside it.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="stub">The stub.</param>
    /// <param name="lit">The parts chosen.</param>
    private void DrawStub(DrawingContext dc, FlowStub stub, IReadOnlySet<FlowLot> lit)
    {
        var chosen = lit.Any(lot => lot.Last == stub.Key && lot.End == FlowEnd.Kept);
        dc.PushOpacity(lit.Count > 0 && !chosen ? 0.16 : 0.6);
        dc.DrawRectangle(Ink.Brush("Brush.Accent"), null, new Rect(stub.X, stub.Y, FlowLayout.StubLength, stub.Height));
        dc.Pop();
        var end = stub.X + FlowLayout.StubLength;
        dc.DrawLine(Ink.Pen("Brush.Accent", 2), new Point(end, stub.Y), new Point(end, stub.Y + stub.Height));
        var text = Ink.Text(stub.Text, 11.5, Ink.Brush("Brush.Text"));
        Ink.Halo(dc, text, new Point(end + 8, stub.Y + (stub.Height / 2) - (text.Height / 2)));
    }

    /// <summary>
    /// Draws an actor's bar with its icon, name and amount: to the right of a terminal, above the bar elsewhere, so that
    /// no band is covered; the sanction mark at its corner.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="bar">The bar.</param>
    private void DrawBar(DrawingContext dc, FlowBar bar)
    {
        var box = new Rect(bar.X, bar.Y, FlowLayout.BarWidth, Math.Max(1, bar.Height));
        dc.PushOpacity(bar.IsDashed ? 0.35 : 1);
        dc.DrawRoundedRectangle(Ink.Brush(bar.Brush), null, box, 3, 3);
        dc.Pop();
        if (bar.IsDashed)
        {
            dc.DrawRoundedRectangle(null, Ink.Pen(bar.Brush, 1, [3, 2]), Rect.Inflate(box, -0.5, -0.5), 3, 3);
        }

        var lx = bar.IsTerminal ? box.Right + 8 : box.Left;
        var ly = bar.IsTerminal ? box.Top + Math.Min(box.Height / 2, 14) + 4 : box.Top - 26;
        var look = GraphPalette.Brush(this, bar.Look.Ink);
        Ink.Glyph(dc, bar.Look.Icon, look, new Point(lx, ly - 13), 14);
        var name = Ink.Text(bar.Name, 13, Ink.Brush("Brush.Text"));
        Ink.Halo(dc, name, new Point(lx + 20, ly - name.Height + 2));
        var line = Ink.Text(bar.Line, 11.5, Ink.Brush("Brush.TextMuted"));
        Ink.Halo(dc, line, new Point(bar.IsTerminal ? lx : lx + 20, ly + 2));
        if (bar.IsSanctioned)
        {
            var (disc, _) = GraphPalette.Mark(this, NodeBadge.Sanctioned);
            dc.DrawEllipse(disc, Ink.Pen("Brush.Background", 2), new Point(box.Right, box.Top), 7, 7);
        }
    }

    /// <summary>Makes a strand's band: a soft curve along its top, straight down, and back along its bottom.</summary>
    /// <param name="strand">The strand.</param>
    /// <returns>The band.</returns>
    private static StreamGeometry Band(FlowStrand strand)
    {
        var (x0, x1, y0, y1, h) = (strand.X0, strand.X1, strand.Y0, strand.Y1, Math.Max(0.6, strand.Height));
        var middle = (x0 + x1) / 2;
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(x0, y0), true, true);
            context.BezierTo(new Point(middle, y0), new Point(middle, y1), new Point(x1, y1), true, false);
            context.LineTo(new Point(x1, y1 + h), true, false);
            context.BezierTo(new Point(middle, y1 + h), new Point(middle, y0 + h), new Point(x0, y0 + h), true, false);
        }

        geometry.Freeze();
        return geometry;
    }

    /// <summary>Makes the stripes of an uncertain band: thin diagonal lines over a faint wash in its colour.</summary>
    /// <param name="key">The colour's resource key.</param>
    /// <returns>The brush.</returns>
    private DrawingBrush Stripes(string key)
    {
        var ink = Ink.Brush(key);
        var wash = new DrawingGroup { Opacity = 0.1 };
        wash.Children.Add(new GeometryDrawing(ink, null, new RectangleGeometry(new Rect(0, 0, 6, 6))));
        var line = new DrawingGroup { Opacity = 0.55 };
        var stroke = new Pen(ink, 2);
        line.Children.Add(new GeometryDrawing(null, stroke, new LineGeometry(new Point(0, 0), new Point(0, 6))));
        var tile = new DrawingGroup();
        tile.Children.Add(wash);
        tile.Children.Add(line);
        var brush = new DrawingBrush(tile)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 6, 6),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 6, 6),
            ViewboxUnits = BrushMappingMode.Absolute,
            Transform = new RotateTransform(45),
        };
        brush.Freeze();
        return brush;
    }
}
