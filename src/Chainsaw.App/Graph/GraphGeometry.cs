namespace Chainsaw.App.Graph;

/// <summary>A point on the graph's surface, in the surface's own units (pixels at zoom 1).</summary>
/// <param name="X">The horizontal position.</param>
/// <param name="Y">The vertical position.</param>
public readonly record struct GraphPoint(double X, double Y)
{
    /// <summary>Gives the distance to another point.</summary>
    /// <param name="other">The other point.</param>
    /// <returns>The straight distance.</returns>
    public double DistanceTo(GraphPoint other) => Math.Sqrt(Square(X - other.X) + Square(Y - other.Y));

    /// <summary>Squares a number.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The number times itself.</returns>
    private static double Square(double value) => value * value;
}

/// <summary>A rectangle on the graph's surface, from its top-left corner.</summary>
/// <param name="X">The left edge.</param>
/// <param name="Y">The top edge.</param>
/// <param name="Width">The width, never negative.</param>
/// <param name="Height">The height, never negative.</param>
public readonly record struct GraphRect(double X, double Y, double Width, double Height)
{
    /// <summary>Gets the right edge.</summary>
    public double Right => X + Width;

    /// <summary>Gets the bottom edge.</summary>
    public double Bottom => Y + Height;

    /// <summary>Gets the centre.</summary>
    public GraphPoint Centre => new(X + (Width / 2), Y + (Height / 2));

    /// <summary>Gives the smallest rectangle that holds every point.</summary>
    /// <param name="points">The points; at least one.</param>
    /// <returns>The rectangle.</returns>
    public static GraphRect Around(IEnumerable<GraphPoint> points)
    {
        var list = points.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("A rectangle needs at least one point.", nameof(points));
        }

        var left = list.Min(point => point.X);
        var top = list.Min(point => point.Y);
        return new GraphRect(left, top, list.Max(point => point.X) - left, list.Max(point => point.Y) - top);
    }

    /// <summary>Says whether a point lies inside the rectangle or on its edge.</summary>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Contains(GraphPoint point) =>
        point.X >= X && point.X <= Right && point.Y >= Y && point.Y <= Bottom;

    /// <summary>Says whether two rectangles overlap or touch.</summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns><see langword="true"/> when they do.</returns>
    public bool Intersects(GraphRect other) =>
        other.X <= Right && other.Right >= X && other.Y <= Bottom && other.Bottom >= Y;

    /// <summary>Gives the rectangle grown by a margin on every side.</summary>
    /// <param name="margin">The margin.</param>
    /// <returns>The larger rectangle.</returns>
    public GraphRect Inflate(double margin) =>
        new(X - margin, Y - margin, Width + (2 * margin), Height + (2 * margin));

    /// <summary>Gives the smallest rectangle that holds both.</summary>
    /// <param name="other">The other rectangle.</param>
    /// <returns>The union.</returns>
    public GraphRect Union(GraphRect other)
    {
        var left = Math.Min(X, other.X);
        var top = Math.Min(Y, other.Y);
        return new GraphRect(left, top, Math.Max(Right, other.Right) - left, Math.Max(Bottom, other.Bottom) - top);
    }
}

/// <summary>The graph's measures, from sketch GR4, in the surface's units.</summary>
public static class GraphSizes
{
    /// <summary>A node's width.</summary>
    public const double NodeWidth = 132;

    /// <summary>A node's height.</summary>
    public const double NodeHeight = 40;

    /// <summary>
    /// The distance from one hop column to the next: a node and a gap wide enough for the longest amount on a line
    /// (<c>0,689997 av 1,0000</c>) to stand between two columns without touching either, so that the graph is airy as
    /// in sketch GR4 and the amounts never lie on the nodes (TODO 34).
    /// </summary>
    public const double LaneSpacing = 292;

    /// <summary>
    /// The distance from one node to the next in a hop column: room for two amounts of a fan of lines, one above the
    /// other, between two rows.
    /// </summary>
    public const double RowSpacing = 104;

    /// <summary>The space left of the first column.</summary>
    public const double Left = 16;

    /// <summary>The space above the first row, under the columns' names.</summary>
    public const double Top = 36;

    /// <summary>
    /// How far a node's marks reach outside its frame (the badge in the corner and the chain's name under it), so that
    /// a node whose frame is just outside the screen is still drawn when its marks are inside.
    /// </summary>
    public const double MarkReach = 12;

    /// <summary>Gives a node's frame at a place.</summary>
    /// <param name="place">The node's top-left corner.</param>
    /// <returns>The frame.</returns>
    public static GraphRect NodeFrame(GraphPoint place) => new(place.X, place.Y, NodeWidth, NodeHeight);
}
