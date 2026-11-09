namespace Chainsaw.App.Graph;

/// <summary>
/// Keeps the lines in the hop columns from running through the nodes they pass (sketch GR4): a hop with more actors
/// than <see cref="HopLayout.MostRows"/> is spread over columns side by side, and a line to the second of them passed
/// straight through a node in the first. Such a line is bent up or down, by as little as clears every node on its way,
/// into the gap between two rows.
/// </summary>
/// <remarks>
/// The ends stay where they are, so that the line still leaves and reaches its nodes at their ports; only the middle
/// moves (<see cref="EdgeCurve.Bent"/>). A line that no bend clears is left as it was rather than thrown far away.
/// </remarks>
public static class LineRouting
{
    /// <summary>How far a line keeps from a node's frame, in the surface's units.</summary>
    public const double Clearance = 6;

    /// <summary>How many points along a line are looked at.</summary>
    private const int Samples = 40;

    /// <summary>The smallest bend tried, a quarter of the room from one row to the next.</summary>
    private const double Step = GraphSizes.RowSpacing / 4;

    /// <summary>How many bends are tried to each side: up to two rows away.</summary>
    private const int Steps = 8;

    /// <summary>The bends tried, the smallest first and up before down: −26, +26, −52, +52 …</summary>
    private static readonly double[] Detours =
        [.. Enumerable.Range(1, Steps).SelectMany(step => new[] { -step * Step, step * Step })];

    /// <summary>Gives the room the nodes take, each frame grown by the clearance, to look lines up in.</summary>
    /// <param name="frames">The nodes' frames.</param>
    /// <returns>The room.</returns>
    public static RoomGrid Room(IEnumerable<GraphRect> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        var room = new RoomGrid();
        foreach (var frame in frames)
        {
            room.Add(frame.Inflate(Clearance));
        }

        return room;
    }

    /// <summary>
    /// Gives a line bent around the nodes it would run through, or itself when it runs through none. An arc over the
    /// top, or a loop beside a column, already keeps clear of the nodes and is left as it is.
    /// </summary>
    /// <param name="curve">The line.</param>
    /// <param name="from">The sender's frame.</param>
    /// <param name="to">The receiver's frame.</param>
    /// <param name="room">The room the nodes take (<see cref="Room"/>).</param>
    /// <returns>The line, bent when it had to be.</returns>
    public static EdgeCurve AroundNodes(EdgeCurve curve, GraphRect from, GraphRect to, RoomGrid room) =>
        Route(curve, from, to, room).Curve;

    /// <summary>
    /// Gives a line bent around the nodes it would run through, as <see cref="AroundNodes"/>, and the cells of the
    /// room it looked in to choose the bend: those of every point it tried, until a curve was clear. Only a node that
    /// comes into or leaves one of those cells can change how the line is routed, so a drag routes only those lines
    /// again (<see cref="SceneLayout.Move"/>).
    /// </summary>
    /// <param name="curve">The line.</param>
    /// <param name="from">The sender's frame.</param>
    /// <param name="to">The receiver's frame.</param>
    /// <param name="room">The room the nodes take (<see cref="Room"/>).</param>
    /// <returns>The line, bent when it had to be, and the cells looked in; none for an arc or a loop.</returns>
    public static (EdgeCurve Curve, IReadOnlySet<(int Column, int Row)> LookedIn) Route(
        EdgeCurve curve, GraphRect from, GraphRect to, RoomGrid room)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(room);
        var lookedIn = new HashSet<(int Column, int Row)>();
        if (curve.IsArc || !Crosses(curve, from, to, room, lookedIn))
        {
            return (curve, lookedIn);
        }

        foreach (var detour in Detours)
        {
            var bent = curve.Bent(detour);
            if (!Crosses(bent, from, to, room, lookedIn))
            {
                return (bent, lookedIn);
            }
        }

        return (curve, lookedIn);
    }

    /// <summary>Says whether a line runs through a node other than its two ends.</summary>
    /// <param name="curve">The line.</param>
    /// <param name="from">The sender's frame.</param>
    /// <param name="to">The receiver's frame.</param>
    /// <param name="room">The room the nodes take.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool Crosses(EdgeCurve curve, GraphRect from, GraphRect to, RoomGrid room) =>
        Crosses(curve, from, to, room, null);

    /// <summary>Says whether a line runs through a node other than its two ends, and notes the cells it looked in.</summary>
    /// <param name="curve">The line.</param>
    /// <param name="from">The sender's frame.</param>
    /// <param name="to">The receiver's frame.</param>
    /// <param name="room">The room the nodes take.</param>
    /// <param name="lookedIn">Where the cells looked in are noted, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool Crosses(
        EdgeCurve curve, GraphRect from, GraphRect to, RoomGrid room, HashSet<(int Column, int Row)>? lookedIn)
    {
        ArgumentNullException.ThrowIfNull(curve);
        ArgumentNullException.ThrowIfNull(room);
        var (start, end) = (from.Inflate(Clearance), to.Inflate(Clearance));
        for (var sample = 1; sample < Samples; sample++)
        {
            var point = curve.At((double)sample / Samples);
            if (start.Contains(point) || end.Contains(point))
            {
                continue;
            }

            lookedIn?.Add(room.CellOf(point));
            if (room.Holds(point))
            {
                return true;
            }
        }

        return false;
    }
}
