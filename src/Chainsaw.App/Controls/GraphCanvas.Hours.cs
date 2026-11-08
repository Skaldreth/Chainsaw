using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The hours in a zoomed day band (sketch TL2a): when a day is wide enough on screen, prickled lines at 06, 12 and 18
/// run through it, fainter than the days' own, bending round the actors as the days' lines do, with the hour small at
/// the top.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>The hours a zoomed day band is divided at.</summary>
    private static readonly int[] DayHours = [6, 12, 18];

    /// <summary>How wide a day must be on screen before its hours are drawn: room for four quarters.</summary>
    private const double LeastDayForHours = 320;

    /// <summary>An hour line's width on screen, in pixels.</summary>
    private const double HourPixels = 0.8;

    /// <summary>Where the hours' names stand on screen, from the top: under the days' names.</summary>
    private const double HourNameTop = 80;

    /// <summary>The dots of an hour line: a dot every three pixels on screen (the sketch's <c>.01 3</c>).</summary>
    private static readonly DashStyle HourDots = Frozen(new DashStyle([0.01 / HourPixels, 3 / HourPixels], 0));

    /// <summary>Draws the hour lines of the day bands that are wide enough on screen, in surface units.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="bands">The lines of the graph drawn.</param>
    /// <param name="visible">The visible part of the surface.</param>
    private void DrawHourLines(DrawingContext dc, TimeBandCache bands, GraphRect visible)
    {
        var ink = ResourceColor("Color.Graph.HourLine");
        foreach (var hours in HoursShown(bands))
        {
            if (bands.Line(hours) is { } line)
            {
                DrawBandLine(dc, bands, line, visible, ink, HourPixels, HourDots);
            }
        }
    }

    /// <summary>Writes the hours small at the top of their lines, in screen units.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="bands">The lines of the graph drawn.</param>
    private void DrawHourNames(DrawingContext dc, TimeBandCache bands)
    {
        var axis = _model!.Timeline.Axis;
        var y = _viewport.ToSurface(new GraphPoint(0, HourNameTop)).Y;
        var ink = GraphPalette.Find(this, "Brush.TextDim");
        foreach (var hours in HoursShown(bands))
        {
            if (bands.Line(hours) is not { } line)
            {
                continue;
            }

            var x = _viewport.ToScreen(new GraphPoint(line.XAt(y), 0)).X;
            var name = axis.HourOf(axis.Time(hours)).ToString("00", CultureInfo.InvariantCulture);
            var text = CachedText(name, 10.5, ink, FontWeights.Normal);
            if (x > 0 && x < ActualWidth)
            {
                dc.DrawText(text, new Point(x - (text.Width / 2), HourNameTop));
            }
        }
    }

    /// <summary>
    /// Gives the hours to draw: 06, 12 and 18 of each day whose band is at least <see cref="LeastDayForHours"/> wide
    /// on screen, between the first and the last actor.
    /// </summary>
    /// <param name="bands">The lines of the graph drawn.</param>
    /// <returns>The hours, in hours on the axis.</returns>
    private IEnumerable<double> HoursShown(TimeBandCache bands)
    {
        var days = bands.Days;
        for (var index = 0; index + 1 < days.Count; index++)
        {
            var (start, startX) = days[index];
            var (end, endX) = days[index + 1];
            if ((endX - startX) * _viewport.Zoom < LeastDayForHours)
            {
                continue;
            }

            foreach (var hour in DayHours)
            {
                var at = start + ((end - start) * hour / 24);
                if (at > bands.First && at < bands.Last)
                {
                    yield return at;
                }
            }
        }
    }
}
