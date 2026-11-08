using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// The empty graph's third state (sketch GR6c, <em>efter startpunkten</em>): the start point stands as a real node,
/// not expanded and with no lines yet, and the graph guides on from it. A guide node <em>Expandera</em> stands beside
/// it, ringed by the accent's sonar, with a dotted line from the start point; <em>Hitta uttaget</em> stands faintly
/// after it. The guide goes as soon as anything has been fetched, so that it never stands in the way of the work.
/// </summary>
/// <param name="StartKey">The start point's node, which the guide expands.</param>
/// <param name="Start">The start point's frame.</param>
/// <param name="Expand">The guide node <em>Expandera</em>.</param>
/// <param name="Exit">The faint guide node <em>Hitta uttaget</em> after it.</param>
public sealed record StartGuide(string StartKey, GraphRect Start, GraphRect Expand, GraphRect Exit)
{
    /// <summary>A guide node's width, as in sketch GR6c.</summary>
    public const double NodeWidth = 200;

    /// <summary>A guide node's height, as in sketch GR6c.</summary>
    public const double NodeHeight = 54;

    /// <summary>The space between the start point and <em>Expandera</em>.</summary>
    public const double ExpandGap = 140;

    /// <summary>The space between <em>Expandera</em> and <em>Hitta uttaget</em>.</summary>
    public const double ExitGap = 120;

    /// <summary>How far below <em>Expandera</em> the faint <em>Hitta uttaget</em> stands.</summary>
    public const double ExitDrop = 90;

    /// <summary><em>Expandera</em>'s title.</summary>
    public const string ExpandTitle = "Expandera";

    /// <summary><em>Expandera</em>'s second line: what to do.</summary>
    public const string ExpandHint = "E, eller klicka här";

    /// <summary><em>Expandera</em>'s explanation on hovering.</summary>
    public const string ExpandTip =
        "Hämtar aktörens tx genom utgången och ritar motparterna.\nBelopp och spårad andel står på linjerna.";

    /// <summary>What the line under <em>Expandera</em> says when lookups can be made.</summary>
    public const string OnlineText = "Hämtar startpunktens tx och ritar motparterna.";

    /// <summary>What the line under <em>Expandera</em> says when no exit is connected (principle 9).</summary>
    public const string OfflineText = "Offline: expansionen kan inte hämta något förrän en utgång är ansluten.";

    /// <summary><em>Hitta uttaget</em>'s title.</summary>
    public const string ExitTitle = "Hitta uttaget";

    /// <summary><em>Hitta uttaget</em>'s second line.</summary>
    public const string ExitHint = "Blått är uttag";

    /// <summary><em>Hitta uttaget</em>'s explanation on hovering.</summary>
    public const string ExitTip =
        "Följ pengarna tills de når en blå nod: en börs eller ett annat uttag.\nDär finns den som tar ut pengarna.";

    /// <summary>Gets the room the guide takes with its start point, for fitting the graph to the screen.</summary>
    public GraphRect Bounds => Start.Union(Expand).Union(Exit);

    /// <summary>Gets the dotted line from the start point to <em>Expandera</em>.</summary>
    public EdgeCurve ToExpand => EdgeCurve.Between(Start, Expand, hopColumns: false);

    /// <summary>Gets the faint dotted line from <em>Expandera</em> to <em>Hitta uttaget</em>.</summary>
    public EdgeCurve ToExit => EdgeCurve.Between(Expand, Exit, hopColumns: false);

    /// <summary>
    /// Gives the guide for a graph, or <see langword="null"/> when it has none: only a graph with no lines yet, whose
    /// first placed start point is not expanded, gets one.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="places">Each node's top-left corner.</param>
    /// <returns>The guide, or <see langword="null"/>.</returns>
    public static StartGuide? For(GraphScene? scene, IReadOnlyDictionary<string, GraphPoint> places)
    {
        ArgumentNullException.ThrowIfNull(places);
        if (scene is null || scene.Edges.Count > 0)
        {
            return null;
        }

        var start = scene.Nodes.FirstOrDefault(node => node.IsStartPoint && places.ContainsKey(node.Key));
        if (start is null || start.Node.Expansion != NodeExpansion.NotExpanded)
        {
            return null;
        }

        var frame = scene.FrameOf(start.Key, places[start.Key]);
        var middle = frame.Centre.Y;
        var expand = new GraphRect(frame.Right + ExpandGap, middle - (NodeHeight / 2), NodeWidth, NodeHeight);
        var exit = new GraphRect(expand.Right + ExitGap, expand.Y + ExitDrop, NodeWidth, NodeHeight);
        return new StartGuide(start.Key, frame, expand, exit);
    }
}
