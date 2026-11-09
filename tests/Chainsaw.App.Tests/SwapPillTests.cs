using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Requests.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.EthSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// The choice per swap in the graph (sketch SW1a): the pill <c>₮ → Ξ</c> shows whether the traced value follows, the
/// line after it is dashed when it does, the box writes the steps, a click stores the choice through the editor, and
/// the choice is in the swapper's history with <em>Ta tillbaka</em>.
/// </summary>
public sealed class SwapPillTests
{
    private static readonly DateTimeOffset Morning = new(2026, 11, 8, 9, 30, 0, TimeSpan.Zero);

    private static readonly TracingMethodId Fifo = new(TracingMethodKind.Fifo, 1);

    private static TokenSwap Swap => EthSamples.Graph().Account.Swaps[0];

    private static string Sold => EdgeKey(S, P, "USDT");

    private static string Bought => EdgeKey(P, S, "ETH");

    /// <summary>The scene with the swap followed: 400 traced USDT in, 0.1 traced ether out, or stopped by the user.</summary>
    private static GraphScene SwapScene(SwapStop? stop = null)
    {
        var graph = EthSamples.Graph() with { SwapStops = stop is null ? [] : [stop] };
        var conversion = new SwapConversion(
            Swap, Usdt(400), stop is null ? Ether(0.1m) : AssetAmount.Zero(Asset.Ether), Fifo, stop);
        IReadOnlyList<AssetTrace> assets = stop is null
            ? [UsdtTrace(), new AssetTrace(AssetAmount.Zero(Asset.Ether), [SwappedLine(conversion)], [])]
            : [UsdtTrace()];
        var trace = new AccountTraceResult(Fifo, [AccountSeed.Of(Transfers[0])], assets) { Swaps = [conversion] };
        return GraphScene.Build(new GraphSnapshot(
            graph, new Dictionary<string, IReadOnlyList<Label>>(), TracingMethodKind.Fifo, [], null, null)
        {
            AccountSeeds = [AccountSeed.Of(Transfers[0])],
            AccountTrace = trace,
        });
    }

    private static AssetTrace UsdtTrace() =>
        new(Usdt(1_000), [Traced(EdgeKey(V, S, "USDT"), Usdt(1_000)), Traced(Sold, Usdt(400))], []);

    private static TracedEdge SwappedLine(SwapConversion conversion) =>
        new(Bought, new TracedAmount(Ether(0.1m), Fifo, []) { Swaps = [conversion] }, false);

    private static SwapStop Stop(long sequence = 4) =>
        new(sequence, Swap.Transaction, Swap.Account, "Bytet hör inte hit", 7, Morning);

    [Fact]
    public void The_line_after_a_followed_swap_is_dashed_in_the_accent()
    {
        var scene = SwapScene();

        var after = scene.Edge(Bought)!;
        var pill = scene.Edge(Sold)!;

        Assert.Equal(GraphInk.Accent, after.Ink);
        Assert.Equal(EdgeDash.Uncertain, after.Dash);
        Assert.False(pill.IsSwapStopped);
        Assert.Single(pill.SwapConversions);
        Assert.Equal(EdgeDash.None, scene.Edge(EdgeKey(V, S, "USDT"))!.Dash);
    }

    [Fact]
    public void The_box_writes_the_steps_of_a_followed_swap()
    {
        var box = SwapPill.Box(SwapScene().Edge(Sold)!)!;

        Assert.Equal("Värdet följer med genom swappen", box.Title);
        Assert.Equal("Förval · en bedömning · priset inte prövat", box.Sub);
        Assert.Equal(["In", "Andel", "Ut", SwapPill.FollowsText], box.Steps.Select(step => step.Key));
        Assert.Equal("100,0 %", box.Steps[1].Value);
        Assert.Equal("FIFO v1", box.Steps[1].Sub);
        Assert.Equal("Ξ " + AmountText.Number(Ether(0.1m)), box.Steps[3].Value);
        Assert.EndsWith(SwapValue.Analysis, box.Foot, StringComparison.Ordinal);
    }

    [Fact]
    public void A_stopped_swap_shows_the_users_choice_and_leaves_the_line_after_it_untraced()
    {
        var scene = SwapScene(Stop());

        var pill = scene.Edge(Sold)!;
        var box = SwapPill.Box(pill)!;

        Assert.True(pill.IsSwapStopped);
        Assert.Equal(GraphInk.Untraced, scene.Edge(Bought)!.Ink);
        Assert.Equal("Värdet följer inte med", box.Title);
        Assert.StartsWith("Ditt val · ", box.Sub, StringComparison.Ordinal);
        Assert.Equal(("Spårat", "inget"), (box.Steps[3].Key, box.Steps[3].Value));
        var why = InspectorContent.ForEdge(scene, pill).Why;
        Assert.Contains(why, line => line.Topic == GuideTopic.Swaps && line.Text.Contains("Bytet hör inte hit"));
    }

    [Fact]
    public void Why_on_a_followed_swap_gives_the_steps_and_the_analysis()
    {
        var scene = SwapScene();

        var why = InspectorContent.ForEdge(scene, scene.Edge(Sold)!).Why;
        var after = InspectorContent.ForEdge(scene, scene.Edge(Bought)!).Why;

        Assert.Contains(why, line => line.Topic == GuideTopic.Swaps
            && line.Text.Contains(SwapValue.Analysis, StringComparison.Ordinal)
            && line.Text.Contains("100,0 %", StringComparison.Ordinal));
        Assert.Contains(after, line => line.Text.Contains(SwapValue.Analysis, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Choosing_not_to_follow_stores_the_choice_and_reads_the_graph_again()
    {
        var rig = new Rig();
        var loads = rig.Source.Loads.Count;

        rig.Graph.ToggleSwap(Sold);
        Assert.Equal(Sold, rig.Graph.OpenSwap);
        await rig.Graph.ChooseSwapAsync(Sold, follows: false);

        Assert.Equal([Swap], rig.Editor.SwapStops);
        Assert.Null(rig.Graph.OpenSwap);
        Assert.True(rig.Source.Loads.Count > loads);
    }

    [Fact]
    public async Task Choosing_what_already_holds_stores_nothing()
    {
        var rig = new Rig();
        var loads = rig.Source.Loads.Count;

        await rig.Graph.ChooseSwapAsync(Sold, follows: true);

        Assert.Empty(rig.Editor.SwapStops);
        Assert.Empty(rig.Editor.Undone);
        Assert.Equal(loads, rig.Source.Loads.Count);
    }

    [Fact]
    public async Task Choosing_to_follow_again_takes_the_choice_back()
    {
        var rig = new Rig(Stop(9));

        await rig.Graph.ChooseSwapAsync(Sold, follows: true);

        Assert.Equal(("swap", "9", (string?)null), Assert.Single(rig.Editor.Undone));
    }

    [Fact]
    public void A_line_without_a_swap_never_opens_a_pill()
    {
        var rig = new Rig();

        rig.Graph.ToggleSwap(EdgeKey(V, S, "USDT"));

        Assert.Null(rig.Graph.OpenSwap);
    }

    [Fact]
    public void The_choice_is_in_the_swappers_history_and_can_be_taken_back()
    {
        var scene = SwapScene();
        var stop = Stop();
        var data = new NodeHistoryData([], [], [], null, new Dictionary<long, string> { [7] = "Maja", [8] = "Kim" })
        {
            SwapStops = [new(stop, new SwapStopUndo(5, null, 8, Morning.AddHours(1)))],
        };

        var rows = NodeHistory.Build(scene, scene.Node(Key(S))!, data);

        var choice = Assert.Single(rows, row => row.Kind == HistoryKind.SwapStop);
        Assert.Equal(NodeHistory.SwapStopText + " · ₮ → Ξ", choice.Text);
        Assert.Contains("Maja · Bytet hör inte hit", choice.Sub, StringComparison.Ordinal);
        Assert.Equal(HistoryUndo.FollowSwap, choice.Undo);
        Assert.True(choice.IsWithdrawn);
        Assert.False(choice.CanUndo);
        Assert.Contains(rows, row => row.Kind == HistoryKind.Withdrawal && row.Sub == "Kim");
        Assert.Equal(new HashSet<long> { 7, 8 }, NodeHistoryData.JournalNeeds(data.SwapStops));
    }

    /// <summary>
    /// The swap scene where the mule M is an exchange and the line to it rests on the swap, so that the request's
    /// trail names the analysis; and the scammer's node has the swap's ends.
    /// </summary>
    private static GraphScene ExchangeAfterSwap()
    {
        var conversion = new SwapConversion(Swap, Usdt(400), Ether(0.1m), Fifo, null);
        var subject = LabelSubject.ForAccount(M.ToChainAddress());
        var origin = new LabelOrigin(
            "x", LabelSource.Own, LabelSource.Own, "egen", "1", new OperatorProfile("Maja", "Analytiker", "Enheten"),
            Morning, "Sett", null);
        var exchange = new Label(
            subject, LabelKind.Exchange, "Börs B", [new LabelEntry(1, subject, LabelKind.Exchange, "Börs B", origin,
                false, 1)]);
        var toMule = new TracedEdge(
            EdgeKey(S, M, "USDT"), new TracedAmount(Usdt(500), Fifo, []) { Swaps = [conversion] }, false);
        var swapped = new TraceEnd(
            TrailEndReason.Swapped, new TracedAmount(Usdt(400), Fifo, []), null, null, Key(S), "Bytt.", [], [], false);
        var trace = new AccountTraceResult(
            Fifo, [AccountSeed.Of(Transfers[0])],
            [new AssetTrace(Usdt(1_000), [toMule, SwappedLine(conversion)], [swapped, swapped])])
        {
            Swaps = [conversion],
        };
        return GraphScene.Build(new GraphSnapshot(
            EthSamples.Graph(), new Dictionary<string, IReadOnlyList<Label>> { [Key(M)] = [exchange] },
            TracingMethodKind.Fifo, [], null, null)
        {
            AccountSeeds = [AccountSeed.Of(Transfers[0])],
            AccountTrace = trace,
        });
    }

    [Fact]
    public void A_requests_trail_through_a_swap_names_the_analysis_the_share_and_the_transaction()
    {
        var exit = Assert.Single(
            RequestExits.Find(ExchangeAfterSwap()), found => found.Kind == RequestKind.Information);

        var swap = Assert.Single(exit.Trail!.Swaps);
        Assert.Equal((Swap.Transaction.Value, "₮ → Ξ", "100,0 %", SwapValue.Analysis),
            (swap.Transaction, swap.Pill, swap.Share, swap.Analysis));
    }

    [Fact]
    public void The_inspector_names_the_swap_analysis_and_never_adds_up_the_swapped_ends()
    {
        var scene = ExchangeAfterSwap();

        var scammer = InspectorContent.ForNode(scene, scene.Node(Key(S))!).Why;
        var mule = InspectorContent.ForNode(scene, scene.Node(Key(M))!).Why;

        var swapped = Assert.Single(scammer, line => line.Text.StartsWith("Gick in i 2 swap", StringComparison.Ordinal));
        Assert.Equal(GuideTopic.Swaps, swapped.Topic);
        Assert.DoesNotContain(scammer, line => line.Text.Contains("800", StringComparison.Ordinal));
        Assert.Contains(mule, line => line.Topic == GuideTopic.Swaps
            && line.Text.Contains(SwapValue.Analysis, StringComparison.Ordinal));
    }

    [Fact]
    public void A_refused_swap_says_why_in_why_and_a_choice_on_it_says_there_is_no_swap_any_more()
    {
        var graph = EthSamples.Graph();
        var refused = graph with
        {
            Account = graph.Account with
            {
                Swaps = [],
                RefusedSwaps = [new SwapRefusal(Swap, P.ToChainAddress(), "Börs, uttag funnet (etikett)")],
            },
        };
        var scene = GraphScene.Build(new GraphSnapshot(
            refused, new Dictionary<string, IReadOnlyList<Label>>(), TracingMethodKind.Fifo, [], null, null));
        var data = new NodeHistoryData([], [], [], null, new Dictionary<long, string> { [7] = "Maja" })
        {
            SwapStops = [new(Stop(), null)],
        };

        var line = InspectorContent.ForEdge(scene, scene.Edge(Sold)!).Why;
        var node = InspectorContent.ForNode(scene, scene.Node(Key(S))!).Why;
        var row = Assert.Single(NodeHistory.Build(scene, scene.Node(Key(S))!, data),
            each => each.Kind == HistoryKind.SwapStop);

        Assert.Null(scene.Edge(Sold)!.Swap);
        Assert.Contains(line, each => each.Text.StartsWith("Ingen swap i tx", StringComparison.Ordinal)
            && each.Text.Contains("Börs, uttag funnet (etikett)", StringComparison.Ordinal));
        Assert.Contains(node, each => each.Text.StartsWith("Ingen swap i tx", StringComparison.Ordinal));
        Assert.Contains(NodeHistory.NoSwapText, row.Text, StringComparison.Ordinal);
        Assert.True(row.CanUndo);
    }

    [Fact]
    public void A_token_known_only_by_its_symbol_is_marked_with_a_question_mark()
    {
        var fake = Asset.Token(Chain.Ethereum, "0x2222222222222222222222222222222222222222", "USDC", 6);

        Assert.Equal("USDC?", SwapMarks.MarkOf(fake));
        Assert.Equal("$", SwapMarks.MarkOf(Modules.Chains.Contracts.KnownTokens.EthereumUsdc));
        Assert.Equal("WETH", SwapMarks.MarkOf(Modules.Chains.Contracts.KnownTokens.EthereumWeth));
        var swap = Swap with { Sold = new AssetAmount(fake, 5) };
        Assert.Equal("USDC? → Ξ", SwapMarks.Of(swap).Text);
    }

    [Fact]
    public void A_swap_at_an_absurd_price_is_dashed_says_why_and_has_nothing_to_choose()
    {
        var check = new SwapPriceCheck(Swap, SwapPriceVerdict.Unreasonable, 4_000m, 0.01m, "SEK");
        var trace = new AccountTraceResult(Fifo, [AccountSeed.Of(Transfers[0])], [UsdtTrace()])
        {
            PriceChecks = [check],
        };
        var scene = GraphScene.Build(new GraphSnapshot(
            EthSamples.Graph(), new Dictionary<string, IReadOnlyList<Label>>(), TracingMethodKind.Fifo, [], null, null)
        {
            AccountSeeds = [AccountSeed.Of(Transfers[0])],
            AccountTrace = trace,
        });
        var pill = scene.Edge(Sold)!;
        var rig = new Rig();
        rig.Source.Scene = () => scene;
        rig.Session.Open(new TestCase("2026-0002"));

        rig.Graph.ToggleSwap(Sold);

        Assert.True(pill.IsSwapPriceAbsurd);
        Assert.False(pill.IsSwapPriceUntested);
        Assert.StartsWith("Orimligt pris", SwapPill.Box(pill)!.Title, StringComparison.Ordinal);
        Assert.Equal(("Spårat", "inget"), (SwapPill.Box(pill)!.Steps[3].Key, SwapPill.Box(pill)!.Steps[3].Value));
        Assert.Contains(InspectorContent.ForEdge(scene, pill).Why,
            line => line.Text.Contains(SwapPriceCheck.Check, StringComparison.Ordinal));
        Assert.Null(rig.Graph.OpenSwap);
    }

    /// <summary>The graph over fakes, showing the swap scene.</summary>
    private sealed class Rig
    {
        public Rig(SwapStop? stop = null)
        {
            Source.Scene = () => SwapScene(stop);
            var workflow = new CaseWorkflow(
                new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), Session);
            Graph = new GraphViewModel(
                Session, workflow, Source, TestWays.For(new TestExitConnection()), new InlineUi(), Editor,
                new TestClock());
            Session.Open(new TestCase("2026-0001"));
        }

        public CaseSession Session { get; } = new();

        public TestGraphSource Source { get; } = new();

        public TestGraphEditor Editor { get; } = new();

        public GraphViewModel Graph { get; }
    }
}
