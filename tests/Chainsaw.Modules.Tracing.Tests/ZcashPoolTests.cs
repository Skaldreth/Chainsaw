using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// Zcash's transparent part in the graph and its shielded pools as the end of the trail: money into a pool (t→z)
/// ends there with the chain's own amount, change that stays transparent is followed on, and money out of a pool
/// (z→t) is marked where it appears. Against a synthetic chain.
/// </summary>
public sealed class ZcashPoolTests : IDisposable
{
    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Money_into_the_pool_ends_the_trail_and_the_transparent_change_goes_on()
    {
        var (payment, shield) = Shielding();
        await _harness.Expand(ZcashPkh("victim-payee"));
        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForTransaction(shield.Txid))).Graph;

        var result = _harness.Service.Trace(
            graph, new TraceRequest(TracingMethodKind.Fifo, [new TraceSeed(new Outpoint(payment.Txid, 0))]));

        Assert.Equal(new AssetAmount(Asset.Zcash, 1_000_000), result.Traced);
        Assert.Equal(new AssetAmount(Asset.Zcash, 700_000), result.EndedBy(TrailEndReason.PrivacyCoin));
        Assert.Equal(new AssetAmount(Asset.Zcash, 10_000), result.EndedBy(TrailEndReason.MinerFee));
        var change = Assert.Single(result.Coins, coin => coin.Coin == new Outpoint(shield.Txid, 0));
        Assert.Equal(290_000, (long)change.Amount.Amount.Units);
        var pool = Assert.Single(result.Ends, end => end.Reason == TrailEndReason.PrivacyCoin);
        Assert.Equal(ShieldedPoolEnds.Source, Assert.Single(pool.Basis).Source);
    }

    [Fact]
    public async Task A_transaction_into_the_pool_keeps_its_own_node_with_the_pools_end()
    {
        var (_, shield) = Shielding();
        await _harness.Expand(ZcashPkh("victim-payee"));
        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForTransaction(shield.Txid))).Graph;

        var node = Assert.Single(graph.Nodes, node => node.Transaction == shield.Txid);
        Assert.Equal(GraphNodeKind.Transaction, node.Kind);
        Assert.Equal(Chain.Zcash, node.Chain);
        var end = Assert.Single(node.Ends);
        Assert.Equal(TrailEndReason.PrivacyCoin, end.Reason);
        Assert.Contains("0.00700000 ZEC gick in", end.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Money_out_of_the_pool_is_marked_where_it_appears()
    {
        var unshield = Tx(Chain.Zcash, "zec-unshield", 3_550_000)
            .Pool(-2_000_000).Out(ZcashPkh("cash-out"), 1_990_000).Build();
        _harness.Zcash.Add(unshield);

        var graph = (await _harness.Expand(ZcashPkh("cash-out"))).Graph;

        var node = Assert.Single(graph.Nodes, node => node.Transaction == unshield.Txid);
        var end = Assert.Single(node.Ends);
        Assert.Equal(TrailEndReason.PrivacyCoin, end.Reason);
        Assert.Contains("kom ut ur", end.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exposure_counts_the_pool_as_where_the_trail_ends()
    {
        var (_, shield) = Shielding();
        await _harness.Expand(ZcashPkh("victim-payee"));
        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForTransaction(shield.Txid))).Graph;
        var payee = graph.Nodes.Single(node => node.GraphAddresses.Contains(ZcashPkh("victim-payee")));

        var exposure = new ExposureAnalysis([new FifoMethod(), new HaircutMethod()]).Analyse(new ExposureRequest(
            graph, payee.Key, Asset.Zcash, ExposureDirection.Out, 3, TracingMethodKind.Fifo,
            new HashSet<string>(StringComparer.Ordinal)));

        Assert.Equal(700_000, (long)exposure.Rest[ExposureRest.TrailEnds].Units);
    }

    /// <summary>
    /// A payment to a transparent address and its shielding: 0.01 ZEC to the payee, who puts 0.007 into the Sapling
    /// pool, keeps 0.0029 as transparent change and pays 0.0001 in fees.
    /// </summary>
    /// <returns>The payment and the shielding.</returns>
    private (BitcoinTransaction Payment, BitcoinTransaction Shield) Shielding()
    {
        var payment = Tx(Chain.Zcash, "zec-payment", 3_549_000)
            .In(ZcashPkh("victim"), 1_500_000).Out(ZcashPkh("victim-payee"), 1_000_000).Build();
        var shield = Tx(Chain.Zcash, "zec-shield", 3_549_010)
            .Spend(payment, 0).Out(ZcashPkh("payee-change"), 290_000).Pool(700_000).Build();
        _harness.Zcash.Add(payment, shield);
        return (payment, shield);
    }
}
