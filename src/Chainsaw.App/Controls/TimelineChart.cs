using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The lanes of the view Tidslinje (sketch TL3a): a lane per actor with its sort and name, the day or hour lines, a
/// transfer as a stroke from the sender's lane to the receiver's in the colour of what the money became and as thick
/// as the amount, bundles as a number, and the case's own events on the last lane with a thread up to the actor. Only
/// the lanes in sight are drawn, and the wheel scrolls them (principle 6). Only drawing and the click; the places come
/// from <see cref="TimelinePicture"/>.
/// </summary>
public sealed class TimelineChart : FrameworkElement
{
    /// <summary>The picture to draw.</summary>
    public static readonly DependencyProperty PictureProperty = DependencyProperty.Register(
        nameof(Picture), typeof(TimelinePicture), typeof(TimelineChart),
        new FrameworkPropertyMetadata(TimelinePicture.None, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>What a click on an event does, with its id.</summary>
    public static readonly DependencyProperty PickCommandProperty = DependencyProperty.Register(
        nameof(PickCommand), typeof(ICommand), typeof(TimelineChart));

    /// <summary>How far right of the time's end the lanes reach.</summary>
    private const double RightRoom = 20;

    /// <summary>How strong an event whose chip is off is.</summary>
    private const double Faint = 0.1;

    /// <summary>How far the lanes are scrolled down.</summary>
    private double _offset;

    /// <summary>The hit under the pointer, for the hand and the tooltip.</summary>
    private TimelineHit? _hovered;

    /// <summary>Creates the chart.</summary>
    public TimelineChart()
    {
        ClipToBounds = true;
        Ink = new ChartDrawing(this);
    }

    /// <summary>Raised when a bundle is clicked, so that the view zooms to its hours.</summary>
    public event EventHandler<TimelineBundle>? BundleOpened;

    /// <summary>Gets or sets the picture.</summary>
    public TimelinePicture Picture
    {
        get => (TimelinePicture)GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    /// <summary>Gets or sets what a click on an event does.</summary>
    public ICommand? PickCommand
    {
        get => (ICommand?)GetValue(PickCommandProperty);
        set => SetValue(PickCommandProperty, value);
    }

    /// <summary>Gets how wide the time is: the chart less the names and the room on the right.</summary>
    public double TimeWidth => Math.Max(0, ActualWidth - TimelinePicture.NamesWidth - RightRoom);

    /// <summary>Gets the brushes, the text and the icons.</summary>
    private ChartDrawing Ink { get; }

    /// <summary>Gets how far the lanes can be scrolled.</summary>
    private double MostOffset => Math.Max(0, (Picture ?? TimelinePicture.None).Height - ActualHeight);

    /// <summary>Draws the lanes in sight.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var dc = drawingContext;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, ActualWidth, ActualHeight));
        var picture = Picture ?? TimelinePicture.None;
        _offset = Math.Clamp(_offset, 0, MostOffset);
        var bottom = ActualHeight;
        DrawHeads(dc, picture);
        dc.PushClip(new RectangleGeometry(new Rect(0, TimelinePicture.Top - 24, ActualWidth, bottom)));
        dc.PushTransform(new TranslateTransform(0, -_offset));
        DrawTicks(dc, picture, _offset + bottom);
        var (first, last) = InSight(picture);
        for (var lane = first; lane <= last; lane++)
        {
            DrawLane(dc, picture, lane);
        }

        foreach (var placed in picture.Events.Where(each => Touches(each, first, last)))
        {
            if (placed.Event.IsTransfer)
            {
                DrawTransfer(dc, picture, placed);
            }
            else
            {
                DrawMoment(dc, picture, placed);
            }
        }

        foreach (var bundle in picture.Bundles)
        {
            DrawBundle(dc, picture, bundle);
        }

        dc.Pop();
        dc.Pop();
    }

    /// <summary>Chooses the event under a click, or zooms to a bundle's hours.</summary>
    /// <param name="e">The click.</param>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        if (HitAt(e.GetPosition(this)) is not { } hit)
        {
            return;
        }

        if (hit.Bundle is { } bundle)
        {
            BundleOpened?.Invoke(this, bundle);
        }
        else if (PickCommand is { } pick && pick.CanExecute(hit.EventId))
        {
            pick.Execute(hit.EventId);
        }

        e.Handled = true;
    }

    /// <summary>Shows the hand and the event's time and amount over an event.</summary>
    /// <param name="e">The pointer.</param>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        var hit = HitAt(e.GetPosition(this));
        if (hit == _hovered)
        {
            return;
        }

        _hovered = hit;
        Cursor = hit is null ? null : Cursors.Hand;
        ToolTip = hit?.ToolTip;
    }

    /// <summary>Scrolls the lanes.</summary>
    /// <param name="e">The wheel.</param>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseWheel(e);
        var offset = Math.Clamp(_offset - (e.Delta / 120.0 * TimelinePicture.LaneHeight), 0, MostOffset);
        if (Math.Abs(offset - _offset) > 0.5)
        {
            _offset = offset;
            InvalidateVisual();
        }

        e.Handled = true;
    }

    /// <summary>Gives what lies under a point on the control.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The hit, or <see langword="null"/>.</returns>
    private TimelineHit? HitAt(Point point) =>
        (Picture ?? TimelinePicture.None).At(point.X - TimelinePicture.NamesWidth, point.Y + _offset);

    /// <summary>Gives the first and last lane in sight.</summary>
    /// <param name="picture">The picture.</param>
    /// <returns>The lanes' indices.</returns>
    private (int First, int Last) InSight(TimelinePicture picture)
    {
        var first = (int)Math.Floor((_offset - TimelinePicture.Top) / TimelinePicture.LaneHeight) - 1;
        var last = (int)Math.Ceiling((_offset + ActualHeight - TimelinePicture.Top) / TimelinePicture.LaneHeight) + 1;
        return (Math.Max(0, first), Math.Min(picture.Lanes.Count - 1, last));
    }

    /// <summary>Gives whether an event crosses the lanes in sight.</summary>
    /// <param name="placed">The event.</param>
    /// <param name="first">The first lane in sight.</param>
    /// <param name="last">The last.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool Touches(PlacedEvent placed, int first, int last)
    {
        var (low, high) = (Math.Min(placed.Lane, placed.ToLane), Math.Max(placed.Lane, placed.ToLane));
        return high >= first && low <= last;
    }

    /// <summary>Draws the labels of the days or hours and the cuts' lengths, fixed above the lanes.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="picture">The picture.</param>
    private void DrawHeads(DrawingContext dc, TimelinePicture picture)
    {
        var left = TimelinePicture.NamesWidth;
        var muted = Ink.Brush("Brush.TextMuted");
        var bright = Ink.Brush("Brush.Text");
        foreach (var tick in picture.Scale.Ticks.Where(tick => tick.Label.Length > 0))
        {
            var x = left + tick.X + 6;
            if (tick.Strong.Length == 0)
            {
                dc.DrawText(Ink.Text(tick.Label, 11.5, muted), new Point(x - 1, 4));
                continue;
            }

            var at = tick.Label.IndexOf(tick.Strong, StringComparison.Ordinal);
            var after = tick.Label[(at + tick.Strong.Length)..];
            foreach (var (part, ink) in new[] { (tick.Label[..at], muted), (tick.Strong, bright), (after, muted) })
            {
                var text = Ink.Text(part, 11.5, ink);
                dc.DrawText(text, new Point(x, 4));
                x += text.WidthIncludingTrailingWhitespace;
            }
        }

        foreach (var cut in picture.Scale.Cuts)
        {
            var text = Ink.Text(cut.Text, 11, Ink.Brush("Brush.TextDim"));
            dc.DrawText(text, new Point(left + ((cut.X0 + cut.X1) / 2) - (text.Width / 2), 5));
        }

        if (picture.NowX is { } now)
        {
            var text = Ink.Text("nu", 11, Ink.Brush("Brush.Accent"));
            dc.DrawText(text, new Point(left + now - 4 - text.Width, 5));
        }
    }

    /// <summary>Draws the dotted day or hour lines, the cuts and the line at now, down through the lanes.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="picture">The picture.</param>
    /// <param name="bottom">How far down to draw.</param>
    private void DrawTicks(DrawingContext dc, TimelinePicture picture, double bottom)
    {
        var left = TimelinePicture.NamesWidth;
        var end = Math.Min(bottom, picture.Height);
        var day = Ink.Pen("Brush.BorderStrong", 1, [0, 4]);
        var hour = Ink.Pen("Brush.BorderStrong", 1, [0, 9]);
        var midnight = Ink.Pen("Brush.BorderHover", 1, [0, 4]);
        foreach (var tick in picture.Scale.Ticks)
        {
            var pen = picture.Scale.Zoom == TimelineZoom.Days ? day : (tick.IsDayStart ? midnight : hour);
            var top = picture.Scale.Zoom == TimelineZoom.Hours && tick.Label.Length == 0 ? 30 : 22;
            dc.DrawLine(pen, new Point(left + tick.X, top), new Point(left + tick.X, end));
        }

        var zig = Ink.Pen("Brush.BorderStrong", 1);
        foreach (var cut in picture.Scale.Cuts)
        {
            var box = new Rect(left + cut.X0 + 4, 26, Math.Max(1, cut.X1 - cut.X0 - 8), Math.Max(1, end - 26));
            dc.DrawRoundedRectangle(Ink.Brush("Brush.Surface"), null, box, 6, 6);
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                var x = box.Left + (box.Width / 2) - 4;
                context.BeginFigure(new Point(x, 34), false, false);
                for (var y = 34.0; y < end - 6; y += 10)
                {
                    context.LineTo(new Point(x + 8, y + 5), true, false);
                    context.LineTo(new Point(x, y + 10), true, false);
                }
            }

            geometry.Freeze();
            dc.DrawGeometry(null, zig, geometry);
        }

        if (picture.NowX is { } now)
        {
            dc.PushOpacity(0.5);
            dc.DrawLine(Ink.Pen("Brush.Accent", 1), new Point(left + now, 22), new Point(left + now, end));
            dc.Pop();
        }
    }

    /// <summary>
    /// Draws a lane: a faint dotted line over the whole time, firmer while the actor is active, and its sort and name;
    /// the lanes of the chosen event are lit, and the case's own lane stands apart below a line.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="picture">The picture.</param>
    /// <param name="index">The lane.</param>
    private void DrawLane(DrawingContext dc, TimelinePicture picture, int index)
    {
        var lane = picture.Lanes[index];
        var y = picture.LaneY(index);
        var left = TimelinePicture.NamesWidth;
        var right = left + picture.Scale.Pieces.DefaultIfEmpty().Max(piece => piece?.X1 ?? 0);
        var lit = picture.Lit.Contains(lane.Key);
        if (lane.IsCase)
        {
            var line = y - (TimelinePicture.LaneHeight / 2) - 2;
            dc.DrawLine(Ink.Pen("Brush.Border", 1), new Point(16, line), new Point(ActualWidth, line));
        }

        if (lit)
        {
            dc.DrawRoundedRectangle(Ink.Brush("Brush.SurfaceRaised"), null, new Rect(6, y - 17, left - 18, 34), 9, 9);
        }

        dc.DrawLine(Ink.Pen("Brush.Border", 1, [0, 5]), new Point(left, y), new Point(right, y));
        var xs = picture.Events.Where(each => each.Event.IsTransfer && (each.Lane == index || each.ToLane == index))
            .Select(each => each.X).ToList();
        if (xs.Count > 1 && !lane.IsCase)
        {
            var pen = Ink.Pen(lit ? "Brush.BorderHover" : "Brush.BorderStrong", 1);
            dc.DrawLine(pen, new Point(left + xs.Min(), y), new Point(left + xs.Max(), y));
        }

        Ink.Mark(dc, lane.Look, new Point(26, y), 20);
        var name = Ink.Text(Trimmed(lane.Name), 12.5, Ink.Brush(lit ? "Brush.Text" : "Brush.TextMuted"),
            lit ? FontWeights.SemiBold : FontWeights.Normal);
        dc.DrawText(name, new Point(44, y - 1 - name.Height + 3));
        dc.DrawText(Ink.Text(Trimmed(lane.Line), 10.5, Ink.Brush("Brush.TextDim")), new Point(44, y + 1));
    }

    /// <summary>Draws a transfer: a stroke between the lanes, a dot where it leaves and a ring where it arrives.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="picture">The picture.</param>
    /// <param name="placed">The transfer.</param>
    private void DrawTransfer(DrawingContext dc, TimelinePicture picture, PlacedEvent placed)
    {
        var x = TimelinePicture.NamesWidth + placed.X;
        var (from, to) = (new Point(x, picture.LaneY(placed.Lane)), new Point(x, picture.LaneY(placed.ToLane)));
        var kind = placed.Event.Kind;
        var key = TimelineReading.BrushOf(kind);
        var chosen = placed.Event.Id == picture.SelectedId;
        dc.PushOpacity(placed.IsHidden ? Faint : 1);
        if (chosen)
        {
            dc.PushOpacity(0.25);
            dc.DrawLine(Ink.Pen("Brush.Accent", placed.Thickness + 10), from, to);
            dc.Pop();
        }

        double[]? dashes = kind switch
        {
            TimelineKind.Guess => [0.5, 3],
            TimelineKind.OtherChain => [2.5, 1.8],
            _ => null,
        };
        dc.DrawLine(Ink.Pen(key, placed.Thickness, dashes), from, to);
        dc.DrawEllipse(Ink.Brush(key), null, from, 3, 3);
        dc.DrawEllipse(Ink.Brush("Brush.Background"), Ink.Pen(key, 1.6), to, 4.5, 4.5);
        var words = Ink.Brush(kind == TimelineKind.Unknown ? "Brush.TextMuted" : "Brush.Text");
        if (placed.Amount.Length > 0)
        {
            var amount = Ink.Text(placed.Amount, 11.5, words);
            Ink.Halo(dc, amount, new Point(x + 8, to.Y - (amount.Height / 2)));
        }

        if (placed.Value.Length > 0)
        {
            Ink.Halo(dc, Ink.Text(placed.Value, 10.5, Ink.Brush("Brush.TextDim")), new Point(x + 8, to.Y + 6));
        }

        dc.Pop();
    }

    /// <summary>
    /// Draws a case event: a small box with its icon on the case's lane and a dotted thread up to the actor; a watch's
    /// hit also has the eye with rings on the actor's lane, and its amount.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="picture">The picture.</param>
    /// <param name="placed">The case event.</param>
    private void DrawMoment(DrawingContext dc, TimelinePicture picture, PlacedEvent placed)
    {
        var x = TimelinePicture.NamesWidth + placed.X;
        var (box, actor) = (picture.LaneY(placed.Lane), picture.LaneY(placed.ToLane));
        var kind = placed.Event.Kind;
        var key = TimelineReading.BrushOf(kind);
        var chosen = placed.Event.Id == picture.SelectedId;
        var icon = kind switch
        {
            TimelineKind.Label => "Icon.Tag",
            TimelineKind.Decision => "Icon.Check",
            _ => "Icon.Eye",
        };
        dc.PushOpacity(placed.IsHidden ? Faint : 1);
        dc.PushOpacity(0.55);
        dc.DrawLine(Ink.Pen(key, 1, [0, 3]), new Point(x, actor + 6), new Point(x, box - 11));
        dc.Pop();
        if (kind == TimelineKind.Watch)
        {
            DrawHit(dc, placed, new Point(x, actor), chosen);
        }

        var frame = Ink.Pen(chosen ? "Brush.Accent" : "Brush.BorderHover", chosen ? 1.8 : 1.2);
        dc.DrawRoundedRectangle(Ink.Brush("Brush.Surface"), frame, new Rect(x - 10, box - 10, 20, 20), 6, 6);
        Ink.Glyph(dc, icon, Ink.Brush(chosen ? "Brush.Text" : "Brush.TextMuted"), new Point(x - 6, box - 6), 12);
        dc.Pop();
    }

    /// <summary>Draws a watch's hit on the actor's lane: the eye in rings, and the amount to its left.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="placed">The hit.</param>
    /// <param name="at">Where on the actor's lane.</param>
    /// <param name="chosen">Whether it is the chosen event.</param>
    private void DrawHit(DrawingContext dc, PlacedEvent placed, Point at, bool chosen)
    {
        var watch = "Brush.Watch";
        dc.PushOpacity(0.25);
        dc.DrawEllipse(null, Ink.Pen(watch, 1.2), at, chosen ? 15 : 13, chosen ? 15 : 13);
        dc.Pop();
        dc.PushOpacity(0.12);
        dc.DrawEllipse(null, Ink.Pen(watch, 1.2), at, chosen ? 22 : 19, chosen ? 22 : 19);
        dc.Pop();
        dc.DrawEllipse(Ink.Brush("Brush.Background"), Ink.Pen(chosen ? "Brush.Accent" : watch, 1.5), at, 9, 9);
        Ink.Glyph(dc, "Icon.Eye", Ink.Brush(watch), new Point(at.X - 6, at.Y - 6), 12);
        if (placed.Amount.Length > 0)
        {
            var amount = Ink.Text(placed.Amount, 11.5, Ink.Brush("Brush.Text"));
            Ink.Halo(dc, amount, new Point(at.X - 16 - amount.Width, at.Y - (amount.Height / 2)));
        }
    }

    /// <summary>Draws a bundle: the number of transfers in a small disc above the sender's lane.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="picture">The picture.</param>
    /// <param name="bundle">The bundle.</param>
    private void DrawBundle(DrawingContext dc, TimelinePicture picture, TimelineBundle bundle)
    {
        var centre = new Point(TimelinePicture.NamesWidth + bundle.X, picture.LaneY(bundle.Lane) - 15);
        dc.DrawEllipse(Ink.Brush("Brush.SurfaceSelected"), Ink.Pen("Brush.Background", 2), centre, 8, 8);
        var count = Ink.Text(
            bundle.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), 10,
            Ink.Brush("Brush.Text"), FontWeights.SemiBold);
        dc.DrawText(count, new Point(centre.X - (count.Width / 2), centre.Y - (count.Height / 2)));
    }

    /// <summary>Shortens a name that would run into the time.</summary>
    /// <param name="text">The name.</param>
    /// <returns>The name, shortened with an ellipsis past 16 characters.</returns>
    private static string Trimmed(string text) => text.Length > 16 ? text[..15] + "…" : text;
}
