namespace Chainsaw.App.Graph;

/// <summary>
/// A line between two nodes as a cubic Bézier curve (sketch GR4): forward from the sender's right side to the
/// receiver's left side, backward on the free surface from the left side to the right side, in the hop columns
/// money that turns back as an arc over the top, and between two nodes in one column a loop out to the right.
/// </summary>
/// <param name="Start">Where the line leaves the sender.</param>
/// <param name="Control1">The first control point.</param>
/// <param name="Control2">The second control point.</param>
/// <param name="End">Where the line reaches the receiver.</param>
/// <param name="IsArc">Whether the line is drawn as an arc over the top.</param>
public sealed record EdgeCurve(GraphPoint Start, GraphPoint Control1, GraphPoint Control2, GraphPoint End, bool IsArc)
{
    /// <summary>The least pull of a curve's control points, so that a short line still bends.</summary>
    private const double MinimumPull = 30;

    /// <summary>How high an arc rises above the higher of its two nodes.</summary>
    private const double ArcLift = 60;

    /// <summary>How much more an arc rises for each unit it spans sideways.</summary>
    private const double ArcLiftPerSpan = 0.12;

    /// <summary>
    /// How far a loop between two nodes in one column reaches out to the right: within the gap to the next column, so
    /// that it never runs behind that column's nodes.
    /// </summary>
    private const double SideLoop = (GraphSizes.LaneSpacing - GraphSizes.NodeWidth) * 0.8;

    /// <summary>How many straight pieces a curve is cut into when it is measured.</summary>
    private const int Pieces = 24;

    /// <summary>How close two columns may be and still count as the same, in the surface's units.</summary>
    private const double SameColumn = 4;

    /// <summary>Gets the point halfway along the curve, where its amount is written.</summary>
    public GraphPoint Middle => At(0.5);

    /// <summary>
    /// Gives the line from one node to another. The two places are the nodes' top-left corners.
    /// </summary>
    /// <param name="from">The sender's place.</param>
    /// <param name="to">The receiver's place.</param>
    /// <param name="hopColumns">Whether the graph is sorted in hop columns.</param>
    /// <returns>The curve.</returns>
    public static EdgeCurve Between(GraphPoint from, GraphPoint to, bool hopColumns)
    {
        const double width = GraphSizes.NodeWidth;
        const double half = GraphSizes.NodeHeight / 2;
        if (Math.Abs(to.X - from.X) < SameColumn)
        {
            // In one column: a loop out to the right, from side to side, never a line through the nodes between.
            var right = from.X + width;
            const double reach = SideLoop;
            return new EdgeCurve(
                new GraphPoint(right, from.Y + half),
                new GraphPoint(right + reach, from.Y + half),
                new GraphPoint(right + reach, to.Y + half),
                new GraphPoint(to.X + width, to.Y + half),
                IsArc: true);
        }

        if (!hopColumns && to.X < from.X)
        {
            // Backward on the free surface: from the sender's left side to the receiver's right side.
            var (x1, x2) = (from.X, to.X + width);
            var pull = Math.Max(MinimumPull, (x1 - x2) / 2);
            return Curve(new GraphPoint(x1, from.Y + half), new GraphPoint(x2, to.Y + half), -pull);
        }

        if (to.X < from.X)
        {
            // Back in the hops: an arc over the top, from top centre to top centre.
            var (x1, x2) = (from.X + (width / 2), to.X + (width / 2));
            var lift = Math.Min(from.Y, to.Y) - ArcLift - (Math.Abs(x1 - x2) * ArcLiftPerSpan);
            return new EdgeCurve(
                new GraphPoint(x1, from.Y),
                new GraphPoint(x1, lift),
                new GraphPoint(x2, lift),
                new GraphPoint(x2, to.Y),
                IsArc: true);
        }

        var (start, end) = (from.X + width, to.X);
        return Curve(
            new GraphPoint(start, from.Y + half),
            new GraphPoint(end, to.Y + half),
            Math.Max(MinimumPull, (end - start) / 2));
    }

    /// <summary>Gives the point at a place along the curve.</summary>
    /// <param name="t">How far along, from 0 at the start to 1 at the end.</param>
    /// <returns>The point.</returns>
    public GraphPoint At(double t)
    {
        var u = 1 - t;
        var (a, b, c, d) = (u * u * u, 3 * u * u * t, 3 * u * t * t, t * t * t);
        return new GraphPoint(
            (a * Start.X) + (b * Control1.X) + (c * Control2.X) + (d * End.X),
            (a * Start.Y) + (b * Control1.Y) + (c * Control2.Y) + (d * End.Y));
    }

    /// <summary>
    /// Gives a rectangle that holds the whole curve: a Bézier curve always lies within its four points.
    /// </summary>
    /// <returns>The rectangle.</returns>
    public GraphRect Bounds() => GraphRect.Around([Start, Control1, Control2, End]);

    /// <summary>Gives the shortest distance from a point to the curve, measured along straight pieces of it.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The distance.</returns>
    public double DistanceTo(GraphPoint point)
    {
        var best = double.MaxValue;
        var previous = Start;
        for (var piece = 1; piece <= Pieces; piece++)
        {
            var next = At((double)piece / Pieces);
            best = Math.Min(best, DistanceToSegment(point, previous, next));
            previous = next;
        }

        return best;
    }

    /// <summary>Gives a curve between two points whose control points pull straight out sideways.</summary>
    /// <param name="start">The start.</param>
    /// <param name="end">The end.</param>
    /// <param name="pull">How far the control points pull, right from the start and left into the end.</param>
    /// <returns>The curve.</returns>
    private static EdgeCurve Curve(GraphPoint start, GraphPoint end, double pull) => new(
        start,
        new GraphPoint(start.X + pull, start.Y),
        new GraphPoint(end.X - pull, end.Y),
        end,
        IsArc: false);

    /// <summary>Gives the shortest distance from a point to a straight piece.</summary>
    /// <param name="point">The point.</param>
    /// <param name="a">The piece's start.</param>
    /// <param name="b">The piece's end.</param>
    /// <returns>The distance.</returns>
    private static double DistanceToSegment(GraphPoint point, GraphPoint a, GraphPoint b)
    {
        var (dx, dy) = (b.X - a.X, b.Y - a.Y);
        var length = (dx * dx) + (dy * dy);
        if (length == 0)
        {
            return point.DistanceTo(a);
        }

        var t = Math.Clamp((((point.X - a.X) * dx) + ((point.Y - a.Y) * dy)) / length, 0, 1);
        return point.DistanceTo(new GraphPoint(a.X + (t * dx), a.Y + (t * dy)));
    }
}
