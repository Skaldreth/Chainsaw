using System.Numerics;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.EthSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// The swaps' rates through the graph's source (swap-price v1): the rates at a swap's time are fetched for both
/// sides, with wrapped ether valued as ether, before the lines' rates, and the trace read again tests the price; a
/// swap at an absurd price is then refused, and the change is said once.
/// </summary>
public sealed class SwapPriceRatesTests
{
    /// <summary>The swap's time: the block of transaction 3.</summary>
    private static readonly DateTimeOffset SwapTime = Block(103).Time;

    [Fact]
    public async Task Wrapped_ether_at_an_absurd_price_is_refused_once_the_swap_s_rates_are_fetched()
    {
        var (source, prices, @case) = Setup(Weth(100));

        var before = await source.LoadAsync(@case, TracingMethodKind.Fifo, CancellationToken.None);
        Assert.Equal(SwapPriceVerdict.NotTested, Assert.Single(before.Snapshot.AccountTrace!.PriceChecks).Verdict);
        Assert.True(before.HasUntestedSwapPrices);

        var rates = await source.FillRatesAsync(@case, before, CancellationToken.None);
        var after = await source.LoadAsync(@case, TracingMethodKind.Fifo, CancellationToken.None);

        Assert.True(rates.SwapRatesFetched);
        // The swap's rates are asked of the source before any line's (the fake's case holds a time once asked).
        var first = prices.Asked.First(asked => asked.Mode == PriceLookupMode.AsNeeded).Request;
        Assert.Equal((KnownTokens.EthereumUsdt, SwapTime), (first.Asset, first.At));
        Assert.Contains(prices.Asked, asked => asked.Request.Asset == Asset.Ether && asked.Request.At == SwapTime);
        Assert.DoesNotContain(prices.Asked, asked => asked.Request.Asset == KnownTokens.EthereumWeth);
        var check = Assert.Single(after.Snapshot.AccountTrace!.PriceChecks);
        Assert.Equal(SwapPriceVerdict.Unreasonable, check.Verdict);
        Assert.False(after.HasUntestedSwapPrices);
        var told = Assert.Single(GraphViewModel.RecalculatedSwaps(
            before.Snapshot.AccountTrace.PriceChecks, after.Snapshot.AccountTrace.PriceChecks));
        Assert.Contains("orimligt pris", told, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_fair_price_is_tested_and_its_small_loss_is_said_as_a_recalculation()
    {
        var (source, _, @case) = Setup(Weth(0.13m));

        var before = await source.LoadAsync(@case, TracingMethodKind.Fifo, CancellationToken.None);
        await source.FillRatesAsync(@case, before, CancellationToken.None);
        var after = await source.LoadAsync(@case, TracingMethodKind.Fifo, CancellationToken.None);

        var check = Assert.Single(after.Snapshot.AccountTrace!.PriceChecks);
        Assert.Equal(SwapPriceVerdict.Reasonable, check.Verdict);
        Assert.Equal(0.975m, check.KeptShare);
        var told = Assert.Single(GraphViewModel.RecalculatedSwaps(
            before.Snapshot.AccountTrace!.PriceChecks, after.Snapshot.AccountTrace.PriceChecks));
        Assert.Contains("priset prövat, 2,5 % skillnad", told, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_source_that_asks_to_wait_leaves_the_price_untested_and_nothing_is_recalculated()
    {
        var (source, prices, @case) = Setup(Weth(100));
        prices.Throttles = true;

        var before = await source.LoadAsync(@case, TracingMethodKind.Fifo, CancellationToken.None);
        var rates = await source.FillRatesAsync(@case, before, CancellationToken.None);

        Assert.False(rates.SwapRatesFetched);
        Assert.True(rates.IsThrottled);
        Assert.Single(prices.Asked, asked => asked.Mode == PriceLookupMode.AsNeeded);
        Assert.Contains("inte prövat", Assert.Single(before.Snapshot.AccountTrace!.PriceChecks).Summary,
            StringComparison.Ordinal);
    }

    /// <summary>Gives wrapped ether.</summary>
    private static AssetAmount Weth(decimal amount) =>
        new(KnownTokens.EthereumWeth, new BigInteger(amount * 1_000_000_000_000_000_000m));

    /// <summary>
    /// Gives a source over the sample graph whose swap sells 400 USDT for the wrapped ether given, at the swap's time;
    /// the rates (ether 30 000 kr, USDT 10 kr) are only at the source, and the trace checks the swap's price with what
    /// the source hands it.
    /// </summary>
    private static (GraphSource Source, TestPrices Prices, TestCase Case) Setup(AssetAmount bought)
    {
        var graph = EthSamples.Graph();
        var swap = new TokenSwap(Hash(3), S.ToChainAddress(), Usdt(400), bought)
        {
            SoldMovements = [AccountSeed.Of(Transfers[3])],
            Time = SwapTime,
        };
        graph = graph with { Account = graph.Account with { Swaps = [swap] } };
        var tracing = new TestTracing
        {
            Graph = graph,
            AccountTracer = (traced, request) => new AccountTraceResult(
                new TracingMethodId(request.Method, 1), request.Seeds, [])
            {
                PriceChecks = [.. traced.Account.Swaps.Select(each => SwapValue.CheckPrice(each, request.Pricing))],
            },
        };
        var prices = new TestPrices { AtSourceOnly = true };
        prices.Rates[Asset.Ether] = 30_000m;
        prices.Rates[KnownTokens.EthereumUsdt] = 10m;
        var source = new GraphSource(tracing, new TestAttribution(), new TestClustering(), null, prices);
        return (source, prices, new TestCase("2026-0001"));
    }
}
