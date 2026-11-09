using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.EthSynthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// A key for a gone transaction on an account chain (sketch GR9a): the victim pays the scammer 1 000 USDT and 2 ETH;
/// the scammer's first payment of 600 USDT to a mule is a transaction the source no longer knows, and a later one pays
/// 600 USDT to a second mule. A settled gone transaction moves nothing in the trace or the exposure, its fee is not
/// paid, and a trace that starts in it ends at once; an unsettled one stops the money without ending. The lines and the
/// facts stay in the graph.
/// </summary>
public sealed class GoneAccountTransactionTests : IDisposable
{
    private static readonly EthereumAddress Victim = Address("gone-victim");
    private static readonly EthereumAddress Scammer = Address("gone-scammer");
    private static readonly EthereumAddress Mule = Address("gone-mule");
    private static readonly EthereumAddress SecondMule = Address("gone-second-mule");
    private static readonly EthereumAddress Pool = Address("gone-pool");

    private readonly CaseHarness _harness = new();

    public GoneAccountTransactionTests() =>
        _harness.Ethereum.Add(
            Tx("gone-pay", 100, Victim, 0.001m, Token(Victim, Scammer, Usdt(1_000))),
            Tx("gone-pay-eth", 101, Victim, 0.001m, Eth(Victim, Scammer, 2m)),
            Tx("gone-to-mule", 110, Scammer, 0.001m, Token(Scammer, Mule, Usdt(600))),
            Tx("gone-again", 112, Scammer, 0.001m, Token(Scammer, SecondMule, Usdt(600))));

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Without_a_change_the_first_payment_carries_the_money_as_before()
    {
        var graph = await GraphAsync();

        var usdt = await TraceUsdtAsync(graph);

        Assert.Empty(graph.Account.Gone);
        Assert.Equal(Usdt(600), ReachedAt(usdt, Mule, graph));
        Assert.Equal(Usdt(400), ReachedAt(usdt, SecondMule, graph));
    }

    [Fact]
    public async Task A_settled_gone_transaction_moves_nothing_and_the_money_goes_with_the_next_payment()
    {
        Gone("gone-to-mule", settled: true);
        var graph = await GraphAsync();

        var usdt = await TraceUsdtAsync(graph);

        Assert.True(graph.Account.IsLeftOut(Hash("gone-to-mule")));
        Assert.Equal(Usdt(0), ReachedAt(usdt, Mule, graph));
        Assert.Equal(Usdt(600), ReachedAt(usdt, SecondMule, graph));
        var stays = Assert.Single(usdt.Ends, end => end.Reason == TrailEndReason.Unspent);
        Assert.Equal(Usdt(400), stays.Amount.Amount);
        Assert.Contains(GoneTransaction.Source, stays.Explanation, StringComparison.Ordinal);
        var basis = Assert.Single(stays.Basis);
        Assert.Equal(TrailEndReason.TransactionGone, basis.Reason);
        Assert.Contains(Hash("gone-to-mule").Value, basis.Explanation, StringComparison.Ordinal);
        Assert.Equal(usdt.Total, Sum(usdt));

        // The facts stay: the line to the mule and its transfer are still in the graph, only not counted.
        var line = Assert.Single(graph.Edges, edge => edge.To == graph.NodeOf(Mule.ToChainAddress())!.Key);
        Assert.DoesNotContain(usdt.Edges, edge => edge.EdgeKey == line.Key);
        Assert.Contains(graph.Account.Transfers, transfer => transfer.Transaction == Hash("gone-to-mule"));
    }

    [Fact]
    public async Task A_settled_gone_transaction_pays_no_fee()
    {
        var before = await TraceEtherAsync(await GraphAsync());
        Gone("gone-to-mule", settled: true);

        var after = await TraceEtherAsync(await GraphAsync());

        Assert.Equal(Ether(0.002m), Fees(before));
        Assert.Equal(Ether(0.001m), Fees(after));
        Assert.Equal(after.Total, Sum(after));
    }

    [Fact]
    public async Task An_unsettled_gone_transaction_stops_the_money_without_ending_it()
    {
        Gone("gone-to-mule", settled: false);
        var graph = await GraphAsync();

        var usdt = await TraceUsdtAsync(graph);

        Assert.True(graph.Account.IsUncertain(Hash("gone-to-mule")));
        var stopped = Assert.Single(usdt.Ends, end => end.Reason == TrailEndReason.GoneUncertain);
        Assert.Equal(Usdt(600), stopped.Amount.Amount);
        Assert.False(stopped.Reason.EndsTrail());
        Assert.Contains("bara en värd", stopped.Explanation, StringComparison.Ordinal);
        Assert.Equal(Usdt(400), ReachedAt(usdt, SecondMule, graph));
        Assert.Equal(usdt.Total, Sum(usdt));
    }

    [Fact]
    public async Task A_trace_that_starts_in_a_gone_transaction_ends_at_once_at_its_sender()
    {
        Gone("gone-to-mule", settled: true);
        var graph = await GraphAsync();
        var seed = graph.Account.Transfers.Single(transfer => transfer.Transaction == Hash("gone-to-mule"));

        var result = await _harness.Service.TraceAccountsAsync(
            graph, new AccountTraceRequest(TracingMethodKind.Fifo, [AccountSeed.Of(seed)]));

        var end = Assert.Single(result.Ends);
        Assert.Equal(TrailEndReason.TransactionGone, end.Reason);
        Assert.Equal(Usdt(600), end.Amount.Amount);
        Assert.Equal(graph.NodeOf(Scammer.ToChainAddress())!.Key, end.NodeKey);
        Assert.Empty(result.Edges);
    }

    [Fact]
    public async Task The_exposure_leaves_a_settled_gone_transaction_out_as_the_trace_does()
    {
        Gone("gone-to-mule", settled: true);
        var graph = await GraphAsync();

        var exposure = Exposure(graph);

        Assert.Equal(Usdt(0), Exposed(exposure, Mule, graph));
        Assert.Equal(Usdt(600), Exposed(exposure, SecondMule, graph));
        Assert.DoesNotContain(exposure.Rest.Keys, rest => rest == ExposureRest.Conflicting);
    }

    [Fact]
    public async Task The_exposure_stops_the_money_at_an_unsettled_gone_transaction()
    {
        Gone("gone-to-mule", settled: false);
        var graph = await GraphAsync();

        var exposure = Exposure(graph);

        Assert.Equal(Usdt(0), Exposed(exposure, Mule, graph));
        Assert.Equal(Usdt(400), Exposed(exposure, SecondMule, graph));
        Assert.Equal(Usdt(600), exposure.Rest[ExposureRest.Conflicting]);
    }

    [Fact]
    public async Task A_swap_in_a_gone_transaction_is_no_swap()
    {
        _harness.Ethereum.Add(Tx("gone-swap", 120, Scammer, 0.002m, Token(Scammer, Pool, Usdt(100)),
            (EthereumTransferKind.Internal, Pool, Scammer, Ether(0.05m))));
        Assert.NotEmpty((await GraphAsync()).Account.Swaps);
        Gone("gone-swap", settled: true);

        var graph = await GraphAsync();

        Assert.Empty(graph.Account.Swaps);
    }

    [Fact]
    public async Task The_gone_transaction_is_read_from_the_case_without_asking()
    {
        Gone("gone-to-mule", settled: true);
        await GraphAsync();
        var asked = _harness.Ethereum.Lookups;
        _harness.Ethereum.Online = false;

        var graph = await _harness.Load();

        Assert.Equal(asked, _harness.Ethereum.Lookups);
        Assert.True(graph.Account.IsLeftOut(Hash("gone-to-mule")));
    }

    /// <summary>Has the source's sealed answers say a transaction is gone, settled or by one host among several.</summary>
    private void Gone(string label, bool settled)
    {
        var seen = new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.Zero);
        _harness.Ethereum.BlockChanges.Add(new BlockChange(
            Hash(label).Value,
            BlockChangeKind.Gone,
            new BlockSighting(110, FakeEthereumSource.HashOf(110), seen, 10) { Endpoint = "blockscout.test" },
            new BlockSighting(null, null, seen.AddMinutes(30), 20) { Endpoint = "blockscout.test" })
        {
            Chain = SharedKernel.Chain.Ethereum,
            GoneHosts = ["blockscout.test"],
            IsSingleSource = settled,
            IsCorroborated = settled,
        });
    }

    /// <summary>Expands the scammer and gives the graph.</summary>
    private async Task<TraceGraph> GraphAsync()
    {
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        return (await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer))).Graph;
    }

    /// <summary>Traces the victim's USDT with FIFO.</summary>
    private async Task<AssetTrace> TraceUsdtAsync(TraceGraph graph) =>
        Assert.Single((await TraceFromVictimAsync(graph, KnownTokens.EthereumUsdt)).Assets);

    /// <summary>Traces the victim's ether with FIFO.</summary>
    private async Task<AssetTrace> TraceEtherAsync(TraceGraph graph) =>
        Assert.Single((await TraceFromVictimAsync(graph, Asset.Ether)).Assets);

    /// <summary>Traces the victim's payment of an asset to the scammer with FIFO.</summary>
    private Task<AccountTraceResult> TraceFromVictimAsync(TraceGraph graph, Asset asset)
    {
        var seed = graph.Account.Transfers.Single(transfer => transfer.From == Victim && transfer.Amount.Asset == asset);
        return _harness.Service.TraceAccountsAsync(
            graph, new AccountTraceRequest(TracingMethodKind.Fifo, [AccountSeed.Of(seed)]));
    }

    /// <summary>Reads the victim's USDT paid out, two hops deep, with both mules as services.</summary>
    private static ExposureResult Exposure(TraceGraph graph)
    {
        var services = new HashSet<string>(
            [graph.NodeOf(Mule.ToChainAddress())!.Key, graph.NodeOf(SecondMule.ToChainAddress())!.Key],
            StringComparer.Ordinal);
        return new ExposureAnalysis([new AccountFifoMethod()]).Analyse(new ExposureRequest(
            graph, graph.NodeOf(Victim.ToChainAddress())!.Key, KnownTokens.EthereumUsdt, ExposureDirection.Out, 2,
            TracingMethodKind.Fifo, services));
    }

    /// <summary>Sums what the trace says reached an address.</summary>
    private static AssetAmount ReachedAt(AssetTrace trace, EthereumAddress address, TraceGraph graph) =>
        trace.Ends.Where(end => end.NodeKey == graph.NodeOf(address.ToChainAddress())!.Key
                && end.Reason == TrailEndReason.NotExpanded)
            .Aggregate(AssetAmount.Zero(trace.Traced.Asset), (sum, end) => sum + end.Amount.Amount);

    /// <summary>Sums what the exposure says reached an address.</summary>
    private static AssetAmount Exposed(ExposureResult exposure, EthereumAddress address, TraceGraph graph) =>
        exposure.Reached.Where(reach => reach.NodeKey == graph.NodeOf(address.ToChainAddress())!.Key)
            .Aggregate(Usdt(0), (sum, reach) => sum + reach.Amount);

    /// <summary>Sums what a trace says went to fees.</summary>
    private static AssetAmount Fees(AssetTrace trace) =>
        trace.Ends.Where(end => end.Reason == TrailEndReason.MinerFee)
            .Aggregate(AssetAmount.Zero(trace.Traced.Asset), (sum, end) => sum + end.Amount.Amount);

    /// <summary>Sums every end of a trace.</summary>
    private static AssetAmount Sum(AssetTrace trace) =>
        trace.Ends.Aggregate(AssetAmount.Zero(trace.Traced.Asset), (sum, end) => sum + end.Amount.Amount);
}
