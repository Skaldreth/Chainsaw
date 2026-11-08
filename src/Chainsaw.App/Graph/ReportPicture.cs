namespace Chainsaw.App.Graph;

/// <summary>An actor in the report's small picture: where it stands, how it is drawn, and whether it is marked.</summary>
/// <param name="Frame">Where it stands in the graph.</param>
/// <param name="Ink">Its sort's colour.</param>
/// <param name="IsRound">Whether it is round rather than square.</param>
/// <param name="IsDashed">Whether its frame is dashed.</param>
/// <param name="IsMarked">Whether it is marked for the report; the rest is drawn faint.</param>
public sealed record PictureNode(GraphRect Frame, GraphInk Ink, bool IsRound, bool IsDashed, bool IsMarked);

/// <summary>A line in the report's small picture, from one actor's port to another's.</summary>
/// <param name="From">Where it leaves.</param>
/// <param name="To">Where it arrives.</param>
/// <param name="IsTraced">Whether it carries traced money (the accent), or not (grey).</param>
/// <param name="IsMarked">Whether both its ends are marked; the rest is drawn faint.</param>
public sealed record PictureLine(GraphPoint From, GraphPoint To, bool IsTraced, bool IsMarked);

/// <summary>
/// The small picture of the marking in the view Rapport (sketch RP1b): the graph as it stands, with the marked actors
/// and the lines between them lit and the rest faint, so that the user sees what the report is built of. Without WPF.
/// </summary>
/// <param name="Nodes">The actors with a place.</param>
/// <param name="Lines">The lines between them.</param>
/// <param name="Bounds">What the picture shows: the marked actors with room around, or everything when none is.</param>
public sealed record ReportPicture(
    IReadOnlyList<PictureNode> Nodes, IReadOnlyList<PictureLine> Lines, GraphRect Bounds)
{
    /// <summary>The room around the marked actors, so that their neighbours show at the edge.</summary>
    private const double Room = 120;

    /// <summary>Gets an empty picture.</summary>
    public static ReportPicture Empty { get; } = new([], [], new GraphRect(0, 0, 0, 0));

    /// <summary>Gets whether there is anything to draw.</summary>
    public bool HasNodes => Nodes.Count > 0;

    /// <summary>Draws the graph with what is marked lit.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="picked">The actors marked.</param>
    /// <param name="places">Where each node stands.</param>
    /// <returns>The picture.</returns>
    public static ReportPicture Of(
        GraphScene scene, IReadOnlyCollection<string> picked, IReadOnlyDictionary<string, GraphPoint> places)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(picked);
        ArgumentNullException.ThrowIfNull(places);
        var marked = picked.ToHashSet(StringComparer.Ordinal);
        var frames = new Dictionary<string, GraphRect>(StringComparer.Ordinal);
        var nodes = new List<PictureNode>();
        foreach (var node in scene.Nodes)
        {
            if (!places.TryGetValue(node.Key, out var place))
            {
                continue;
            }

            var frame = GraphSizes.NodeFrame(place, node.Width);
            frames[node.Key] = frame;
            nodes.Add(new PictureNode(
                frame, node.Look.Ink, node.Look.Shape == NodeShape.Round, node.Look.IsDashed, marked.Contains(node.Key)));
        }

        var lines = scene.Edges
            .Where(edge => frames.ContainsKey(edge.Edge.From) && frames.ContainsKey(edge.Edge.To))
            .Select(edge => new PictureLine(
                OutPort(frames[edge.Edge.From]),
                InPort(frames[edge.Edge.To]),
                edge.IsTraced,
                marked.Contains(edge.Edge.From) && marked.Contains(edge.Edge.To)))
            .ToList();
        return new ReportPicture(nodes, lines, BoundsOf(nodes));
    }

    /// <summary>Gives what the picture shows: the marked actors with room around, or every actor.</summary>
    /// <param name="nodes">The actors.</param>
    /// <returns>The bounds.</returns>
    private static GraphRect BoundsOf(IReadOnlyList<PictureNode> nodes)
    {
        var shown = nodes.Where(node => node.IsMarked).ToList();
        var room = shown.Count > 0 ? Room : 0;
        if (shown.Count == 0)
        {
            shown = [.. nodes];
        }

        if (shown.Count == 0)
        {
            return Empty.Bounds;
        }

        var left = shown.Min(node => node.Frame.X) - room;
        var top = shown.Min(node => node.Frame.Y) - room;
        var right = shown.Max(node => node.Frame.X + node.Frame.Width) + room;
        var bottom = shown.Max(node => node.Frame.Y + node.Frame.Height) + room;
        return new GraphRect(left, top, right - left, bottom - top);
    }

    /// <summary>Gives the port a line leaves a node by: the middle of its right side.</summary>
    /// <param name="frame">The node.</param>
    /// <returns>The port.</returns>
    private static GraphPoint OutPort(GraphRect frame) => new(frame.X + frame.Width, frame.Y + (frame.Height / 2));

    /// <summary>Gives the port a line comes into a node by: the middle of its left side.</summary>
    /// <param name="frame">The node.</param>
    /// <returns>The port.</returns>
    private static GraphPoint InPort(GraphRect frame) => new(frame.X, frame.Y + (frame.Height / 2));
}
