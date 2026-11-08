using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// The piles, the clues and the hidden outputs as the graph draws them (sketches EX1, EX2 and EX2b), the fan, the
/// layout that uses the surface, and the graph's view model around them; without WPF.
/// </summary>
public sealed class PileTests
{
    /// <summary>The pile's key in the made-up graph.</summary>
    private const string PileKey = "pile:out:" + VKey;

    /// <summary>The clue's key in the made-up graph.</summary>
    private const string ClueKey = "held-back:" + VKey;

    /// <summary>Gives a member's key.</summary>
    private static string Member(int index) => "address:p" + index;

    /// <summary>
    /// The made-up graph with V paying a number of new receivers in a pile on its out side, 0.01 BTC each, and a clue
    /// that 1 709 transactions are held back at V.
    /// </summary>
    private static TraceGraph Piled(int count = 12)
    {
        var graph = GraphSamples.Graph();
        var members = Enumerable.Range(0, count).Select(Member).ToList();
        var nodes = members.Select((key, index) => Node(
            key, GraphNodeKind.Address, [Address(200 + index)], hop: 1, expansion: NodeExpansion.NotExpanded));
        var edges = members.Select((key, index) => Edge("v-p" + index, VKey, key, 1, 2 + index, 0.01m));
        var none = new Dictionary<GraphLayout, NodePosition>();
        return graph with
        {
            Nodes = [.. graph.Nodes, .. nodes],
            Edges = [.. graph.Edges, .. edges],
            Piles = [new GraphPile(PileKey, VKey, PileSide.Out, members, [], none)],
            HeldBack = [new HeldBackClue(ClueKey, VKey, 1709, [ExpansionRequest.ForAddress(V, 100)], none)],
        };
    }

    [Fact]
    public void The_members_of_a_pile_are_drawn_as_one_square_node_with_one_thick_line_and_the_sum()
    {
        var scene = Scene(graph: Piled());

        Assert.All(Enumerable.Range(0, 12), index => Assert.Null(scene.Node(Member(index))));
        var pile = scene.Node(PileKey)!;
        Assert.Equal(NodeSort.Pile, pile.Look.Sort);
        Assert.Equal("12 mottagare", pile.Name);
        Assert.Equal("0,120 BTC · 1 tx", pile.Subject);
        var line = Assert.Single(scene.Edges, edge => edge.Edge.To == PileKey);
        Assert.Equal(VKey, line.Edge.From);
        Assert.Equal(Btc(0.12m), line.Edge.Amount);
        Assert.Equal(12, line.Edge.Transfers.Count);
        Assert.Equal("0,120 BTC · 1 tx", line.Label);
        Assert.Equal(GraphFold.PileWidth(Btc(0.12m)), line.Width);
        Assert.Equal(12, scene.Pile(PileKey)!.Cards.Count);
        Assert.Equal(Btc(0.12m), scene.Pile(PileKey)!.Sum);
        Assert.Equal(17, scene.Snapshot.Graph.Nodes.Count);
    }

    [Fact]
    public void A_labelled_member_is_never_folded_into_the_pile()
    {
        var labels = new Dictionary<string, IReadOnlyList<Label>>
        {
            [Member(3)] = [Label(Address(203), LabelKind.Exchange, "Börsen")],
        };

        var scene = Scene(labels, Piled());

        Assert.NotNull(scene.Node(Member(3)));
        Assert.Equal(11, scene.Pile(PileKey)!.Cards.Count);
    }

    [Fact]
    public void What_was_held_back_is_a_dashed_clue_with_a_thin_line_from_its_actor()
    {
        var scene = Scene(graph: Piled());

        var clue = scene.Node(ClueKey)!;
        Assert.Equal(NodeSort.HeldBack, clue.Look.Sort);
        Assert.True(clue.Look.IsDashed);
        Assert.StartsWith("+1", clue.Name, StringComparison.Ordinal);
        Assert.EndsWith("709 tx", clue.Name, StringComparison.Ordinal);
        var line = Assert.Single(scene.Edges, edge => edge.Edge.To == ClueKey);
        Assert.Equal(string.Empty, line.Label);
        Assert.Equal(EdgeDash.Uncertain, line.Dash);
    }

    [Fact]
    public void A_clue_without_a_count_says_there_are_more()
    {
        var clue = new HeldBackClue(ClueKey, VKey, null, [], new Dictionary<GraphLayout, NodePosition>());

        Assert.Equal("+ fler tx", GraphFold.ClueName(clue));
    }

    [Fact]
    public void An_empty_op_return_is_hidden_until_shown_and_one_that_burns_money_never_is()
    {
        var graph = WithOpReturns();

        var hidden = Scene(graph: graph);
        var shown = hidden.WithUnspendable(true);

        Assert.Equal(1, hidden.Unspendable);
        Assert.Null(hidden.Node("coin:empty"));
        Assert.NotNull(hidden.Node("coin:burn"));
        Assert.DoesNotContain(hidden.Edges, edge => edge.Edge.To == "coin:empty");
        Assert.NotNull(shown.Node("coin:empty"));
        Assert.True(shown.ShowsUnspendable);
        Assert.Same(hidden, hidden.WithUnspendable(false));
    }

    /// <summary>The made-up graph with V paying two OP_RETURN outputs, one of nothing and one that burns money.</summary>
    private static TraceGraph WithOpReturns()
    {
        var graph = GraphSamples.Graph();
        var tx = graph.Transactions[Tx[1]].Transaction;
        var outputs = tx.Outputs.Concat([
            new BitcoinOutput(2, BitcoinScriptType.OpReturn, null, Satoshi.Zero, "6a", null),
            new BitcoinOutput(3, BitcoinScriptType.OpReturn, null, new Satoshi(1_000), "6a", null),
        ]).ToList();
        var transactions = graph.Transactions.ToDictionary(pair => pair.Key, pair => pair.Value);
        transactions[Tx[1]] = transactions[Tx[1]] with { Transaction = tx with { Outputs = outputs } };
        GraphNode Coin(string key, int index) => Node(key, GraphNodeKind.Coin, [], hop: 1) with
        {
            Coin = new Outpoint(Tx[1], index),
        };
        return graph with
        {
            Nodes = [.. graph.Nodes, Coin("coin:empty", 2), Coin("coin:burn", 3)],
            Edges =
            [
                .. graph.Edges,
                Edge("v-empty", VKey, "coin:empty", 1, 2, 0m),
                Edge("v-burn", VKey, "coin:burn", 1, 3, 0.00001m),
            ],
            Transactions = transactions,
        };
    }

    [Fact]
    public void Many_actors_in_one_hop_are_spread_over_columns_side_by_side_without_overlap()
    {
        var nodes = Enumerable.Range(0, 200)
            .Select(index => Node("address:n" + index, GraphNodeKind.Address, [Address(300 + index)], hop: 1))
            .Prepend(Node(VKey, GraphNodeKind.Address, [V], hop: 0, start: true))
            .ToList();

        var (lanes, places) = HopLayout.Arrange(nodes);

        var hop = nodes.Skip(1).Select(node => places[node.Key]).ToList();
        Assert.True(hop.Select(place => place.X).Distinct().Count() > 1);
        Assert.Equal(hop.Count, hop.Distinct().Count());
        var width = hop.Max(place => place.X) - hop.Min(place => place.X);
        var height = hop.Max(place => place.Y) - hop.Min(place => place.Y);
        Assert.InRange(width / height, 0.5, 2);
        Assert.Equal(2, lanes.Count);
        Assert.True(lanes[1].Width > GraphSizes.NodeWidth);
    }

    [Fact]
    public void A_few_actors_in_a_hop_still_stand_in_one_column()
    {
        Assert.Equal(HopLayout.MostRows, HopLayout.RowsFor(HopLayout.MostRows));
        Assert.Equal(3, HopLayout.RowsFor(3));
        Assert.True(HopLayout.RowsFor(400) >= 20);
    }

    [Fact]
    public void The_ring_lays_hundreds_of_tiles_out_without_overlap_and_off_the_pile()
    {
        var cards = Scene(graph: Piled(300)).Pile(PileKey)!.Cards;
        var centre = new GraphPoint(500, 500);

        var places = PileFanLayout.PlaceRing(cards, centre);

        var frames = places.Values.Select(PileFanLayout.TileFrame).ToList();
        var pile = GraphSizes.NodeFrame(new GraphPoint(
            centre.X - (GraphSizes.NodeWidth / 2), centre.Y - (GraphSizes.NodeHeight / 2)));
        Assert.Equal(300, frames.Count);
        Assert.All(frames, frame => Assert.False(Overlap(frame, pile)));
        for (var first = 0; first < frames.Count; first++)
        {
            for (var second = first + 1; second < frames.Count; second++)
            {
                Assert.False(Overlap(frames[first], frames[second]), $"{first} and {second} overlap");
            }
        }
    }

    [Fact]
    public void The_first_ring_holds_twelve_evenly_from_the_top()
    {
        var ring = PileFanLayout.Ring(12);

        Assert.Equal(12, ring.Count);
        Assert.All(ring, point => Assert.InRange(
            Math.Pow(point.X / 360, 2) + Math.Pow(point.Y / 200, 2), 0.999, 1.001));
        Assert.True(ring[0].Y < 0 && ring[0].X > 0);
    }

    /// <summary>Says whether two frames overlap by more than a touch.</summary>
    private static bool Overlap(GraphRect first, GraphRect second) =>
        first.X < second.Right - 0.5 && second.X < first.Right - 0.5
        && first.Y < second.Bottom - 0.5 && second.Y < first.Bottom - 0.5;

    [Fact]
    public void A_tile_is_chosen_alone_or_with_shift_and_goes_with_the_others_chosen()
    {
        var fan = new PileFanViewModel(Scene(graph: Piled(4)).Pile(PileKey)!, new GraphPoint(0, 0));

        fan.Click(Member(1), add: false);
        fan.Click(Member(2), add: true);
        Assert.Equal([Member(1), Member(2)], fan.Selected.Order(StringComparer.Ordinal));
        Assert.Equal([Member(2), Member(1)], fan.Taking(Member(2)));
        Assert.Equal([Member(3)], fan.Taking(Member(3)));
        fan.Click(Member(3), add: false);
        Assert.Equal([Member(3)], fan.Selected);
        fan.Click(Member(3), add: false);
        Assert.Empty(fan.Selected);

        fan.Click(Member(0), add: false);
        IReadOnlyList<string>? picked = null;
        fan.PickWanted += (_, keys) => picked = keys;
        fan.PickSelected();
        Assert.Equal([Member(0)], picked);
        Assert.Equal("0,010 BTC · 1 tx", fan.Shown[0].Line);
    }

    /// <summary>The graph's view model with a case open, and the toasts it says.</summary>
    private sealed class Rig
    {
        public Rig(TraceGraph graph)
        {
            Source.Scene = () => Scene(graph: graph);
            var workflow = new CaseWorkflow(new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked),
                Session);
            Graph = new GraphViewModel(
                Session, workflow, Source, TestWays.For(new TestExitConnection()), new InlineUi(),
                new TestGraphEditor(), new TestClock());
            Graph.ToastWanted += (_, toast) => Toasts.Add(toast);
            Session.Open(new TestCase("2026-0001"));
        }

        public CaseSession Session { get; } = new();

        public TestGraphSource Source { get; } = new();

        public GraphViewModel Graph { get; }

        public List<ToastRequest> Toasts { get; } = [];
    }

    [Fact]
    public async Task A_click_on_a_pile_fans_it_out_and_the_chosen_cards_are_picked_out_and_kept()
    {
        var rig = new Rig(Piled());

        rig.Graph.Choose(PileKey, null);

        Assert.Null(rig.Graph.SelectedNode);
        var fan = rig.Graph.Fan!;
        Assert.Equal(PileKey, fan.Pile.Key);
        fan.Click(Member(1), add: false);
        await rig.Graph.PickFromPileAsync(fan.Taking(Member(1)), new GraphPoint(900, 40));

        Assert.Equal([Member(1)], Assert.Single(rig.Source.Picked));
        Assert.Contains(rig.Source.Saved, saved => saved.Key == Member(1) && saved.Place == new GraphPoint(900, 40));
    }

    [Fact]
    public void Esc_closes_the_fan()
    {
        var rig = new Rig(Piled());
        rig.Graph.Choose(PileKey, null);

        rig.Graph.Fan!.Close();

        Assert.Null(rig.Graph.Fan);
    }

    [Fact]
    public void A_second_click_on_a_fanned_pile_folds_it()
    {
        var rig = new Rig(Piled());
        rig.Graph.Choose(PileKey, null);

        rig.Graph.Choose(PileKey, null);

        Assert.Null(rig.Graph.Fan);
    }

    [Fact]
    public void A_click_on_a_clue_without_an_address_history_brings_in_the_next_part()
    {
        var rig = new Rig(Piled());

        rig.Graph.Choose(ClueKey, null);

        Assert.Equal([ExpansionRequest.ForAddress(V, 100)], Assert.Single(rig.Source.Expansions));
        Assert.Null(rig.Graph.SelectedNode);
    }

    [Fact]
    public async Task An_expansion_that_brings_hidden_outputs_says_so_in_a_lingering_toast_that_can_show_them()
    {
        var rig = new Rig(GraphSamples.Graph());
        var after = Scene(graph: WithOpReturns());
        rig.Source.Expansion = new GraphExpansion(after, ExpansionOutcome.Expanded, 1, []);
        rig.Graph.Choose(VKey, null);

        await rig.Graph.ExpandAsync();

        var toast = Assert.Single(rig.Toasts, toast => toast.Lingers);
        Assert.Equal(GraphViewModel.HiddenUnspendableText(1), toast.Title);
        Assert.Equal(GraphViewModel.ShowUnspendableText, toast.ActionText);
        toast.Action!();
        Assert.True(rig.Graph.Scene!.ShowsUnspendable);
        Assert.NotNull(rig.Graph.Scene.Node("coin:empty"));
        Assert.True(rig.Source.SavedViews[^1].ShowUnspendable);
    }
}
