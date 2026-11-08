using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.EthSynthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The edges of the trace through a swap (SW1a), from the independent review's probes: a cycle back and forth, a
/// payment to a third party in the swap's transaction, a swap before the traced money came, tiny and huge amounts,
/// start points on both sides of a swap, a movement into a swap linked to another chain, and many swaps in a row.
/// </summary>
public sealed class SwapEdgeTests : IDisposable
{
    private static readonly EthereumAddress Victim = Address("p-victim");
    private static readonly EthereumAddress Other = Address("p-other");
    private static readonly EthereumAddress Scammer = Address("p-scammer");
    private static readonly EthereumAddress Accomplice = Address("p-accomplice");
    private static readonly EthereumAddress Pool = Address("p-pool");
    private static readonly EthereumAddress Pool2 = Address("p-pool-2");
    private static readonly EthereumAddress Exchange = Address("p-exchange");

    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    private async Task<TraceGraph> Build(string[] whole, params EthereumTransaction[] txs)
    {
        _harness.Ethereum.Add(txs);
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));
        foreach (var label in whole)
        {
            await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumTransaction(Hash(label)));
        }

        return await _harness.Load();
    }

    private Task<AccountTraceResult> FromVictim(TraceGraph graph, TracingMethodKind method) =>
        _harness.Service.TraceAccountsAsync(graph, new AccountTraceRequest(method,
            [.. graph.Account.Transfers.Where(t => t.From == Victim).Select(AccountSeed.Of)]));

    private static string Key(TraceGraph graph, EthereumAddress address) =>
        graph.NodeOf(address.ToChainAddress())!.Key;

    private static void EveryUnitEndsSomewhere(AccountTraceResult result)
    {
        foreach (var asset in result.Assets)
        {
            var ended = asset.Ends.Aggregate(AssetAmount.Zero(asset.Traced.Asset), (sum, end) => sum + end.Amount.Amount);
            Assert.Equal(asset.Total, ended);
        }
    }

    [Theory]
    [InlineData(TracingMethodKind.Fifo, 596)]
    [InlineData(TracingMethodKind.Haircut, 588)]
    public async Task A_cycle_back_and_forth_ends_where_the_money_left(TracingMethodKind method, int expected)
    {
        var graph = await Build(["c-s1", "c-s2"],
            Tx("c-other", 100, Other, 0m, Token(Other, Scammer, Usdt(400))),
            Tx("c-victim", 101, Victim, 0m, Token(Victim, Scammer, Usdt(600))),
            Tx("c-s1", 110, Scammer, 0m, Token(Scammer, Pool, Usdt(500)),
                (EthereumTransferKind.Internal, Pool, Scammer, Ether(0.25m))),
            Tx("c-s2", 120, Scammer, 0m, Eth(Scammer, Pool2, 0.25m), Token(Pool2, Scammer, Usdt(480))),
            Tx("c-out", 130, Scammer, 0m, Token(Scammer, Exchange, Usdt(980))));

        var result = await FromVictim(graph, method);

        var exchange = Assert.Single(result.Ends, end => end.NodeKey == Key(graph, Exchange));
        Assert.Equal(Usdt(expected), exchange.Amount.Amount);
        EveryUnitEndsSomewhere(result);
    }

    [Fact]
    public async Task A_payment_to_a_third_party_in_the_swaps_transaction_is_not_part_of_the_swap()
    {
        var graph = await Build(["t-swap"],
            Tx("t-victim", 101, Victim, 0m, Token(Victim, Scammer, Usdt(1000))),
            Tx("t-swap", 110, Scammer, 0m, Token(Scammer, Pool, Usdt(500)), Token(Scammer, Accomplice, Usdt(500)),
                (EthereumTransferKind.Internal, Pool, Scammer, Ether(0.25m))),
            Tx("t-out", 130, Scammer, 0m, Eth(Scammer, Exchange, 0.25m)));

        var result = await FromVictim(graph, TracingMethodKind.Fifo);

        var swap = Assert.Single(graph.Account.Swaps);
        Assert.Equal(Usdt(500), swap.Sold);
        Assert.Equal(Usdt(500), Assert.Single(result.Ends, end => end.Reason == TrailEndReason.Swapped).Amount.Amount);
        Assert.Equal(Usdt(500), Assert.Single(result.Ends, end => end.NodeKey == Key(graph, Accomplice)).Amount.Amount);
        Assert.Equal(Ether(0.25m), Assert.Single(result.Ends, end => end.NodeKey == Key(graph, Exchange)).Amount.Amount);
        var toAccomplice = graph.Edges.Single(edge => edge.To == Key(graph, Accomplice));
        Assert.DoesNotContain(toAccomplice.Transfers, swap.IsSold);
        Assert.Equal(Usdt(500), result.Edges.Single(edge => edge.EdgeKey == toAccomplice.Key).Amount.Amount);
        EveryUnitEndsSomewhere(result);
    }

    [Fact]
    public async Task A_swap_before_the_traced_money_came_carries_none_of_it()
    {
        var graph = await Build(["b-swap"],
            Tx("b-other", 100, Other, 0m, Token(Other, Scammer, Usdt(1000))),
            Tx("b-swap", 110, Scammer, 0m, Token(Scammer, Pool, Usdt(1000)),
                (EthereumTransferKind.Internal, Pool, Scammer, Ether(0.5m))),
            Tx("b-victim", 120, Victim, 0m, Token(Victim, Scammer, Usdt(1000))));

        foreach (var method in new[] { TracingMethodKind.Fifo, TracingMethodKind.Haircut })
        {
            var result = await FromVictim(graph, method);

            Assert.Empty(result.Swaps);
            EveryUnitEndsSomewhere(result);
        }
    }

    [Fact]
    public async Task Tiny_and_huge_amounts_never_create_money()
    {
        var graph = await Build(["x-swap"],
            Tx("x-other", 100, Other, 0m, Token(Other, Scammer, new AssetAmount(KnownTokens.EthereumUsdt, 2))),
            Tx("x-victim", 101, Victim, 0m, Token(Victim, Scammer, new AssetAmount(KnownTokens.EthereumUsdt, 1))),
            Tx("x-swap", 110, Scammer, 0m, Token(Scammer, Pool, new AssetAmount(KnownTokens.EthereumUsdt, 3)),
                (EthereumTransferKind.Internal, Pool, Scammer, new AssetAmount(Asset.Ether, BigInteger.Pow(10, 40)))),
            Tx("x-out", 130, Scammer, 0m, Eth(Scammer, Exchange, 0.000000001m)));

        foreach (var method in new[] { TracingMethodKind.Fifo, TracingMethodKind.Haircut })
        {
            var result = await FromVictim(graph, method);

            EveryUnitEndsSomewhere(result);
            Assert.All(result.Swaps, swap => Assert.True(swap.TracedBought <= swap.Swap.Bought));
        }
    }

    [Fact]
    public async Task Start_points_on_both_sides_of_a_swap_are_refused_as_seeds_in_each_others_flow()
    {
        var graph = await Build(["d-swap"],
            Tx("d-victim", 101, Victim, 0m, Token(Victim, Scammer, Usdt(1000))),
            Tx("d-swap", 110, Scammer, 0m, Token(Scammer, Pool, Usdt(1000)),
                (EthereumTransferKind.Internal, Pool, Scammer, Ether(0.5m))),
            Tx("d-out", 130, Scammer, 0m, Eth(Scammer, Exchange, 0.5m)));
        var seeds = graph.Account.Transfers.Where(t => t.From == Victim || (t.From == Pool && t.To == Scammer))
            .Select(AccountSeed.Of).ToList();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.Service.TraceAccountsAsync(
            graph, new AccountTraceRequest(TracingMethodKind.Fifo, seeds)));

        Assert.Contains("each other's flow", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_movement_into_a_swap_linked_to_another_chain_continues_there_and_is_never_swapped()
    {
        var graph = await Build(["l-swap"],
            Tx("l-victim", 101, Victim, 0m, Token(Victim, Scammer, Usdt(1000))),
            Tx("l-swap", 110, Scammer, 0m, Token(Scammer, Pool, Usdt(1000)),
                (EthereumTransferKind.Internal, Pool, Scammer, Ether(0.5m))),
            Tx("l-out", 130, Scammer, 0m, Eth(Scammer, Exchange, 0.5m)));
        var sold = graph.Account.Transfers.Single(t => t.From == Scammer && t.To == Pool);
        _harness.Service.LinkCrossChain(_harness.Case, AccountSeed.Of(sold),
            new ChainTransactionRef(SharedKernel.Chain.Tron, new string('c', 64)), "x");
        graph = await _harness.Load();

        var result = await FromVictim(graph, TracingMethodKind.Fifo);

        var end = Assert.Single(result.Ends);
        Assert.Equal((TrailEndReason.OtherChain, Usdt(1000)), (end.Reason, end.Amount.Amount));
        Assert.Empty(result.Swaps);
        Assert.Single(result.Assets);
        var services = new HashSet<string>(StringComparer.Ordinal) { Key(graph, Exchange) };
        var exposure = new ExposureAnalysis([new AccountFifoMethod(), new AccountHaircutMethod()]).Analyse(
            new ExposureRequest(graph, Key(graph, Scammer), KnownTokens.EthereumUsdt, ExposureDirection.Out, 3,
                TracingMethodKind.Fifo, services));
        Assert.Empty(exposure.Reached);
        Assert.Equal(Usdt(1000), exposure.Total);
    }

    [Fact]
    public async Task Many_swaps_back_and_forth_settle_and_are_never_added_up()
    {
        var txs = new List<EthereumTransaction>
        {
            Tx("m-victim", 100, Victim, 0m, Token(Victim, Scammer, Usdt(1000))),
        };
        var whole = new List<string>();
        for (var i = 0; i < 40; i++)
        {
            var label = "m-swap-" + i;
            whole.Add(label);
            txs.Add(i % 2 == 0
                ? Tx(label, 110 + i, Scammer, 0m, Token(Scammer, Pool, Usdt(1000)),
                    (EthereumTransferKind.Internal, Pool, Scammer, Ether(0.5m)))
                : Tx(label, 110 + i, Scammer, 0m, Eth(Scammer, Pool, 0.5m), Token(Pool, Scammer, Usdt(1000))));
        }

        txs.Add(Tx("m-out", 300, Scammer, 0m, Token(Scammer, Exchange, Usdt(1000))));
        var graph = await Build([.. whole], [.. txs]);

        var result = await FromVictim(graph, TracingMethodKind.Haircut);

        Assert.Equal(40, result.Swaps.Count);
        Assert.Equal(Usdt(1000), Assert.Single(result.Ends, end => end.NodeKey == Key(graph, Exchange)).Amount.Amount);
        EveryUnitEndsSomewhere(result);
    }

    [Fact]
    public async Task A_choice_for_a_swap_the_case_does_not_show_is_refused()
    {
        var graph = await Build(["b-swap"],
            Tx("b-other", 100, Other, 0m, Token(Other, Scammer, Usdt(1000))),
            Tx("b-swap", 110, Scammer, 0m, Token(Scammer, Pool, Usdt(1000)),
                (EthereumTransferKind.Internal, Pool, Scammer, Ether(0.5m))));
        var elsewhere = graph.Account.Swaps[0] with { Transaction = Hash("not-in-the-case") };

        Assert.Throws<ArgumentException>(() => _harness.Service.StopAtSwap(_harness.Case, elsewhere, null));
        Assert.Empty(_harness.Service.GetSwapStopHistory(_harness.Case));
    }
}
