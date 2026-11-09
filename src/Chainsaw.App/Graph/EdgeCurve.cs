namespace Chainsaw.App.Graph;

/// <summary>
/// A line between two nodes as a cubic Bézier curve (sketch GR4). A line always leaves its sender at the out port on
/// the right side and reaches its receiver at the in port on the left side (the user's principle 2026-11-09: right is
/// outgoing, left is incoming), whichever way the nodes stand. Forward it is a plain curve; money that turns back, or
/// runs between two nodes in one column, swings out to the right of the sender, over the top of both nodes and into
/// the receiver from the left.
/// </summary>
public sealed record EdgeCurve(GraphPoint Start, GraphPoint Control1, GraphPoint Control2, GraphPoint End, bool IsArc)
{
    /// <summary>The least pull of a curve's control points, so that a short line still bends.</summary>
    private const double MinimumPull = 30;

    /// <summary>How high an arc rises above the higher of its two nodes.</summary>
    private const double ArcLift = 60;

    /// <summary>How much more an arc rises for each unit it spans sideways.</summary>
    private const double ArcLiftPerSpan = 0.12;

    /// <summary>
    /// How far a line that turns back reaches out sideways from its ports: within the gap to the next column, so that
    /// it never runs behind that column's nodes.
    /// </summary>
    private const double TurnReach = (GraphSizes.LaneSpacing - GraphSizes.NodeWidth) * 0.8;

    /// <summary>How many straight pieces a curve is cut into when it is measured.</summary>
    private const int Pieces = 24;

    /// <summary>How close two columns may be and still count as the same, in the surface's units.</summary>
    private const double SameColumn = 4;

    /// <summary>How much of a move of both control points reaches the curve's middle.</summary>
    private const double MiddleShare = 0.75;

    /// <summary>
    /// How far apart the middles of two lines between the same two nodes are drawn: more than an amount is tall, so
    /// that both amounts can stand halfway.
    /// </summary>
    public const double ParallelSpacing = 28;

    /// <summary>Gets the point halfway along the curve, where its amount is written.</summary>
    public GraphPoint Middle => At(0.5);

    /// <summary>
    /// Gives the line from one node to another of the graph's width. The two places are the nodes' top-left corners.
    /// </summary>
    /// <param name="from">The sender's place.</param>
    /// <param name="to">The receiver's place.</param>
    /// <param name="hopColumns">Whether the graph is sorted in hop columns.</param>
    /// <returns>The curve.</returns>
    public static EdgeCurve Between(GraphPoint from, GraphPoint to, bool hopColumns) =>
        Between(GraphSizes.NodeFrame(from), GraphSizes.NodeFrame(to), hopColumns);

    /// <summary>
    /// Gives the line from one node to another, each as wide as it is drawn (sketch GR5: a node grows with its name).
    /// </summary>
    /// <param name="from">The sender's frame.</param>
    /// <param name="to">The receiver's frame.</param>
    /// <param name="hopColumns">Whether the graph is sorted in hop columns.</param>
    /// <returns>The curve.</returns>
    public static EdgeCurve Between(GraphRect from, GraphRect to, bool hopColumns)
    {
        const double half = GraphSizes.NodeHeight / 2;
        var outPort = new GraphPoint(from.Right, from.Y + half);
        var inPort = new GraphPoint(to.X, to.Y + half);
        if (to.X - from.X >= SameColumn)
        {
            return Curve(outPort, inPort, Math.Max(MinimumPull, (inPort.X - outPort.X) / 2));
        }

        // Back, or within one column: out to the right, over the top of both nodes, and in from the left. In the hop
        // columns it is an arc that the routing leaves alone; on the free surface the routing may still bend it.
        var lift = Math.Min(from.Y, to.Y) - ArcLift - (Math.Abs(outPort.X - inPort.X) * ArcLiftPerSpan);
        return new EdgeCurve(
            outPort,
            new GraphPoint(outPort.X + TurnReach, lift),
            new GraphPoint(inPort.X - TurnReach, lift),
            inPort,
            IsArc: hopColumns);
    }

    /// <summary>
    /// Gives the curve bent up or down by a distance at its middle, so that two lines between the same two nodes, as
    /// money that goes back and forth, are drawn apart and each keeps room for its amount (TODO 34).
    /// </summary>
    /// <param name="offset">How far the middle moves; below zero is up.</param>
    /// <returns>The bent curve, its ends unchanged.</returns>
    public EdgeCurve Bent(double offset)
    {
        // The middle of a cubic Bézier curve is (P0 + 3·P1 + 3·P2 + P3) / 8, so moving both control points by d moves
        // it by 3/4 of d.
        var shift = offset / MiddleShare;
        return this with
        {
            Control1 = Control1 with { Y = Control1.Y + shift },
            Control2 = Control2 with { Y = Control2.Y + shift },
        };
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
    public GraphRect Bounds()
    {
        // Without a list of the four points: this is asked for every line in every hit test and every scene drawn.
        var left = Math.Min(Math.Min(Start.X, Control1.X), Math.Min(Control2.X, End.X));
        var top = Math.Min(Math.Min(Start.Y, Control1.Y), Math.Min(Control2.Y, End.Y));
        var right = Math.Max(Math.Max(Start.X, Control1.X), Math.Max(Control2.X, End.X));
        var bottom = Math.Max(Math.Max(Start.Y, Control1.Y), Math.Max(Control2.Y, End.Y));
        return new GraphRect(left, top, right - left, bottom - top);
    }

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
