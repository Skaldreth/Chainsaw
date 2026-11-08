using System.Numerics;
using Chainsaw.App.Graph;
using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// The view Flöde (sketch FL1a) without WPF: the flow by the method, the parts' ends, the Sankey's layout and the view.
/// </summary>
public sealed class FlowViewTests
{
    /// <summary>The victim A, the other source B, the cluster M where they mix, and the exchange X.</summary>
    private const string AKey = "address:a", BKey = "address:b", MKey = "address:m", XKey = "address:x";

    /// <summary>The lines: A and B pay M, and M pays X.</summary>
    private const string AM = "a-m", BM = "b-m", MX = "m-x";

    /// <summary>The made-up graph with E an exchange, U sanctioned and W a victim.</summary>
    private static GraphScene Labelled() => Scene(new Dictionary<string, IReadOnlyList<Label>>
    {
        [EKey] = [Label(E, LabelKind.Exchange, "Börs A")],
        [UKey] = [Label(U, LabelKind.Sanctioned, "Växlare Z")],
        [WKey] = [Label(W, LabelKind.Victim, "Offer 1")],
    });

    /// <summary>
    /// A graph where the victim A pays 1 BTC to M first, then B pays 1 BTC to M, and M pays 1 BTC to the exchange X:
    /// FIFO sends A's money on, haircut half of each.
    /// </summary>
    /// <returns>The scene.</returns>
    private static GraphScene Mixed()
    {
        GraphNode[] nodes =
        [
            Node(AKey, GraphNodeKind.Address, [Address(21)], hop: 0, start: true),
            Node(BKey, GraphNodeKind.Address, [Address(22)], hop: 0),
            Node(MKey, GraphNodeKind.Address, [Address(23)], hop: 1),
            Node(XKey, GraphNodeKind.Address, [Address(24)], hop: 2),
        ];
        GraphEdge[] edges =
        [
            Edge(AM, AKey, MKey, 0, 0, 1m), Edge(BM, BKey, MKey, 1, 0, 1m), Edge(MX, MKey, XKey, 2, 0, 1m),
        ];
        var graph = new TraceGraph(
            [TraceStartPoint.ForAddress(Address(21))], nodes, edges,
            new UtxoGraph(
                new Dictionary<Txid, GraphTransaction>(), new Dictionary<Outpoint, CoinSpend>(),
                new Dictionary<Txid, IReadOnlyList<TrailEndHint>>()),
            [], [], []);
        var labels = new Dictionary<string, IReadOnlyList<Label>>
        {
            [XKey] = [Label(Address(24), LabelKind.Exchange, "Börs X")],
        };
        return Scene(labels, graph, new TraceResult(Fifo, [], Btc(0), [], [], [], []));
    }

    /// <summary>Sums the units of some parts.</summary>
    /// <param name="lots">The parts.</param>
    /// <returns>The sum.</returns>
    private static BigInteger Sum(IEnumerable<FlowLot> lots) => lots.Aggregate(BigInteger.Zero, (s, l) => s + l.Units);

    [Fact]
    public void The_view_is_built_and_has_its_shortcut()
    {
        var entry = ViewTable.For(AppView.Flow);

        Assert.True(entry.IsBuilt);
        Assert.Equal("Ctrl+3", entry.Gesture);
    }

    [Fact]
    public void The_victims_money_is_followed_to_where_it_ended()
    {
        var flow = FlowReading.Read(Labelled(), Asset.Bitcoin, TracingMethodKind.Fifo);

        Assert.Equal(Btc(1.0m).Units, flow.VictimUnits);
        var exchange = Assert.Single(flow.Lots, lot => lot.End == FlowEnd.CashOut);
        Assert.Equal([WKey, VKey, KKey, EKey], exchange.Path);
        Assert.Equal(Btc(0.48m).Units, exchange.Units);
        Assert.Equal(Btc(0.39m).Units, Assert.Single(flow.Lots, lot => lot.End == FlowEnd.Risk).Units);
        Assert.Equal(Btc(0.13m).Units, Sum(flow.Lots.Where(lot => lot.End == FlowEnd.Kept)));
    }

    [Fact]
    public void Money_that_comes_back_to_an_actor_is_not_followed_round_in_a_ring()
    {
        var flow = FlowReading.Read(Labelled(), Asset.Bitcoin, TracingMethodKind.Fifo);

        Assert.All(flow.Lots, lot => Assert.Equal(lot.Path.Count, lot.Path.Distinct().Count()));
        Assert.Contains(flow.Lots, lot => lot.Last == KKey && lot.Units == Btc(0.12m).Units);
    }

    [Fact]
    public void Fifo_sends_the_first_money_on_and_haircut_a_share_of_each()
    {
        var fifo = FlowReading.Read(Mixed(), Asset.Bitcoin, TracingMethodKind.Fifo);
        var haircut = FlowReading.Read(Mixed(), Asset.Bitcoin, TracingMethodKind.Haircut);

        Assert.Equal(Btc(1m).Units, Sum(fifo.Lots.Where(lot => lot.IsVictim && lot.End == FlowEnd.CashOut)));
        Assert.Equal(Btc(1m).Units, Sum(fifo.Lots.Where(lot => !lot.IsVictim && lot.Last == MKey)));
        Assert.Equal(Btc(0.5m).Units, Sum(haircut.Lots.Where(lot => lot.IsVictim && lot.End == FlowEnd.CashOut)));
        Assert.Equal(Btc(0.5m).Units, Sum(haircut.Lots.Where(lot => !lot.IsVictim && lot.Last == XKey)));
        Assert.Equal(new TracingMethodId(TracingMethodKind.Haircut, 1), haircut.Method);
    }

    [Fact]
    public void Money_that_never_meets_the_victims_is_left_out()
    {
        var flow = FlowReading.Read(Scene(), Asset.Bitcoin, TracingMethodKind.Fifo);

        Assert.All(flow.Lots, lot => Assert.True(lot.IsVictim));
        Assert.All(flow.Lots, lot => Assert.Equal(VKey, lot.Source));
    }

    [Fact]
    public void The_sankey_has_a_column_per_hop_and_strands_stacked_inside_the_bars()
    {
        var flow = FlowReading.Read(Labelled(), Asset.Bitcoin, TracingMethodKind.Fifo);
        var actors = ExposureMap.Actors(Labelled()).Values.ToDictionary(
            node => node.Key, node => new FlowLayout.Actor(node.Name, node.Look, false));

        var picture = FlowLayout.Lay(flow, actors, 900, 600, lots => Sum(lots).ToString());

        Assert.Equal(["Offren", "1 hopp", "2 hopp", "3 hopp"], picture.Columns.Select(column => column.Title));
        foreach (var strand in picture.Strands)
        {
            var from = picture.Bars.Single(bar => bar.Key == strand.StepKey.Split('>')[0]);
            Assert.InRange(strand.Y0, from.Y - 0.01, from.Y + from.Height + 0.01);
        }

        Assert.Contains(picture.Stubs, stub => stub.Key == KKey);
        Assert.True(picture.Bars.Single(bar => bar.Key == EKey).IsTerminal);
        Assert.Equal(KKey, picture.At(picture.Bars.Single(bar => bar.Key == KKey).X + 4,
            picture.Bars.Single(bar => bar.Key == KKey).Y + 2)!.Key);
    }

    [Fact]
    public async Task The_view_counts_only_while_shown_and_chooses_the_largest_cash_out()
    {
        var model = new FlowViewModel(new InlineUi());
        model.Update(Labelled());
        await model.Computed;
        Assert.False(model.HasFlow);

        model.IsShown = true;
        await model.Computed;

        Assert.True(model.HasFlow);
        Assert.Equal(EKey, model.SelectedKey);
        Assert.Equal("Börs A", model.Detail!.Name);
        Assert.Equal("48,0 %", model.Detail.Percent);
        Assert.Contains(model.Ends, row => row.End == FlowEnd.CashOut && row.Percent == "48,0 %");
        Assert.Contains(model.Detail.Basis, chip => chip.Text == FlowResult.Analysis);
    }

    [Fact]
    public async Task The_method_counts_the_split_again()
    {
        var model = new FlowViewModel(new InlineUi()) { IsShown = true };
        model.Update(Mixed());
        await model.Computed;
        Assert.Equal("100,0 %", model.Ends.Single(row => row.End == FlowEnd.CashOut).Percent);

        model.SetMethodCommand.Execute(nameof(TracingMethodKind.Haircut));
        await model.Computed;

        Assert.Equal("50,0 %", model.Ends.Single(row => row.End == FlowEnd.CashOut).Percent);
        Assert.Contains(model.Methods, choice => choice is { Text: "Haircut", IsSelected: true });
    }

    [Fact]
    public async Task A_band_lights_the_parts_on_it_and_shows_the_way_in_the_graph()
    {
        var model = new FlowViewModel(new InlineUi()) { IsShown = true };
        model.Update(Labelled());
        await model.Computed;
        ExposurePath? asked = null;
        model.PathWanted += (_, path) => asked = path;

        model.PickCommand.Execute(FlowResult.StepKey(VKey, UKey));
        model.ShowPathCommand.Execute(null);

        Assert.All(model.Lit, lot => Assert.True(lot.Passes(VKey, UKey)));
        Assert.Equal([WKey, VKey, UKey], asked!.Nodes);
        Assert.Equal([WV, VU], asked.Edges);
    }

    [Fact]
    public async Task Why_says_the_flow_is_a_judgement_by_the_method()
    {
        var model = new FlowViewModel(new InlineUi()) { IsShown = true };
        model.Update(Labelled());
        await model.Computed;
        Guide.GuideRequest? asked = null;
        model.GuideWanted += (_, request) => asked = request;

        model.AskWhyCommand.Execute(null);

        Assert.Equal(GuideTopic.Flow, asked!.Topic);
        Assert.Contains("bedömning", asked.Context!.Lines[0].Text, StringComparison.Ordinal);
        Assert.Contains(FlowResult.Analysis, asked.Context.Lines[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_victim_the_view_says_so_honestly()
    {
        var model = new FlowViewModel(new InlineUi()) { IsShown = true };
        var plain = GraphSamples.Graph();
        var graph = plain with { StartPoints = [], Nodes = [.. plain.Nodes.Select(n => n with { IsStartPoint = false })] };
        model.Update(Scene(graph: graph));
        await model.Computed;

        Assert.False(model.HasFlow);
        Assert.False(model.HasDetail);
        Assert.False(model.HasUnits);
    }
}
