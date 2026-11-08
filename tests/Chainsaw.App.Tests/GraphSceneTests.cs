using System.Text.RegularExpressions;
using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>
/// The graph's meaning without WPF: the sorts, marks and lines of the visual language (GR3a and GR3c), the seeds,
/// the inspector with <em>Varför?</em>, and the cluster's explanation.
/// </summary>
public sealed partial class GraphSceneTests
{
    /// <summary>Compares texts exactly.</summary>
    private const StringComparison Ordinal = StringComparison.Ordinal;

    /// <summary>The exchange's label on E.</summary>
    private static readonly Label ExchangeA = GraphSamples.Label(GraphSamples.E, LabelKind.Exchange, "Börs A");

    /// <summary>Gives the labels for one node.</summary>
    /// <param name="key">The node.</param>
    /// <param name="labels">Its labels.</param>
    /// <returns>The labels by node.</returns>
    private static Dictionary<string, IReadOnlyList<Label>> On(string key, params Label[] labels) =>
        new() { [key] = labels };

    /// <summary>
    /// Without labels: the start keeps its own sort with the flag (sketch GR5), a cluster is a cluster and an address
    /// unknown and faint.
    /// </summary>
    [Fact]
    public void UnlabelledActorsHaveTheGraphsOwnSorts()
    {
        var scene = GraphSamples.Scene();

        var start = scene.Node(GraphSamples.VKey)!;
        Assert.Equal(NodeSort.Unknown, start.Look.Sort);
        Assert.True(start.IsStartPoint);
        Assert.Empty(start.Badges);
        Assert.Contains("Startpunkt", start.ToolTip, StringComparison.Ordinal);
        Assert.False(scene.Node(GraphSamples.UKey)!.IsStartPoint);
        Assert.Equal(NodeSort.Cluster, scene.Node(GraphSamples.KKey)!.Look.Sort);
        Assert.Equal("2", scene.Node(GraphSamples.KKey)!.Count);
        var unknown = scene.Node(GraphSamples.UKey)!;
        Assert.Equal(NodeSort.Unknown, unknown.Look.Sort);
        Assert.Equal(NodeShape.Round, unknown.Look.Shape);
        Assert.True(unknown.IsDim);
        Assert.Contains("Inte expanderad", unknown.ToolTip, StringComparison.Ordinal);
        Assert.Equal(GraphScene.Shorten(GraphSamples.U.Value), unknown.Name);
    }

    /// <summary>An exchange that traced money reaches is blue, square, named by its label and marked as found.</summary>
    [Fact]
    public void ExchangeWithTracedMoneyIsACashOutFound()
    {
        var scene = GraphSamples.Scene(On(GraphSamples.EKey, ExchangeA));
        var exchange = scene.Node(GraphSamples.EKey)!;

        Assert.Equal(NodeSort.Exchange, exchange.Look.Sort);
        Assert.Equal(GraphInk.CashOut, exchange.Look.Ink);
        Assert.Equal(NodeShape.Square, exchange.Look.Shape);
        Assert.Equal("Börs A", exchange.Name);
        Assert.Equal([NodeBadge.CashOutFound], exchange.Badges);
        Assert.Equal(GraphSamples.Btc(0.5m), exchange.TracedIn);
        Assert.Equal(GraphSamples.Btc(0.5m), exchange.In);
        Assert.Equal(GraphSamples.Btc(0.02m), exchange.Out);
    }

    /// <summary>An exchange no traced money reaches is not marked as found.</summary>
    [Fact]
    public void ExchangeWithoutTracedMoneyIsNotFound()
    {
        var trace = GraphSamples.Trace() with { Edges = [] };
        var scene = GraphSamples.Scene(
            On(GraphSamples.EKey, GraphSamples.Label(GraphSamples.E, LabelKind.Exchange)), trace: trace);

        Assert.Empty(scene.Node(GraphSamples.EKey)!.Badges);
    }

    /// <summary>A sanction is the sort alone, else a mark; another case's label is marked too.</summary>
    [Fact]
    public void SanctionsAndOtherCasesAreMarks()
    {
        var alone = GraphSamples.Scene(On(GraphSamples.UKey, GraphSamples.Label(GraphSamples.U, LabelKind.Sanctioned)));
        var both = GraphSamples.Scene(On(
            GraphSamples.UKey,
            GraphSamples.Label(GraphSamples.U, LabelKind.Sanctioned),
            GraphSamples.Label(GraphSamples.U, LabelKind.Mixer, "Mixer X", LabelSource.OtherCase)));

        Assert.Equal(NodeSort.Sanctioned, alone.Node(GraphSamples.UKey)!.Look.Sort);
        var mixer = both.Node(GraphSamples.UKey)!;
        Assert.Equal(NodeSort.Mixer, mixer.Look.Sort);
        Assert.True(mixer.Look.IsDashed);
        Assert.Equal([NodeBadge.FromOtherCase, NodeBadge.Sanctioned], mixer.Badges);
        Assert.Contains("Sanktionerad", mixer.ToolTip, StringComparison.Ordinal);
    }

    /// <summary>
    /// A former sanction is a toned-down mark beside the mixer, said in the inspector and the legend; a sanction in
    /// force outweighs it.
    /// </summary>
    [Fact]
    public void AFormerSanctionIsAToneDownMarkSaidInTheInspectorAndTheLegend()
    {
        var former = GraphSamples.Label(
            GraphSamples.U, LabelKind.FormerlySanctioned, "OFAC SDN 2022-08-08 – 2025-03-21");
        var mixer = GraphSamples.Label(GraphSamples.U, LabelKind.Mixer, "Blandaren");
        var scene = GraphSamples.Scene(On(GraphSamples.UKey, mixer, former));
        var both = GraphSamples.Scene(On(
            GraphSamples.UKey, mixer, former, GraphSamples.Label(GraphSamples.U, LabelKind.Sanctioned)));

        var node = scene.Node(GraphSamples.UKey)!;
        Assert.Equal(NodeSort.Mixer, node.Look.Sort);
        Assert.Equal([NodeBadge.FormerlySanctioned], node.Badges);
        Assert.Contains("Tidigare sanktionerad", node.ToolTip, StringComparison.Ordinal);
        var fact = Assert.Single(
            InspectorContent.ForNode(scene, node).Facts, each => each.Name == InspectorContent.FormerSanctionFact);
        Assert.Contains("2022-08-08 – 2025-03-21", fact.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(NodeBadge.FormerlySanctioned, both.Node(GraphSamples.UKey)!.Badges);
        Assert.Contains(Legend.Badges, mark => mark.Look.Badge == NodeBadge.FormerlySanctioned);
        Assert.Equal("Brush.Mark.FormerlySanctioned", GraphInkKeys.Of(GraphInk.FormerlySanctioned));
    }

    /// <summary>
    /// A label on the start point says what it is; the start is never the victim by itself, only by the victim's
    /// label (sketch GR5), and a start that is a cluster stays a cluster.
    /// </summary>
    [Fact]
    public void StartPointKeepsItsLabel()
    {
        var exchange = GraphSamples.Scene(On(GraphSamples.VKey, GraphSamples.Label(GraphSamples.V, LabelKind.Exchange)));
        var unknown = GraphSamples.Scene(On(GraphSamples.VKey, GraphSamples.Label(GraphSamples.V, LabelKind.Unknown)));
        var victim = GraphSamples.Scene(On(GraphSamples.VKey, GraphSamples.Label(GraphSamples.V, LabelKind.Victim)));
        var graph = GraphSamples.Graph();
        var clusterStart = graph with
        {
            Nodes =
            [
                .. graph.Nodes.Select(node => node.Key == GraphSamples.KKey ? node with { IsStartPoint = true } : node),
            ],
        };

        Assert.Equal(NodeSort.Exchange, exchange.Node(GraphSamples.VKey)!.Look.Sort);
        Assert.Equal(NodeSort.Unknown, unknown.Node(GraphSamples.VKey)!.Look.Sort);
        Assert.Equal(NodeSort.Victim, victim.Node(GraphSamples.VKey)!.Look.Sort);
        Assert.True(victim.Node(GraphSamples.VKey)!.IsStartPoint);
        var cluster = GraphSamples.Scene(graph: clusterStart).Node(GraphSamples.KKey)!;
        Assert.Equal(NodeSort.Cluster, cluster.Look.Sort);
        Assert.True(cluster.IsStartPoint);
    }

    /// <summary>
    /// A node without a name is titled by its address in full, to be shortened only where it does not fit; a named
    /// node has the name with the address under it (CLAUDE.md, <em>Nodernas namn</em>).
    /// </summary>
    [Fact]
    public void NamedNodesShowTheAddressUnderTheName()
    {
        var scene = GraphSamples.Scene(On(GraphSamples.EKey, ExchangeA));
        var named = scene.Node(GraphSamples.EKey)!;
        var unnamed = scene.Node(GraphSamples.UKey)!;
        var ownName = GraphSamples.Label(GraphSamples.U, LabelKind.Unknown, "Säljaren");
        var own = GraphSamples.Scene(On(GraphSamples.UKey, ownName)).Node(GraphSamples.UKey)!;

        Assert.True(named.IsNamed);
        Assert.Equal("Börs A", named.Name);
        Assert.Equal(GraphSamples.E.Value, named.Subject);
        Assert.False(unnamed.IsNamed);
        Assert.Equal(GraphSamples.U.Value, unnamed.Subject);
        Assert.True(own.IsNamed);
        Assert.Equal("Säljaren", own.Name);
        Assert.Equal(NodeSort.Unknown, own.Look.Sort);
    }

    /// <summary>An address is shortened only as much as the room asks, keeping its start and its last four.</summary>
    [Fact]
    public void SubjectIsShortenedOnlyWhenItDoesNotFit()
    {
        const string address = "bc1qdlm5aahn0t09nl4ujy8624fl6zy54klay937yr";

        Assert.Equal(address, SubjectText.Fit(address, 42));
        Assert.Equal(address, SubjectText.Fit(address, 100));
        Assert.Equal("bc1qdlm…37yr", SubjectText.Fit(address, 12));
        Assert.Equal("bc1q…37yr", SubjectText.Fit(address, 3));
        Assert.Equal("short", SubjectText.Fit("short", 2));
        Assert.Equal(22, SubjectText.Fit(address, 22).Length);
    }

    /// <summary>The legend has the start point's flag first among the marks.</summary>
    [Fact]
    public void LegendHasTheStartFlag()
    {
        var flag = VisualLanguage.Badges[0];

        Assert.Equal(NodeBadge.StartPoint, flag.Badge);
        Assert.Equal("Startpunkt", flag.Name);
        Assert.Equal("Icon.Flag", flag.Icon);
        Assert.Equal(GraphInk.StartMark, flag.Ink);
    }

    /// <summary>A shared tx is a CoinJoin when the trail ends there as one; another chain has its sort.</summary>
    [Fact]
    public void TransactionsCoinsAndOtherChainsHaveSorts()
    {
        var hint = new TrailEndHint(TrailEndReason.CoinJoin, "coinjoin v1", "Fem lika utgångar.");
        var coinJoin = GraphSamples.Node("tx:x", GraphNodeKind.Transaction, [], hop: 2, ends: [hint]);
        var shared = GraphSamples.Node("tx:y", GraphNodeKind.Transaction, [], hop: 2);
        var other = GraphSamples.Node("chain:z", GraphNodeKind.OtherChain, [], hop: 3) with
        {
            OtherChain = new ChainTransactionRef(Chain.Tron, new string('a', 64)),
        };
        var graph = GraphSamples.Graph() with { Nodes = [coinJoin, shared, other], Edges = [] };
        var scene = GraphSamples.Scene(graph: graph);

        Assert.Equal(NodeSort.CoinJoin, scene.Node("tx:x")!.Look.Sort);
        Assert.Equal(NodeShape.Round, scene.Node("tx:x")!.Look.Shape);
        Assert.Equal(NodeSort.SharedTransaction, scene.Node("tx:y")!.Look.Sort);
        Assert.Equal(NodeSort.OtherChainTransaction, scene.Node("chain:z")!.Look.Sort);
        Assert.Equal("TRON", scene.Node("chain:z")!.ChainName);
    }

    /// <summary>
    /// Traced lines are the accent and as thick as their amount; untraced lines are grey and thin; a partly traced
    /// line says how much of how much.
    /// </summary>
    [Fact]
    public void LinesCarryColourThicknessAndAmount()
    {
        var trace = GraphSamples.Trace() with
        {
            Edges = [GraphSamples.Traced(GraphSamples.VK, 0.6m), GraphSamples.Traced(GraphSamples.KE, 0.3m)],
        };
        var scene = GraphSamples.Scene(trace: trace);

        var vk = scene.Edge(GraphSamples.VK)!;
        var ke = scene.Edge(GraphSamples.KE)!;
        var ek = scene.Edge(GraphSamples.EK)!;
        Assert.Equal(GraphInk.Accent, vk.Ink);
        Assert.Equal(EdgeWidth.ThinnestTraced + EdgeWidth.TracedSpan, vk.Width);
        Assert.Equal("0,6000", vk.Label);
        Assert.Equal("0,3000 av 0,5000", ke.Label);
        Assert.Equal(GraphInk.Untraced, ek.Ink);
        Assert.Equal(EdgeWidth.Untraced, ek.Width);
        Assert.False(ek.IsTraced);
        Assert.Equal(EdgeDash.None, vk.Dash);
    }

    /// <summary>Dashed always means uncertain: an unconfirmed transfer or a traced amount that may be outdated.</summary>
    [Fact]
    public void UncertainLinesAreDashed()
    {
        var graph = GraphSamples.Graph() with
        {
            Edges =
            [
                GraphSamples.Edge(GraphSamples.VK, GraphSamples.VKey, GraphSamples.KKey, 1, 0, 0.6m, confirmed: false),
                GraphSamples.Edge(GraphSamples.KE, GraphSamples.KKey, GraphSamples.EKey, 2, 0, 0.5m),
            ],
        };
        var trace = GraphSamples.Trace() with { Edges = [GraphSamples.Traced(GraphSamples.KE, 0.5m, outdated: true)] };
        var scene = GraphSamples.Scene(graph: graph, trace: trace);

        Assert.Equal(EdgeDash.Uncertain, scene.Edge(GraphSamples.VK)!.Dash);
        Assert.Equal(EdgeDash.Uncertain, scene.Edge(GraphSamples.KE)!.Dash);
    }

    /// <summary>A line to another chain is purple with long dashes.</summary>
    [Fact]
    public void LineToAnotherChainIsPurpleAndDashed()
    {
        var edge = GraphSamples.Edge("chain", GraphSamples.KKey, GraphSamples.EKey, 2, 0, 0.5m) with
        {
            Kind = GraphEdgeKind.ContinuesOnOtherChain,
        };
        var scene = GraphSamples.Scene(graph: GraphSamples.Graph() with { Edges = [edge] });

        Assert.Equal(GraphInk.OtherChain, scene.Edge("chain")!.Ink);
        Assert.Equal(EdgeDash.OtherChain, scene.Edge("chain")!.Dash);
    }

    /// <summary>Choosing a node brings its lines and neighbours near; choosing a line brings its two ends.</summary>
    [Fact]
    public void NearFollowsWhatIsChosen()
    {
        var scene = GraphSamples.Scene();

        var (nodes, edges) = scene.Near(GraphSamples.KKey, null);
        Assert.Equal(
            new[] { GraphSamples.VKey, GraphSamples.KKey, GraphSamples.EKey }.Order(),
            nodes.Order());
        Assert.Equal(new[] { GraphSamples.VK, GraphSamples.KE, GraphSamples.EK }.Order(), edges.Order());

        var (lineNodes, lineEdges) = scene.Near(null, GraphSamples.VU);
        Assert.Equal(new[] { GraphSamples.UKey, GraphSamples.VKey }.Order(), lineNodes.Order());
        Assert.Equal([GraphSamples.VU], lineEdges);
        Assert.Empty(scene.Near(null, null).Nodes);
    }

    /// <summary>Long names are shortened to their ends; short ones are left.</summary>
    [Fact]
    public void NamesAreShortenedToTheirEnds()
    {
        Assert.Equal("bc1q…x0wl", GraphScene.Shorten("bc1qabcdefghijklmnopx0wl"));
        Assert.Equal("kort", GraphScene.Shorten("kort"));
        Assert.Equal("FIFO", GraphScene.MethodName(TracingMethodKind.Fifo));
        Assert.Equal("haircut", GraphScene.MethodName(TracingMethodKind.Haircut));
    }

    /// <summary>Every sort and mark has one look, and every label's kind draws as a sort.</summary>
    [Fact]
    public void VisualLanguageIsComplete()
    {
        foreach (var sort in Enum.GetValues<NodeSort>())
        {
            Assert.Single(VisualLanguage.Sorts, look => look.Sort == sort);
        }

        foreach (var badge in Enum.GetValues<NodeBadge>())
        {
            Assert.Single(VisualLanguage.Badges, look => look.Badge == badge);
        }

        Assert.Equal(NodeSort.Gambling, VisualLanguage.SortOf(LabelKind.Gambling));
        Assert.Equal(NodeSort.Unknown, VisualLanguage.SortOf(LabelKind.Unknown));
        Assert.All(
            // A supplier's report and a former sanction are marks beside the node's sort, not sorts of their own.
            Enum.GetValues<LabelKind>().Where(kind =>
                kind is not (LabelKind.Unknown or LabelKind.Reported or LabelKind.FormerlySanctioned)),
            kind => Assert.NotEqual(NodeSort.Unknown, VisualLanguage.SortOf(kind)));
        Assert.True(VisualLanguage.IsCashOut(NodeSort.Atm));
        Assert.False(VisualLanguage.IsCashOut(NodeSort.Gambling));
    }

    /// <summary>Every icon the visual language names is drawn in <c>Themes/Icons.xaml</c>.</summary>
    [Fact]
    public void EveryIconExists()
    {
        var icons = File.ReadAllText(Path.Combine(Root(), "src", "Chainsaw.App", "Themes", "Icons.xaml"));
        var keys = IconKey().Matches(icons).Select(match => match.Groups[1].Value).ToHashSet();

        Assert.All(VisualLanguage.Sorts, look => Assert.Contains(look.Icon, keys));
        Assert.All(VisualLanguage.Badges, look => Assert.Contains(look.Icon, keys));
    }

    /// <summary>Every section the graph links to lies in <c>docs/guide</c>.</summary>
    [Fact]
    public void EveryGuideTopicHasItsText()
    {
        foreach (var topic in Enum.GetValues<GuideTopic>())
        {
            Assert.True(File.Exists(Path.Combine(Root(), "docs", "guide", topic.FileName() + ".md")), topic.ToString());
            Assert.NotEmpty(topic.Title());
        }

        Assert.Equal(GuideTopic.ChangeAddress, GuideTopics.ForHeuristic(HeuristicNames.ChangeAddress));
        Assert.Null(GuideTopics.ForHeuristic("okand"));
    }

    /// <summary>An address start traces every coin the graph shows paid to it.</summary>
    [Fact]
    public void AddressStartTracesItsReceivedCoins() =>
        Assert.Equal([new TraceSeed(new Outpoint(GraphSamples.Tx[0], 0))], SeedPicker.Pick(GraphSamples.Graph()));

    /// <summary>A tx start traces what it pays to others than its payers; if only back to them, all of it.</summary>
    [Fact]
    public void TransactionStartTracesItsPaymentsToOthers()
    {
        var graph = GraphSamples.Graph();
        var payment = graph with { StartPoints = [TraceStartPoint.ForTransaction(GraphSamples.Tx[2])] };
        var selfPay = GraphSamples.Transaction(7, [(GraphSamples.U, 1m)], [(GraphSamples.U, 0.5m), (GraphSamples.U, 0.4m)]);
        var transactions = graph.Transactions.ToDictionary(pair => pair.Key, pair => pair.Value);
        transactions[selfPay.Txid] = new GraphTransaction(selfPay, LookupStatus.FromCase, null);
        var back = graph with
        {
            StartPoints = [TraceStartPoint.ForTransaction(selfPay.Txid)],
            Transactions = transactions,
        };

        Assert.Equal(
            [new TraceSeed(new Outpoint(GraphSamples.Tx[2], 0)), new TraceSeed(new Outpoint(GraphSamples.Tx[2], 1))],
            SeedPicker.Pick(payment));
        Assert.Equal(2, SeedPicker.Pick(back).Count);
        var missing = graph with { StartPoints = [TraceStartPoint.ForTransaction(GraphSamples.TxidOf(50))] };
        Assert.Empty(SeedPicker.Pick(missing));
    }

    /// <summary>A seed downstream of another is left out, so that nothing is counted twice.</summary>
    [Fact]
    public void SeedDownstreamOfAnotherIsLeftOut()
    {
        var graph = GraphSamples.Graph() with
        {
            StartPoints = [TraceStartPoint.ForAddress(GraphSamples.V), TraceStartPoint.ForAddress(GraphSamples.K1)],
        };

        Assert.Equal([new TraceSeed(new Outpoint(GraphSamples.Tx[0], 0))], SeedPicker.Pick(graph));
    }

    /// <summary>The inspector for a node gives the facts, the full address and Varför? with sections.</summary>
    [Fact]
    public void InspectorExplainsANode()
    {
        var scene = GraphSamples.Scene(On(GraphSamples.EKey, ExchangeA));
        var content = InspectorContent.ForNode(scene, scene.Node(GraphSamples.EKey)!);

        Assert.Equal("Börs A", content.Title);
        Assert.Contains(new InspectorFact("Spårat in", "0,5000 BTC"), content.Facts);
        Assert.Equal("Börs · Bitcoin", content.Subtitle);
        Assert.DoesNotContain(content.Facts, fact => fact.Name == "Sort");
        Assert.Contains(new InspectorFact("Hopp", "+2"), content.Facts);
        Assert.Equal([GraphSamples.E.Value], content.Details);
        Assert.Contains(content.Why, line => line.Topic == GuideTopic.Labels && line.Text.Contains("Bedömning", Ordinal));
        Assert.Contains(content.Why, line => line.Topic == GuideTopic.Methods && line.Text.Contains("FIFO v1", Ordinal));
        Assert.Equal([ExpansionRequest.ForAddress(GraphSamples.E)], content.Expansion);
    }

    /// <summary>The start says why it is the start, and a node not expanded says so.</summary>
    [Fact]
    public void InspectorExplainsTheStartAndWhatIsNotExpanded()
    {
        var scene = GraphSamples.Scene(graph: GraphSamples.Graph(NodeExpansion.NotExpanded));
        var start = InspectorContent.ForNode(scene, scene.Node(GraphSamples.VKey)!);
        var cluster = InspectorContent.ForNode(scene, scene.Node(GraphSamples.KKey)!);

        Assert.Contains(start.Why, line => line.Topic == GuideTopic.StartPoints);
        Assert.Contains(start.Why, line => line.Text.StartsWith("Inte expanderad", StringComparison.Ordinal));
        Assert.Contains(start.Why, line => line.Text.Contains("Ingen etikett", StringComparison.Ordinal));
        Assert.Equal(2, cluster.Expansion.Count);
        Assert.StartsWith("Okänd · ", start.Subtitle, StringComparison.Ordinal);
        Assert.True(start.IsStartPoint);
        Assert.False(cluster.IsStartPoint);
    }

    /// <summary>
    /// A coin that can never be spent (an OP_RETURN, as in every coinbase) offers no expansion, which could only say
    /// <em>Inget nytt att hämta</em>; a coin that may still be spent can be expanded.
    /// </summary>
    [Fact]
    public void InspectorOffersNoExpansionForACoinThatCanNeverBeSpent()
    {
        var graph = GraphSamples.Graph();
        var burnt = new Outpoint(GraphSamples.Tx[3], 1);
        var open = new Outpoint(GraphSamples.Tx[2], 2);
        graph = graph with
        {
            Nodes = [.. graph.Nodes, Coin("coin:burnt", burnt, NodeExpansion.Expanded), Coin("coin:open", open)],
        };
        var scene = GraphSamples.Scene(graph: graph);

        var unspendable = InspectorContent.ForNode(scene, scene.Node("coin:burnt")!);
        var spendable = InspectorContent.ForNode(scene, scene.Node("coin:open")!);

        Assert.False(unspendable.CanExpand);
        Assert.Equal([ExpansionRequest.ForTransaction(open.Txid)], spendable.Expansion);
    }

    /// <summary>A coin node without an address.</summary>
    /// <param name="key">Its key.</param>
    /// <param name="coin">The coin.</param>
    /// <param name="expansion">How far it is expanded.</param>
    /// <returns>The node.</returns>
    private static GraphNode Coin(string key, Outpoint coin, NodeExpansion expansion = NodeExpansion.NotExpanded) =>
        GraphSamples.Node(key, GraphNodeKind.Coin, [], hop: 3, expansion: expansion) with { Coin = coin };

    /// <summary>The trail's end at a node is said with its reason, its source and its section.</summary>
    [Fact]
    public void InspectorSaysWhyTheTrailEnds()
    {
        var hint = new TrailEndHint(TrailEndReason.Mixer, "etikett: Mixer X", "Känd mixer.");
        var graph = GraphSamples.Graph() with
        {
            Nodes =
            [
                .. GraphSamples.Graph().Nodes.Select(
                    node => node.Key == GraphSamples.UKey ? node with { Ends = [hint] } : node),
            ],
        };
        var end = new TraceEnd(
            TrailEndReason.Unspent, new TracedAmount(GraphSamples.Btc(0.39m), GraphSamples.Fifo, []), null, null,
            GraphSamples.UKey, "Myntet låg kvar.", [], [], MayBeOutdated: true);
        var scene = GraphSamples.Scene(graph: graph, trace: GraphSamples.Trace() with { Ends = [end] });

        var why = InspectorContent.ForNode(scene, scene.Node(GraphSamples.UKey)!).Why;

        Assert.Contains(
            why, line => line.Text == "Mixer: Känd mixer. (etikett: Mixer X)" && line.Topic == GuideTopic.TrailEnds);
        Assert.Contains(why, line => line.Text.StartsWith("Ligger kvar", StringComparison.Ordinal)
            && line.Text.Contains("0,3900 BTC", StringComparison.Ordinal)
            && line.Text.Contains("kan ha ändrats", StringComparison.Ordinal));
    }

    /// <summary>The inspector for a line gives the facts, every txid with its block, and why its amount.</summary>
    [Fact]
    public void InspectorExplainsALine()
    {
        var scene = GraphSamples.Scene();
        var traced = InspectorContent.ForEdge(scene, scene.Edge(GraphSamples.VK)!);
        var back = InspectorContent.ForEdge(scene, scene.Edge(GraphSamples.EK)!);

        Assert.Contains(new InspectorFact("Belopp", "0,6000 BTC"), traced.Facts);
        Assert.Contains(new InspectorFact("Tx", "1"), traced.Facts);
        Assert.Contains(GraphSamples.Tx[1].Value, traced.Details);
        Assert.Contains(traced.Details, line => line.StartsWith("Block 101 · ", StringComparison.Ordinal));
        Assert.Contains(traced.Why, line => line.Text.StartsWith("Spårat belopp", StringComparison.Ordinal));
        Assert.Contains(traced.Why, line => line.Topic == GuideTopic.Utxo);
        Assert.False(traced.CanExpand);
        Assert.Contains(back.Why, line => line.Text.StartsWith("Inga av de spårade", StringComparison.Ordinal));
        Assert.Contains(back.Why, line => line.Text.Contains("båge", StringComparison.Ordinal));
        Assert.Contains("UTC", InspectorContent.Time(DateTimeOffset.UnixEpoch), StringComparison.Ordinal);
    }

    /// <summary>A cluster explains itself as a conclusion, with each link's heuristic, version, tx and reason.</summary>
    [Fact]
    public void ClusterExplainsEachLink()
    {
        var heuristic = new HeuristicId(HeuristicNames.CommonInputOwnership, 1);
        var evidence = new ClusterEvidence(
            1, heuristic, GraphSamples.Tx[2], [GraphSamples.K1, GraphSamples.K2], EvidenceStrength.Merge,
            "Inputs i samma tx.", null, null, 1, DateTimeOffset.UnixEpoch);
        var link = new ClusterLink(evidence, LinkState.Merged);
        var cluster = new Cluster(
            GraphSamples.K1,
            [new ClusterMember(GraphSamples.K1, null, null), new ClusterMember(GraphSamples.K2, link, GraphSamples.K1)],
            [link, link],
            [],
            [],
            []);

        var why = GraphSource.Explain(cluster);

        Assert.StartsWith("Klustret är klustringens slutsats", why[0].Text, StringComparison.Ordinal);
        Assert.Contains("2 adresser", why[0].Text, StringComparison.Ordinal);
        Assert.Equal(2, why.Count);
        Assert.Contains("common-input-ownership v1", why[1].Text, StringComparison.Ordinal);
        Assert.Contains(GraphScene.Shorten(GraphSamples.Tx[2].Value), why[1].Text, StringComparison.Ordinal);
        Assert.Equal(GuideTopic.CommonInputOwnership, why[1].Topic);
    }

    /// <summary>A cluster that a merge of the user's holds says so: the user's judgement, not a fact.</summary>
    [Fact]
    public void ClusterSaysWhenTheUsersJudgementHoldsIt()
    {
        var user = new ClusterEvidence(
            1, UserMerges.Id, GraphSamples.Tx[2], [GraphSamples.K1, GraphSamples.K2], EvidenceStrength.Merge,
            "Användaren godkände förslaget.", null, null, 1, DateTimeOffset.UnixEpoch);
        var heuristic = user with { Heuristic = new HeuristicId(HeuristicNames.CommonInputOwnership, 1) };
        Cluster Of(params ClusterEvidence[] links) => new(
            GraphSamples.K1,
            [new ClusterMember(GraphSamples.K1, null, null)],
            [.. links.Select(evidence => new ClusterLink(evidence, LinkState.Merged))],
            [],
            [],
            []);

        Assert.EndsWith(
            "enligt användarens bedömning samma ägare.",
            GraphSource.Explain(Of(user))[0].Text,
            StringComparison.Ordinal);
        Assert.EndsWith(
            "enligt heuristikerna och användarens bedömning samma ägare.",
            GraphSource.Explain(Of(heuristic, user))[0].Text,
            StringComparison.Ordinal);
        Assert.Equal(3, GraphSource.Explain(Of(heuristic, user)).Count);
        Assert.EndsWith(
            "enligt heuristikerna samma ägare.", GraphSource.Explain(Of(heuristic))[0].Text, StringComparison.Ordinal);
    }

    /// <summary>A whole expansion is expanded only when every lookup was, and not expanded only when none was.</summary>
    [Fact]
    public void ExpansionOutcomesCombine()
    {
        Assert.Equal(ExpansionOutcome.NotExpanded, GraphSource.Combine([]));
        const ExpansionOutcome done = ExpansionOutcome.Expanded, none = ExpansionOutcome.NotExpanded;
        Assert.Equal(done, GraphSource.Combine([done, done]));
        Assert.Equal(ExpansionOutcome.Partly, GraphSource.Combine([done, none]));
        Assert.Equal(ExpansionOutcome.NotExpanded, GraphSource.Combine([ExpansionOutcome.NotExpanded]));
    }

    /// <summary>
    /// Without a trace no amount is a method's result: the inspector says so and gives the reason (regression).
    /// </summary>
    [Fact]
    public void WithoutTraceNoTracedAmountIsClaimed()
    {
        var graph = GraphSamples.Graph();
        var scene = GraphScene.Build(new GraphSnapshot(
            graph, new Dictionary<string, IReadOnlyList<Label>>(), TracingMethodKind.Fifo, [], null,
            GraphSource.TraceFailedText));

        var node = InspectorContent.ForNode(scene, scene.Node(GraphSamples.EKey)!);
        var edge = InspectorContent.ForEdge(scene, scene.Edge(GraphSamples.VU)!);

        Assert.Contains(new InspectorFact("Spårat in", InspectorContent.NoTraceText), node.Facts);
        Assert.Contains(node.Why, line => line.Text == GraphSource.TraceFailedText);
        Assert.Contains(new InspectorFact("Spårat", InspectorContent.NoTraceText), edge.Facts);
        Assert.Contains(edge.Why, line => line.Text == GraphSource.TraceFailedText);
        Assert.DoesNotContain(edge.Why, line => line.Text.StartsWith("Inga av de spårade", Ordinal));
    }

    /// <summary>Only a line to or from a cluster says it rests on the clustering; times and heights are plain.</summary>
    [Fact]
    public void LineSaysClusteringOnlyForClusters()
    {
        var scene = GraphSamples.Scene();

        var single = InspectorContent.ForEdge(scene, scene.Edge(GraphSamples.VU)!);
        var cluster = InspectorContent.ForEdge(scene, scene.Edge(GraphSamples.VK)!);

        Assert.DoesNotContain(single.Why, line => line.Text.Contains("klustringens", Ordinal));
        Assert.Contains(cluster.Why, line => line.Text.Contains("klustringens", Ordinal));
        Assert.Contains("(1970-01-01 00:00 UTC)", InspectorContent.Time(DateTimeOffset.UnixEpoch), Ordinal);
        Assert.Contains(cluster.Details, line => line.StartsWith("Block 101 ·", Ordinal));
    }

    /// <summary>A service label wins over a victim label on the same node.</summary>
    [Fact]
    public void ServiceWinsOverVictim()
    {
        var scene = GraphSamples.Scene(On(
            GraphSamples.UKey,
            GraphSamples.Label(GraphSamples.U, LabelKind.Victim),
            GraphSamples.Label(GraphSamples.U, LabelKind.Exchange)));
        var victim = GraphSamples.Scene(On(GraphSamples.UKey, GraphSamples.Label(GraphSamples.U, LabelKind.Victim)));

        Assert.Equal(NodeSort.Exchange, scene.Node(GraphSamples.UKey)!.Look.Sort);
        Assert.Equal(NodeSort.Victim, victim.Node(GraphSamples.UKey)!.Look.Sort);
    }

    /// <summary>Finds the repository's root, where <c>Chainsaw.slnx</c> lies.</summary>
    /// <returns>The root.</returns>
    internal static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Chainsaw.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository's root was not found.");
    }

    /// <summary>
    /// A large graph is built in time linear in its lines: each node finds its own lines by key instead of looking
    /// through all of them, so that a scene of many thousand actors does not freeze the view.
    /// </summary>
    [Fact]
    public void LargeGraphBuildsInLinearTime()
    {
        const int count = 20_000;
        var nodes = Enumerable.Range(0, count)
            .Select(i => GraphSamples.Node($"n{i}", GraphNodeKind.Address, [GraphSamples.Address(1000 + i)], null))
            .ToList();
        var edges = Enumerable.Range(0, count - 1)
            .Select(i => GraphSamples.Edge($"e{i}", $"n{i}", $"n{i + 1}", i % 4, i, 0.01m))
            .ToList();
        var graph = new TraceGraph(
            [], nodes, edges,
            new UtxoGraph(
                new Dictionary<Txid, GraphTransaction>(), new Dictionary<Outpoint, CoinSpend>(),
                new Dictionary<Txid, IReadOnlyList<TrailEndHint>>()),
            [], [], []);
        var snapshot = new GraphSnapshot(
            graph, new Dictionary<string, IReadOnlyList<Label>>(), TracingMethodKind.Fifo, [], null, null);
        GraphScene.Build(snapshot);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var scene = GraphScene.Build(snapshot);
        watch.Stop();

        Assert.Equal(count, scene.Nodes.Count);
        Assert.Equal(GraphSamples.Btc(0.01m), scene.Node("n1")!.In);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(3), watch.Elapsed.ToString());
    }

    /// <summary>A resource key in a XAML file.</summary>
    /// <returns>The pattern.</returns>
    [GeneratedRegex("x:Key=\"([^\"]+)\"")]
    private static partial Regex IconKey();
}
