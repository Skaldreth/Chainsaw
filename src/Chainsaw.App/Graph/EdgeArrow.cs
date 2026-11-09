namespace Chainsaw.App.Graph;

/// <summary>
/// A small open chevron on a line that shows which way the money flows (the user's wish 2026-11-09), so that the
/// direction can be read out in the graph, far from the ports. It stands where it touches neither the line's amount nor
/// its swap pill, and its wings reach out past the line, so that it is seen on a thick line too.
/// </summary>
/// <param name="Tip">The chevron's point, towards the receiver.</param>
/// <param name="Wing1">The end of one wing.</param>
/// <param name="Wing2">The end of the other wing.</param>
public sealed record EdgeArrow(GraphPoint Tip, GraphPoint Wing1, GraphPoint Wing2)
{
    /// <summary>How far the wings reach out past the line's edge on each side.</summary>
    public const double WingReach = 4;

    /// <summary>The least length of a line, along it, that gets an arrow; a shorter line is plain between its ports.</summary>
    public const double ShortestLine = 80;

    /// <summary>How far back along the line the wings go, as a share of their reach to the side.</summary>
    private const double WingBack = 0.9;

    /// <summary>How far around a place along the line its direction is measured.</summary>
    private const double Step = 0.01;

    /// <summary>
    /// The places along the line that are tried, in order: the middle first, then ever further from it, so that the
    /// arrow stands out in the graph rather than at a node.
    /// </summary>
    private static readonly double[] Along = [0.5, 0.36, 0.64, 0.24, 0.76];

    /// <summary>Gives the arrow on a line, at the first place along it that is clear of what stands on the line.</summary>
    /// <param name="curve">The line's curve.</param>
    /// <param name="width">The line's width.</param>
    /// <param name="keepClear">What stands on the line: its amount and its swap pill.</param>
    /// <returns>The arrow, or <see langword="null"/> for a line too short to carry one.</returns>
    public static EdgeArrow? On(EdgeCurve curve, double width, IReadOnlyList<GraphRect> keepClear)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(keepClear);
        if (Length(curve) < ShortestLine)
        {
            return null;
        }

        var arrows = Along.Select(t => At(curve, t, width)).ToList();
        return arrows.FirstOrDefault(arrow => !keepClear.Any(box => box.Intersects(arrow.Bounds()))) ?? arrows[^1];
    }

    /// <summary>Gives the rectangle that holds the arrow.</summary>
    /// <returns>The rectangle.</returns>
    public GraphRect Bounds() => GraphRect.Around([Tip, Wing1, Wing2]);

    /// <summary>Gives the arrow at a place along the line, pointing the way the line runs there.</summary>
    /// <param name="curve">The line's curve.</param>
    /// <param name="t">How far along, from 0 at the sender to 1 at the receiver.</param>
    /// <param name="width">The line's width.</param>
    /// <returns>The arrow.</returns>
    private static EdgeArrow At(EdgeCurve curve, double t, double width)
    {
        var (before, after) = (curve.At(t - Step), curve.At(t + Step));
        var (dx, dy) = (after.X - before.X, after.Y - before.Y);
        var length = Math.Max(Math.Sqrt((dx * dx) + (dy * dy)), double.Epsilon);
        var (ux, uy) = (dx / length, dy / length);
        var side = (width / 2) + WingReach;
        var back = side * WingBack;
        var centre = curve.At(t);
        var tip = new GraphPoint(centre.X + (ux * back / 2), centre.Y + (uy * back / 2));
        var foot = new GraphPoint(tip.X - (ux * back), tip.Y - (uy * back));
        return new EdgeArrow(
            tip,
            new GraphPoint(foot.X - (uy * side), foot.Y + (ux * side)),
            new GraphPoint(foot.X + (uy * side), foot.Y - (ux * side)));
    }

    /// <summary>Gives a line's length, measured along straight pieces of it.</summary>
    /// <param name="curve">The line's curve.</param>
    /// <returns>The length.</returns>
    private static double Length(EdgeCurve curve)
    {
        const int pieces = 16;
        var total = 0.0;
        var previous = curve.Start;
        for (var piece = 1; piece <= pieces; piece++)
        {
            var next = curve.At((double)piece / pieces);
            total += previous.DistanceTo(next);
            previous = next;
        }

        return total;
    }
}
