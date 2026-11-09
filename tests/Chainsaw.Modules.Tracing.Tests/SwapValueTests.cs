using System.Numerics;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.EthSynthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The trace through a swap (the user's decision 2026-11-08, sketch SW1a) against a key of synthetic transactions:
/// the traced share of what went in becomes the same share of what came out (<c>swap-value v1</c>), by FIFO and
/// haircut, with a partly traced stake, several swaps in a row, and the user's choice that the value does not follow;
/// and the exposure follows the same rule.
/// </summary>
public sealed class SwapValueTests : IDisposable
{
    private static readonly EthereumAddress Victim = Address("sw-victim");
    private static readonly EthereumAddress Other = Address("sw-other");
    private static readonly EthereumAddress Scammer = Address("sw-scammer");
    private static readonly EthereumAddress Mule = Address("sw-mule");
    private static readonly EthereumAddress Pool = Address("sw-pool");
    private static readonly EthereumAddress Pool2 = Address("sw-pool-2");
    private static readonly EthereumAddress Exchange = Address("sw-exchange");

    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// The scammer first got 400 USDT from someone else and then 600 from the victim; it swaps 500 USDT for 0.25 ether
    /// and sends the ether to an exchange.
    /// </summary>
    private async Task<TraceGraph> PartlyTracedStake()
    {
        _harness.Ethereum.Add(
            Tx("sw-other-pays", 100, Other, 0m, Token(Other, Scammer, Usdt(400))),
            Tx("sw-victim-pays", 101, Victim, 0m, Token(Victim, Scammer, Usdt(600))),
            Tx("sw-swap", 120, Scammer, 0m, Token(Scammer, Pool, Usdt(500)),
                (EthereumTransferKind.Internal, Pool, Scammer, Ether(0.25m))),
            Tx("sw-cash-out", 130, Scammer, 0m, Eth(Scammer, Exchange, 0.25m)));
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));
        return await WithWholeSwaps("sw-swap");
    }

    /// <summary>
    /// The victim pays 1 000 USDT to the scammer, who swaps all of it for 0.5 ether and sends it to a mule; the mule
    /// swaps the ether for 990 USDT and pays it to an exchange.
    /// </summary>
    private async Task<TraceGraph> TwoSwapsInARow()
    {
        _harness.Ethereum.Add(
            Tx("sw2-pay", 100, Victim, 0m, Token(Victim, Scammer, Usdt(1_000))),
            Tx("sw2-swap-1", 110, Scammer, 0m, Token(Scammer, Pool, Usdt(1_000)),
                (EthereumTransferKind.Internal, Pool, Scammer, Ether(0.5m))),
            Tx("sw2-to-mule", 120, Scammer, 0m, Eth(Scammer, Mule, 0.5m)),
            Tx("sw2-swap-2", 130, Mule, 0m, Eth(Mule, Pool2, 0.5m), Token(Pool2, Mule, Usdt(990))),
            Tx("sw2-cash-out", 140, Mule, 0m, Token(Mule, Exchange, Usdt(990))));
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Mule));
        return await WithWholeSwaps("sw2-swap-1", "sw2-swap-2");
    }

    /// <summary>
    /// Reads the swaps' transactions whole, so that their signer is known without a fee (the facit has no fees, so
    /// that the amounts stay exact).
    /// </summary>
    private async Task<TraceGraph> WithWholeSwaps(params string[] labels)
    {
        foreach (var label in labels)
        {
            await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumTransaction(Hash(label)));
        }

        return await _harness.Load();
    }

    private Task<AccountTraceResult> TraceFromVictim(TraceGraph graph, TracingMethodKind method) =>
        _harness.Service.TraceAccountsAsync(graph, new AccountTraceRequest(method,
            [.. graph.Account.Transfers.Where(transfer => transfer.From == Victim).Select(AccountSeed.Of)]));

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
    [InlineData(TracingMethodKind.Fifo, 100, 0.05)]
    [InlineData(TracingMethodKind.Haircut, 300, 0.15)]
    public async Task The_traced_share_of_a_partly_traced_stake_follows_into_the_new_asset(
        TracingMethodKind method, int tracedUsdt, double tracedEther)
    {
        // FIFO: the 400 USDT that came first leave first, so 100 of the 500 swapped are the victim's (20 %).
        // Haircut: 600 of the 1 000 held are the victim's, so 300 of the 500 swapped are (60 %).
        var graph = await PartlyTracedStake();

        var result = await TraceFromVictim(graph, method);

        var conversion = Assert.Single(result.Swaps);
        Assert.True(conversion.Follows);
        Assert.Equal(Usdt(tracedUsdt), conversion.TracedSold);
        Assert.Equal(Ether((decimal)tracedEther), conversion.TracedBought);
        var exchange = Assert.Single(result.Ends, end => end.NodeKey == Key(graph, Exchange));
        Assert.Equal(Ether((decimal)tracedEther), exchange.Amount.Amount);
        Assert.True(exchange.Amount.RestsOnSwap);
        Assert.Contains(SwapValue.Analysis, exchange.Amount.Explain(), StringComparison.Ordinal);
        var swapped = Assert.Single(result.Ends, end => end.Reason == TrailEndReason.Swapped);
        Assert.Equal(Usdt(tracedUsdt), swapped.Amount.Amount);
        Assert.Equal(Key(graph, Scammer), swapped.NodeKey);
        Assert.Equal(Usdt(600 - tracedUsdt), Assert.Single(result.Ends, end => end.Reason == TrailEndReason.Unspent)
            .Amount.Amount);
        EveryUnitEndsSomewhere(result);
    }

    [Fact]
    public async Task The_pool_never_counts_the_money_that_went_into_the_swap()
    {
        var graph = await PartlyTracedStake();

        var result = await TraceFromVictim(graph, TracingMethodKind.Fifo);

        Assert.DoesNotContain(result.Ends, end => end.NodeKey == Key(graph, Pool));
        var into = Assert.Single(result.Edges, edge => graph.Edges.Single(e => e.Key == edge.EdgeKey).To
            == Key(graph, Pool));
        Assert.Equal(Usdt(100), into.Amount.Amount);
        Assert.False(into.Amount.RestsOnSwap);
        var outOf = Assert.Single(result.Edges, edge => graph.Edges.Single(e => e.Key == edge.EdgeKey).From
            == Key(graph, Pool));
        Assert.Equal(Ether(0.05m), outOf.Amount.Amount);
        Assert.True(outOf.Amount.RestsOnSwap);
    }

    [Theory]
    [InlineData(TracingMethodKind.Fifo)]
    [InlineData(TracingMethodKind.Haircut)]
    public async Task The_value_follows_through_several_swaps_in_a_row(TracingMethodKind method)
    {
        var graph = await TwoSwapsInARow();

        var result = await TraceFromVictim(graph, method);

        Assert.Equal(2, result.Swaps.Count);
        Assert.All(result.Swaps, swap => Assert.Equal("100,0 %", swap.ShareText));
        var exchange = Assert.Single(result.Ends, end => end.NodeKey == Key(graph, Exchange));
        Assert.Equal(Usdt(990), exchange.Amount.Amount);
        Assert.Equal(2, exchange.Amount.Swaps.Count);
        var usdt = result.Assets.Single(asset => asset.Traced.Asset == KnownTokens.EthereumUsdt);
        Assert.Equal(Usdt(1_000), usdt.Traced);
        Assert.Equal(Usdt(990), usdt.SwappedIn);
        var ether = result.Assets.Single(asset => asset.Traced.Asset == Asset.Ether);
        Assert.True(ether.Traced.IsZero);
        Assert.Equal(Ether(0.5m), ether.SwappedIn);
        EveryUnitEndsSomewhere(result);
    }

    [Fact]
    public async Task Choosing_that_the_value_does_not_follow_ends_the_trail_in_the_swap()
    {
        var graph = await TwoSwapsInARow();
        var first = graph.Account.Swaps.Single(swap => swap.Account == Scammer.ToChainAddress());

        var stop = _harness.Service.StopAtSwap(_harness.Case, first, "  Bytet hör inte till brottet  ");
        graph = await _harness.Load();
        var result = await TraceFromVictim(graph, TracingMethodKind.Fifo);

        Assert.Equal("Bytet hör inte till brottet", stop.Reason);
        Assert.Equal([stop], graph.SwapStops);
        var conversion = Assert.Single(result.Swaps);
        Assert.False(conversion.Follows);
        Assert.Equal(stop, conversion.Stop);
        Assert.True(conversion.TracedBought.IsZero);
        var end = Assert.Single(result.Ends);
        Assert.Equal(TrailEndReason.SwapStopped, end.Reason);
        Assert.True(end.Reason.EndsTrail());
        Assert.Equal(Usdt(1_000), end.Amount.Amount);
        Assert.Single(result.Assets);
        Assert.DoesNotContain(result.Ends, e => e.NodeKey == Key(graph, Exchange));
        var journal = _harness.Case.ReadJournal().Last();
        Assert.Equal(TracingJournalKinds.SwapStopped, journal.Kind);
        Assert.Equal(journal.Sequence, stop.JournalSequence);
    }

    [Fact]
    public async Task Taking_the_choice_back_lets_the_value_follow_again_and_stays_in_the_history()
    {
        var graph = await TwoSwapsInARow();
        var first = graph.Account.Swaps.Single(swap => swap.Account == Scammer.ToChainAddress());
        var stop = _harness.Service.StopAtSwap(_harness.Case, first, null);

        Assert.Equal(stop, _harness.Service.StopAtSwap(_harness.Case, first, "igen"));
        Assert.True(_harness.Service.FollowSwap(_harness.Case, stop, null));
        Assert.False(_harness.Service.FollowSwap(_harness.Case, stop, null));
        graph = await _harness.Load();
        var result = await TraceFromVictim(graph, TracingMethodKind.Fifo);

        Assert.Empty(graph.SwapStops);
        Assert.Equal(2, result.Swaps.Count(swap => swap.Follows));
        var entry = Assert.Single(_harness.Service.GetSwapStopHistory(_harness.Case));
        Assert.True(entry.IsTakenBack);
        Assert.Null(entry.Stop.Reason);
        Assert.Equal(TracingJournalKinds.SwapFollowed, _harness.Case.ReadJournal().Last().Kind);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task The_choice_survives_reopening_the_case()
    {
        var graph = await TwoSwapsInARow();
        var second = graph.Account.Swaps.Single(swap => swap.Account == Mule.ToChainAddress());
        _harness.Service.StopAtSwap(_harness.Case, second, null);

        _harness.Reopen();
        var result = await TraceFromVictim(await _harness.Load(), TracingMethodKind.Haircut);

        Assert.Equal([true, false], result.Swaps.Select(swap => swap.Follows));
        var end = Assert.Single(result.Ends, end => end.Reason == TrailEndReason.SwapStopped);
        Assert.Equal(Ether(0.5m), end.Amount.Amount);
    }

    [Fact]
    public async Task A_reason_that_is_too_long_is_refused()
    {
        var graph = await TwoSwapsInARow();

        Assert.Throws<ArgumentException>(() =>
            _harness.Service.StopAtSwap(_harness.Case, graph.Account.Swaps[0], new string('x', 1_001)));
    }

    [Fact]
    public void The_rule_rounds_down_and_never_gives_more_than_came_out()
    {
        var sold = new AssetAmount(KnownTokens.EthereumUsdt, 3);

        Assert.Equal(new BigInteger(3), SwapValue.Follow(1, sold, 10));
        Assert.Equal(new BigInteger(10), SwapValue.Follow(5, sold, 10));
        Assert.Equal(BigInteger.Zero, SwapValue.Follow(0, sold, 10));
        Assert.Equal(BigInteger.Zero, SwapValue.Follow(1, AssetAmount.Zero(sold.Asset), 10));
        Assert.Equal("33,3 %", SwapValue.Percent(1, 3));
    }

    [Theory]
    [InlineData(TracingMethodKind.Fifo)]
    [InlineData(TracingMethodKind.Haircut)]
    public async Task The_exposure_follows_the_same_rule(TracingMethodKind method)
    {
        var graph = await TwoSwapsInARow();
        var analysis = new ExposureAnalysis([new AccountFifoMethod(), new AccountHaircutMethod()]);
        var services = new HashSet<string>(StringComparer.Ordinal) { Key(graph, Exchange) };
        var request = new ExposureRequest(
            graph, Key(graph, Scammer), KnownTokens.EthereumUsdt, ExposureDirection.Out, 3, method, services);

        var followed = analysis.Analyse(request);
        var mule = graph.Account.Swaps.Single(swap => swap.Account == Mule.ToChainAddress());
        _harness.Service.StopAtSwap(_harness.Case, mule, null);
        var stopped = analysis.Analyse(request with { Graph = await _harness.Load() });

        var reach = Assert.Single(followed.Reached);
        Assert.Equal(Usdt(1_000), followed.Total);
        Assert.Equal(Usdt(1_000), reach.Amount);
        Assert.Equal(2, reach.Hops);
        Assert.Contains(SwapValue.Analysis, followed.Explain(reach), StringComparison.Ordinal);
        Assert.Equal(2, reach.Swaps.Count);
        Assert.Empty(stopped.Reached);
        Assert.Equal(Usdt(1_000), stopped.Rest[ExposureRest.TrailEnds]);
    }

    [Fact]
    public async Task The_exposure_read_in_follows_the_swap_back_to_what_went_in()
    {
        var graph = await TwoSwapsInARow();
        var analysis = new ExposureAnalysis([new AccountFifoMethod(), new AccountHaircutMethod()]);
        var services = new HashSet<string>(StringComparer.Ordinal) { Key(graph, Victim) };
        var request = new ExposureRequest(
            graph, Key(graph, Mule), KnownTokens.EthereumUsdt, ExposureDirection.In, 3, TracingMethodKind.Fifo,
            services);

        var result = analysis.Analyse(request);

        Assert.Equal(Usdt(990), result.Total);
        var victim = Assert.Single(result.Reached);
        Assert.Equal((Key(graph, Victim), Usdt(990), 2), (victim.NodeKey, victim.Amount, victim.Hops));
        var sum = result.Reached.Aggregate(BigInteger.Zero, (s, r) => s + r.Amount.Units) + result.Unknown.Units;
        Assert.Equal(result.Total.Units, sum);
    }
}
