using Chainsaw.App.Graph;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary><em>Sortera grafen</em>: the sorted free surface, airy, left to right and the same every time.</summary>
public sealed class TidyLayoutTests
{
    /// <summary>The made-up graph's lines as pairs of keys.</summary>
    private static readonly (string, string)[] SampleLinks =
        [(WKey, VKey), (VKey, KKey), (VKey, UKey), (KKey, EKey), (EKey, KKey)];

    /// <summary>The money runs left to right in the hops' order, the start's column after what came before it.</summary>
    [Fact]
    public void ColumnsRunLeftToRightInHopOrder()
    {
        var places = TidyLayout.Arrange(GraphSamples.Graph().Nodes, SampleLinks);

        Assert.True(places[WKey].X < places[VKey].X);
        Assert.True(places[VKey].X < places[KKey].X);
        Assert.Equal(places[KKey].X, places[UKey].X);
        Assert.True(places[KKey].X < places[EKey].X);
        Assert.Equal(TidyLayout.ColumnSpacing, places[VKey].X - places[WKey].X);
    }

    /// <summary>
    /// No two nodes overlap, and none stand closer than a node and a full gap for the amounts: it is airy, not compact.
    /// </summary>
    [Fact]
    public void NodesNeverOverlapAndKeepAnAiryDistance()
    {
        var (nodes, links) = Fan(40);

        var places = TidyLayout.Arrange(nodes, links).Values.ToList();

        for (var i = 0; i < places.Count; i++)
        {
            for (var j = i + 1; j < places.Count; j++)
            {
                var (a, b) = (places[i], places[j]);
                Assert.False(GraphSizes.NodeFrame(a).Intersects(GraphSizes.NodeFrame(b)));
                var apartSideways = Math.Abs(a.X - b.X) >= TidyLayout.ColumnSpacing;
                var apartInHeight = Math.Abs(a.Y - b.Y) >= TidyLayout.RowSpacing;
                Assert.True(apartSideways || apartInHeight, $"{a} and {b} are too close");
            }
        }

        Assert.True(TidyLayout.RowSpacing > GraphSizes.RowSpacing);
    }

    /// <summary>Many counterparties of one hop are spread over the surface, never one long column.</summary>
    [Fact]
    public void ManyCounterpartiesUseTheSurface()
    {
        var (nodes, links) = Fan(60);

        var places = TidyLayout.Arrange(nodes, links);
        var fan = nodes.Where(node => node.Hop == 1).Select(node => places[node.Key]).ToList();

        Assert.True(fan.Select(place => place.X).Distinct().Count() > 1);
        Assert.True(fan.Select(place => place.Y).Distinct().Count() <= HopLayout.RowsFor(60));
    }

    /// <summary>The same graph is sorted the same way every time.</summary>
    [Fact]
    public void SortingIsDeterministic()
    {
        var (nodes, links) = Fan(25);

        var first = TidyLayout.Arrange(nodes, links);
        var second = TidyLayout.Arrange(nodes, links);

        Assert.Equal(first.OrderBy(pair => pair.Key), second.OrderBy(pair => pair.Key));
    }

    /// <summary>An actor stands at the height of the actor it has a line from, so that lines need not cross.</summary>
    [Fact]
    public void ReceiversFollowTheirSenders()
    {
        GraphNode[] nodes =
        [
            Node("a", GraphNodeKind.Address, [], hop: 0, start: true),
            Node("b", GraphNodeKind.Address, [], hop: 0, start: true),
            Node("to-b", GraphNodeKind.Address, [], hop: 1),
            Node("to-a", GraphNodeKind.Address, [], hop: 1),
        ];

        var places = TidyLayout.Arrange(nodes, [("a", "to-a"), ("b", "to-b")]);

        // Whichever start stands on top, its receiver does too: the two lines never cross.
        Assert.Equal(places["a"].Y < places["b"].Y, places["to-a"].Y < places["to-b"].Y);
        Assert.Equal(places["a"].Y, places["to-a"].Y);
    }

    /// <summary>The actors no start point reaches stand in a column of their own, last; the top is under the names.</summary>
    [Fact]
    public void ActorsWithoutHopStandLastAndTheTopIsUnderTheNames()
    {
        GraphNode[] nodes =
        [
            Node("loose", GraphNodeKind.Address, [], hop: null),
            Node("start", GraphNodeKind.Address, [], hop: 0, start: true),
            Node("next", GraphNodeKind.Address, [], hop: 1),
        ];

        var places = TidyLayout.Arrange(nodes, [("start", "next")]);

        Assert.True(places["loose"].X > places["next"].X);
        Assert.Equal(GraphSizes.Left, places["start"].X);
        Assert.Equal(GraphSizes.Top, places.Values.Min(place => place.Y));
    }

    /// <summary>An empty graph gives no places.</summary>
    [Fact]
    public void EmptyGraphGivesNoPlaces() => Assert.Empty(TidyLayout.Arrange([], []));

    /// <summary>A start that pays many counterparties, some of which pay on.</summary>
    /// <param name="count">The counterparties.</param>
    /// <returns>The nodes and lines.</returns>
    private static (List<GraphNode> Nodes, List<(string, string)> Links) Fan(int count)
    {
        var nodes = new List<GraphNode> { Node("start", GraphNodeKind.Address, [], hop: 0, start: true) };
        var links = new List<(string, string)>();
        for (var index = 0; index < count; index++)
        {
            var key = "fan-" + index;
            nodes.Add(Node(key, GraphNodeKind.Address, [], hop: 1));
            links.Add(("start", key));
            if (index % 5 == 0)
            {
                var next = "next-" + index;
                nodes.Add(Node(next, GraphNodeKind.Address, [], hop: 2));
                links.Add((key, next));
            }
        }

        return (nodes, links);
    }
}
