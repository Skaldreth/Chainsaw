using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.EthSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// A DEX's router as a mark in the swap pill's corner (sketch DX1b): the way through the pools read from the
/// transaction's movements, the number of steps for an aggregator, and <em>Varför?</em> in the Guide's panel.
/// </summary>
public sealed class SwapRouteTests
{
    private static readonly KnownContract Uniswap =
        KnownContracts.All.First(contract => contract.Category == KnownContractCategory.Dex);

    private static readonly KnownContract OneInch =
        KnownContracts.All.First(contract => contract.Category == KnownContractCategory.Aggregator);

    private static string Sold => EdgeKey(S, P, "USDT");

    /// <summary>Gives the sample graph with its swap through a known exchange.</summary>
    private static TraceGraph Routed(KnownContract venue)
    {
        var graph = EthSamples.Graph();
        var swap = graph.Account.Swaps[0] with { Venue = venue };
        return graph with { Account = graph.Account with { Swaps = [swap] } };
    }

    private static GraphScene SceneOf(TraceGraph graph, SwapStop? stop = null) => GraphScene.Build(new GraphSnapshot(
        graph with { SwapStops = stop is null ? [] : [stop] }, new Dictionary<string, IReadOnlyList<Label>>(),
        TracingMethodKind.Fifo, [], null, null));

    [Fact]
    public void A_swap_through_no_known_exchange_has_no_mark()
    {
        Assert.Null(SceneOf(EthSamples.Graph()).Edge(Sold)!.SwapRoute);
    }

    [Fact]
    public void A_swap_through_a_router_shows_one_step_with_the_pool_and_the_router_icon()
    {
        var route = SceneOf(Routed(Uniswap)).Edge(Sold)!.SwapRoute!;

        Assert.Equal(["₮", "Ξ"], route.Steps.Select(step => step.Mark));
        Assert.Null(route.Steps[0].Pool);
        Assert.Equal("pool " + GraphScene.Shorten(P.Value), route.Steps[1].Pool);
        Assert.Equal(1, route.Exchanges);
        Assert.Null(route.MarkCount);
        Assert.Equal(Uniswap.Name, route.Title);
        Assert.True(route.IsPartial);
        Assert.Contains("ofullständig", route.Sub, StringComparison.Ordinal);
    }

    [Fact]
    public void An_aggregator_shows_every_step_of_a_whole_transaction_and_counts_them()
    {
        var router = OneInch.Address;
        var first = Address('5');
        var second = Address('6');
        var usdc = new AssetAmount(KnownTokens.EthereumUsdc, 29_500_000_000);
        var weth = new AssetAmount(KnownTokens.EthereumWeth, Ether(10).Units);
        IReadOnlyList<EthereumTransfer> moves =
        [
            Move(9, EthereumTransferKind.Token, S, router, Usdt(29_600), 1),
            Move(9, EthereumTransferKind.Token, router, first, Usdt(29_600), 2),
            Move(9, EthereumTransferKind.Token, first, router, usdc, 3),
            Move(9, EthereumTransferKind.Token, router, second, usdc, 4),
            Move(9, EthereumTransferKind.Token, second, router, weth, 5),
            Move(9, EthereumTransferKind.Internal, router, S, Ether(10), 6),
        ];
        var transaction = new EthereumTransaction(
            Hash(9), Block(109), S, router, Ether(0), Ether(0.001m), EthereumOutcome.Succeeded, moves);
        var swap = new TokenSwap(Hash(9), S.ToChainAddress(), Usdt(29_600), Ether(10)) { Venue = OneInch };
        var account = AccountGraph.Empty with
        {
            Transactions = new Dictionary<EthereumTxHash, EthereumTransaction> { [Hash(9)] = transaction },
        };

        var route = SwapRoute.Read(swap, account)!;

        Assert.Equal(["₮", "$", "Ξ"], route.Steps.Select(step => step.Mark));
        Assert.Equal("pool " + GraphScene.Shorten(first.Value), route.Steps[1].Pool);
        Assert.Equal("pool " + GraphScene.Shorten(second.Value), route.Steps[2].Pool);
        Assert.Equal(2, route.Exchanges);
        Assert.Equal("2", route.MarkCount);
        Assert.False(route.IsPartial);
        Assert.Equal("Känt kontrakt · 2 byten i en tx", route.Sub);
        Assert.EndsWith("· aggregator", route.Title, StringComparison.Ordinal);
    }

    [Fact]
    public void Why_names_the_router_its_source_and_the_rule_and_says_when_the_trail_stops()
    {
        var through = SceneOf(Routed(Uniswap)).Edge(Sold)!;
        var stopped = SceneOf(Routed(Uniswap), new SwapStop(
            4, Hash(3), S.ToChainAddress(), null, 7, new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero)))
            .Edge(Sold)!;

        var why = through.SwapRoute!.Why(through);

        Assert.All(why, line => Assert.Equal((GuideTopic.Swaps, SwapRoute.GuideHeading), (line.Topic!.Value,
            line.Heading)));
        Assert.Contains(why, line => line.Text.Contains(Uniswap.Source.AbsoluteUri, StringComparison.Ordinal));
        Assert.Contains(why, line => line.Text.Contains(KnownContracts.Version, StringComparison.Ordinal));
        Assert.Contains(why, line => line.Text.Contains("går spåret igenom", StringComparison.Ordinal));
        Assert.True(SwapRoute.GoesThrough(through));
        Assert.False(SwapRoute.GoesThrough(stopped));
        Assert.Contains(
            stopped.SwapRoute!.Why(stopped), line => line.Text.Contains("ditt val", StringComparison.Ordinal));
    }

    [Fact]
    public void A_click_on_the_mark_opens_why_in_the_guide_at_the_router_heading()
    {
        var source = new TestGraphSource { Scene = () => SceneOf(Routed(Uniswap)) };
        var session = new CaseSession();
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), session);
        var graph = new GraphViewModel(
            session, workflow, source, TestWays.For(new TestExitConnection()), new InlineUi(), new TestGraphEditor(),
            new TestClock());
        session.Open(new TestCase("2026-0001"));
        GuideRequest? asked = null;
        graph.GuideWanted += (_, request) => asked = request;

        graph.OpenSwapRouteWhy(Sold);

        Assert.NotNull(asked);
        Assert.Equal((GuideTopic.Swaps, SwapRoute.GuideHeading, Sold), (asked.Topic, asked.Heading, asked.Subject));
        Assert.True(asked.IsChoice);
        Assert.Equal("Varför? · " + Uniswap.Name, asked.Context!.Label);
    }

    [Fact]
    public void The_guide_has_the_router_heading()
    {
        using var stream = typeof(SwapRoute).Assembly.GetManifestResourceStream("Chainsaw.App.Guide.swappar.md")!;
        using var reader = new StreamReader(stream);

        Assert.Contains("## " + SwapRoute.GuideHeading, reader.ReadToEnd(), StringComparison.Ordinal);
    }
}
