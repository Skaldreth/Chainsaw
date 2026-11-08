using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>
/// The time bands over the graph (sketch TL1a): a thin dotted line between the days over the whole surface, fading in
/// and out at its ends and bending round actors dragged out of their day; the day's name at the top of each band; and,
/// at a time chosen in the strip, a whole line with the time over it while what came later is drawn faint.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>How faint an actor is that the money reached after the chosen time (sketch TL1a).</summary>
    private const double LaterNodeOpacity = 0.25;

    /// <summary>How faint a line is that first carried money after the chosen time (sketch TL1a).</summary>
    private const double LaterLineOpacity = 0.18;

    /// <summary>How faint a line's amount is when the line came after the chosen time (sketch TL1a).</summary>
    private const double LaterTextOpacity = 0.25;

    /// <summary>The bands' width on screen, in pixels.</summary>
    private const double BandPixels = 1.5;

    /// <summary>The chosen time's line's width on screen, in pixels.</summary>
    private const double ChosenPixels = 1.25;

    /// <summary>The least distance on screen between two day lines; closer days share a band (principle 7).</summary>
    private const double LeastBandGap = 36;

    /// <summary>Where the days' names stand on screen, from the top: under the trace's line.</summary>
    private const double BandNameTop = 62;

    /// <summary>Where the chosen time's pill stands on screen, from the top, at its middle.</summary>
    private const double FrontMiddle = 96;

    /// <summary>The pill's height.</summary>
    private const double FrontHeight = 22;

    /// <summary>The space inside the pill, left and right.</summary>
    private const double FrontPadding = 9;

    /// <summary>The space a day's name needs at least on either side within its band.</summary>
    private const double NameRoom = 12;

    /// <summary>The most lines kept from one graph, so that hovering along the strip does not fill the memory.</summary>
    private const int MostCachedLines = 64;

    /// <summary>
    /// The dots of a band, in multiples of its width as WPF counts dashes: a dot every five pixels on screen (the
    /// sketch's <c>0.01 5</c> with round caps).
    /// </summary>
    private static readonly DashStyle BandDots = Frozen(new DashStyle([0.01 / BandPixels, 5 / BandPixels], 0));

    /// <summary>The lines worked out for the graph drawn, so that a pan or a zoom does not work them out again.</summary>
    private TimeBandCache? _bands;

    /// <summary>Gives how faint a line's amount is: faint when the line came after the chosen time.</summary>
    /// <param name="edgeKey">The line.</param>
    /// <returns>The opacity.</returns>
    private double LaterAmountOpacity(string edgeKey) => _model!.Timeline.IsLaterEdge(edgeKey) ? LaterTextOpacity : 1;

    /// <summary>Gives how faint a pile or a clue is: faint when it came after the chosen time.</summary>
    /// <param name="nodeKey">The pile or clue.</param>
    /// <returns>The opacity.</returns>
    private double LaterFoldedOpacity(string nodeKey) => _model!.Timeline.IsLaterNode(nodeKey) ? LaterNodeOpacity : 1;

    /// <summary>
    /// Draws the bands between the days, the faint front at the time under the pointer in the strip and the whole line
    /// at the chosen time, on the surface under everything else; only what is on screen (principle 6).
    /// </summary>
    /// <param name="dc">Where to draw, in the surface's units.</param>
    /// <param name="visible">The visible part of the surface.</param>
    private void DrawTimeBands(DrawingContext dc, GraphRect visible)
    {
        var timeline = _model!.Timeline;
        if (!timeline.IsShown || Bands() is not { } bands)
        {
            return;
        }

        var band = ResourceColor("Color.Graph.TimeBand");
        foreach (var line in KeptLines(bands))
        {
            DrawBandLine(dc, bands, line, visible, band, BandPixels, BandDots);
        }

        DrawHourLines(dc, bands, visible);

        if (timeline.Hovered is { } hovered && bands.Line(timeline.Axis.Hours(hovered)) is { } front)
        {
            DrawBandLine(dc, bands, front, visible, BrushColor("Brush.TextDim"), BandPixels, BandDots);
        }

        if (timeline.Chosen is { } chosen && bands.Line(timeline.Axis.Hours(chosen)) is { } whole)
        {
            DrawBandLine(dc, bands, whole, visible, ResourceColor("Color.Graph.TimeLine"), ChosenPixels, null);
        }
    }

    /// <summary>
    /// Draws the days' names at the top of their bands and the chosen time in a pill over its line, in screen units;
    /// a name that does not fit its band on screen is left out.
    /// </summary>
    /// <param name="dc">Where to draw, in screen units.</param>
    private void DrawTimeNames(DrawingContext dc)
    {
        var timeline = _model!.Timeline;
        if (!timeline.IsShown || GraphHidden || Bands() is not { } bands)
        {
            return;
        }

        var y = _viewport.ToSurface(new GraphPoint(0, BandNameTop)).Y;
        var kept = KeptLines(bands);
        for (var index = 0; index <= kept.Count; index++)
        {
            var left = index > 0 ? kept[index - 1] : null;
            var right = index < kept.Count ? kept[index] : null;
            DrawBandName(dc, bands, left, right, y);
        }

        DrawHourNames(dc, bands);

        if (timeline.Chosen is { } chosen && bands.Line(timeline.Axis.Hours(chosen)) is { } line)
        {
            var x = _viewport.ToScreen(new GraphPoint(line.XAt(_viewport.ToSurface(new GraphPoint(0, FrontMiddle)).Y), 0)).X;
            DrawFront(dc, timeline.Axis.TimeName(chosen), x);
        }
    }

    /// <summary>
    /// Writes a band's day, or its days when the band holds several, centred between its lines; faint when the band
    /// begins after the chosen time.
    /// </summary>
    /// <param name="dc">Where to draw, in screen units.</param>
    /// <param name="bands">The lines of the graph drawn.</param>
    /// <param name="left">The line on the band's left, or <see langword="null"/> for the first band.</param>
    /// <param name="right">The line on the band's right, or <see langword="null"/> for the last band.</param>
    /// <param name="y">The height of the names on the surface.</param>
    private void DrawBandName(
        DrawingContext dc, TimeBandCache bands, TimeBandLine? left, TimeBandLine? right, double y)
    {
        var timeline = _model!.Timeline;
        var axis = timeline.Axis;
        var from = left?.Hours ?? bands.First;
        var first = axis.DayOf(from);
        var last = right is null ? axis.DayOf(bands.Last) : axis.DayOf(right.Hours).AddDays(-1);
        var name = first >= last ? TimeAxis.DayName(first) : $"{TimeAxis.DayName(first)} – {TimeAxis.DayName(last)}";
        var later = timeline.Chosen is { } chosen && from > axis.Hours(chosen);
        var text = Text(name, 11.5, GraphPalette.Find(this, later ? "Brush.BorderStrong" : "Brush.TextDim"),
            FontWeights.Normal);
        var (edgeLeft, edgeRight) = TimeBands.BandAt(left, right, y, bands.Actors);
        var screenLeft = _viewport.ToScreen(new GraphPoint(edgeLeft, y)).X;
        var screenRight = _viewport.ToScreen(new GraphPoint(edgeRight, y)).X;
        if (screenRight - screenLeft < text.Width + (2 * NameRoom))
        {
            return;
        }

        var middle = (screenLeft + screenRight) / 2;
        if (middle + text.Width < 0 || middle - text.Width > ActualWidth)
        {
            return;
        }

        dc.DrawText(text, new Point(middle - (text.Width / 2), BandNameTop));
    }

    /// <summary>Draws the chosen time in its pill over the whole line (sketch TL1a).</summary>
    /// <param name="dc">Where to draw, in screen units.</param>
    /// <param name="time">The time, as the timeline writes it.</param>
    /// <param name="x">The line's place on screen at the pill's height.</param>
    private void DrawFront(DrawingContext dc, string time, double x)
    {
        var text = Text(time, 11.5, GraphPalette.Find(this, "Brush.Text"), FontWeights.Normal);
        var pill = new Rect(
            x - (text.Width / 2) - FrontPadding, FrontMiddle - (FrontHeight / 2), text.Width + (2 * FrontPadding),
            FrontHeight);
        dc.DrawRoundedRectangle(
            GraphPalette.Find(this, "Brush.SurfaceRaised"),
            new Pen(GraphPalette.Find(this, "Brush.BorderHover"), 1),
            pill,
            FrontHeight / 2,
            FrontHeight / 2);
        dc.DrawText(text, new Point(pill.X + FrontPadding, FrontMiddle - (text.Height / 2)));
    }

    /// <summary>
    /// Draws one line through the graph: the part on screen, its width kept on screen whatever the zoom, fading in and
    /// out at its ends.
    /// </summary>
    /// <param name="dc">Where to draw, in the surface's units.</param>
    /// <param name="bands">The lines of the graph drawn.</param>
    /// <param name="line">The line.</param>
    /// <param name="visible">The visible part of the surface.</param>
    /// <param name="ink">Its colour.</param>
    /// <param name="pixels">Its width on screen.</param>
    /// <param name="dots">Its dots, or <see langword="null"/> for a whole line.</param>
    private void DrawBandLine(
        DrawingContext dc, TimeBandCache bands, TimeBandLine line, GraphRect visible, Color ink, double pixels,
        DashStyle? dots)
    {
        var points = line.Within(visible.Y, visible.Bottom);
        if (points.Count < 2 || points.All(point => point.X < visible.X || point.X > visible.Right))
        {
            return;
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(points[0]), false, false);
            context.PolyLineTo([.. points.Skip(1).Select(ToPoint)], true, true);
        }

        geometry.Freeze();
        var pen = new Pen(Faded(ink, bands.Top, bands.Bottom), pixels / _viewport.Zoom)
        {
            DashStyle = dots ?? DashStyles.Solid,
            DashCap = PenLineCap.Round,
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        pen.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    /// <summary>Gives a colour that fades in at the top of the lines and out at their bottom (sketch TL1a).</summary>
    /// <param name="ink">The colour.</param>
    /// <param name="top">The lines' top on the surface.</param>
    /// <param name="bottom">The lines' bottom on the surface.</param>
    /// <returns>The brush.</returns>
    private static Brush Faded(Color ink, double top, double bottom)
    {
        var clear = Color.FromArgb(0, ink.R, ink.G, ink.B);
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, top),
            EndPoint = new Point(0, bottom),
            GradientStops =
            [
                new GradientStop(clear, 0),
                new GradientStop(ink, TimeBands.FadeIn),
                new GradientStop(ink, TimeBands.FadeOut),
                new GradientStop(clear, 1),
            ],
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Gives the day lines kept at this zoom: one between each two days, or between groups of days when they would
    /// stand closer on screen than <see cref="LeastBandGap"/>.
    /// </summary>
    /// <param name="bands">The lines of the graph drawn.</param>
    /// <returns>The lines, in time order.</returns>
    private List<TimeBandLine> KeptLines(TimeBandCache bands)
    {
        var places = bands.Days.Select(day => (day.Hours, day.BaseX * _viewport.Zoom)).ToList();
        return [.. TimeBands.Thin(places, LeastBandGap).Select(bands.Line).OfType<TimeBandLine>()];
    }

    /// <summary>
    /// Gives the lines of the graph drawn, worked out again only when the places or the times changed; none when no
    /// actor has a time.
    /// </summary>
    /// <returns>The lines, or <see langword="null"/>.</returns>
    private TimeBandCache? Bands()
    {
        var model = _model!;
        if (_bands is { } kept && kept.IsFor(model.Places, model.Timeline.Times))
        {
            return kept;
        }

        var times = model.Timeline.Times;
        var axis = model.Timeline.Axis;
        var actors = model.Places
            .Where(pair => times.NodeTimes.ContainsKey(pair.Key))
            .Select(pair => new TimedFrame(model.FrameAt(pair.Key, pair.Value), axis.Hours(times.NodeTimes[pair.Key])))
            .ToList();
        _bands = TimeBands.Extent(actors) is { } extent
            ? new TimeBandCache(model.Places, times, actors, extent.Top, extent.Bottom, axis)
            : null;
        return _bands;
    }

    /// <summary>Finds a colour of the theme, or a neutral grey if the theme lacks it.</summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The colour.</returns>
    private Color ResourceColor(string key) => TryFindResource(key) is Color color ? color : Colors.Gray;

    /// <summary>Gives the colour of a brush of the theme.</summary>
    /// <param name="key">The brush's resource key.</param>
    /// <returns>The colour, or a neutral grey when the brush is not a plain colour.</returns>
    private Color BrushColor(string key) =>
        GraphPalette.Find(this, key) is SolidColorBrush brush ? brush.Color : Colors.Gray;

    /// <summary>
    /// The time lines worked out for one arrangement of the graph: the actors with their times, the day lines' natural
    /// places and every line asked for so far, by its time.
    /// </summary>
    private sealed class TimeBandCache
    {
        /// <summary>The places the lines were worked out for.</summary>
        private readonly IReadOnlyDictionary<string, GraphPoint> _places;

        /// <summary>The times the lines were worked out for.</summary>
        private readonly GraphTimeline _times;

        /// <summary>The lines asked for so far, by time; <see langword="null"/> where every actor is on one side.</summary>
        private readonly Dictionary<double, TimeBandLine?> _lines = [];

        /// <summary>Works out the day lines' natural places for an arrangement.</summary>
        /// <param name="places">The places.</param>
        /// <param name="times">The times.</param>
        /// <param name="actors">The actors with a time.</param>
        /// <param name="top">The lines' top.</param>
        /// <param name="bottom">The lines' bottom.</param>
        /// <param name="axis">The time axis.</param>
        public TimeBandCache(
            IReadOnlyDictionary<string, GraphPoint> places,
            GraphTimeline times,
            IReadOnlyList<TimedFrame> actors,
            double top,
            double bottom,
            TimeAxis axis)
        {
            _places = places;
            _times = times;
            Actors = actors;
            Top = top;
            Bottom = bottom;
            First = actors.Min(actor => actor.Hours);
            Last = actors.Max(actor => actor.Hours);
            Days = [.. axis.DayStarts(First, Last)
                .Select(day => (day.Hours, BaseX: TimeBands.BaseX(actors, day.Hours)))
                .Where(day => day.BaseX is not null)
                .Select(day => (day.Hours, day.BaseX!.Value))];
        }

        /// <summary>Gets the actors with a time.</summary>
        public IReadOnlyList<TimedFrame> Actors { get; }

        /// <summary>Gets the lines' top on the surface.</summary>
        public double Top { get; }

        /// <summary>Gets the lines' bottom on the surface.</summary>
        public double Bottom { get; }

        /// <summary>Gets the first actor's time, in hours.</summary>
        public double First { get; }

        /// <summary>Gets the last actor's time, in hours.</summary>
        public double Last { get; }

        /// <summary>Gets each day's start between the first and the last actor, with its line's natural place.</summary>
        public IReadOnlyList<(double Hours, double BaseX)> Days { get; }

        /// <summary>Says whether the lines were worked out for these places and times.</summary>
        /// <param name="places">The places.</param>
        /// <param name="times">The times.</param>
        /// <returns><see langword="true"/> when they were.</returns>
        public bool IsFor(IReadOnlyDictionary<string, GraphPoint> places, GraphTimeline times) =>
            ReferenceEquals(places, _places) && ReferenceEquals(times, _times);

        /// <summary>Gives the line at a time, worked out the first time it is asked for.</summary>
        /// <param name="hours">The time.</param>
        /// <returns>The line, or <see langword="null"/> when every actor is on one side of the time.</returns>
        public TimeBandLine? Line(double hours)
        {
            if (_lines.TryGetValue(hours, out var line))
            {
                return line;
            }

            if (_lines.Count >= MostCachedLines)
            {
                _lines.Clear();
            }

            line = TimeBands.Line(Actors, hours, Top, Bottom);
            _lines[hours] = line;
            return line;
        }
    }
}
