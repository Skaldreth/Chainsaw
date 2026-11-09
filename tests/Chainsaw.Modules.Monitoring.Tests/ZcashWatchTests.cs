using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>
/// A watch on a transparent Zcash address: read through Zcash's own source in step with the chain, counted in ZEC,
/// with a minimum in ZEC, and read back from the case.
/// </summary>
public sealed class ZcashWatchTests : IDisposable
{
    /// <summary>A made-up transparent Zcash address (<c>t1…</c>) of 20 bytes of 0x44.</summary>
    private static readonly BitcoinAddress Node = BitcoinAddress.FromHash(
        BitcoinScriptType.PayToPublicKeyHash, Enumerable.Repeat((byte)0x44, 20).ToArray(), BitcoinNetwork.Zcash);

    /// <summary>A made-up transparent Zcash address (<c>t1…</c>) of 20 bytes of 0x55.</summary>
    private static readonly BitcoinAddress Payer = BitcoinAddress.FromHash(
        BitcoinScriptType.PayToPublicKeyHash, Enumerable.Repeat((byte)0x55, 20).ToArray(), BitcoinNetwork.Zcash);

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_zcash_hit_is_counted_in_zec_through_zcashs_source()
    {
        _harness.Service.Add(
            _harness.Case,
            new WatchSubject(WatchSubjectKind.Address, Node.ToChainAddress(), "t1…node"),
            new WatchFilter(Chain.Zcash, WatchDirection.In, [], [], null, new AssetAmount(Asset.Zcash, 100_000)));
        await _harness.Service.CheckAsync(_harness.Case, Chain.Zcash, 3_500_000);
        var payment = Synthetic.Tx("zec-in", 3_500_002).In(Payer, 50_000_000).Out(Node, 49_990_000).Build();
        _harness.Zcash.Holds(Node, payment);

        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Zcash, 3_500_002);

        var hit = Assert.Single(run.Hits);
        Assert.Equal(new AssetAmount(Asset.Zcash, 49_990_000), hit.Match.Amount);
        Assert.Empty(_harness.Bitcoin.Asked);
        _harness.Reopen();
        Assert.Equal(Chain.Zcash, _harness.Service.GetHits(_harness.Case).Single().Match.Amount.Asset.Chain);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Money_that_leaves_for_a_shielded_pool_is_an_outgoing_hit(bool poolKnown)
    {
        _harness.Service.Add(
            _harness.Case,
            new WatchSubject(WatchSubjectKind.Address, Node.ToChainAddress(), "t1…node"),
            new WatchFilter(Chain.Zcash, WatchDirection.Out, [], [], null, null));
        await _harness.Service.CheckAsync(_harness.Case, Chain.Zcash, 3_500_000);
        var into = poolKnown ? new ShieldedFlow(new Satoshi(29_990_000), Satoshi.Zero, [ShieldedPool.Sapling]) : null;
        var shielding = Synthetic.Tx("zec-shield", 3_500_003).In(Node, 30_000_000).Build() with { Shielded = into };
        _harness.Zcash.Holds(Node, shielding);

        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Zcash, 3_500_003);

        var hit = Assert.Single(run.Hits);
        var expected = poolKnown ? 29_990_000 : 30_000_000;
        Assert.Equal(new AssetAmount(Asset.Zcash, expected), hit.Match.Amount);
    }

    [Fact]
    public async Task A_small_fee_on_zcash_is_never_read_as_money_into_a_pool()
    {
        _harness.Service.Add(
            _harness.Case,
            new WatchSubject(WatchSubjectKind.Address, Node.ToChainAddress(), "t1…node"),
            new WatchFilter(Chain.Zcash, WatchDirection.Out, [], [], null, null));
        await _harness.Service.CheckAsync(_harness.Case, Chain.Zcash, 3_500_000);
        var payment = Synthetic.Tx("zec-pay", 3_500_004).In(Node, 30_000_000).Out(Payer, 29_990_000).Build();
        _harness.Zcash.Holds(Node, payment);

        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Zcash, 3_500_004);

        Assert.Equal(new AssetAmount(Asset.Zcash, 29_990_000), Assert.Single(run.Hits).Match.Amount);
    }

    [Fact]
    public async Task A_cluster_on_zcash_is_watched_through_zcashs_source()
    {
        var merge = Synthetic.Tx("zec-merge", 3_499_000).In(Node, 5_000).In(Payer, 5_000).Build();
        _harness.Clustering.Analyse(_harness.Case, [merge]);
        _harness.Service.Add(
            _harness.Case,
            new WatchSubject(WatchSubjectKind.Cluster, Node.ToChainAddress(), "Kluster på Zcash"),
            new WatchFilter(Chain.Zcash, WatchDirection.Both, [], [], null, null));
        await _harness.Service.CheckAsync(_harness.Case, Chain.Zcash, 3_500_000);

        await _harness.Service.CheckAsync(_harness.Case, Chain.Zcash, 3_500_001);

        Assert.Single(_harness.Service.GetWatches(_harness.Case));
        Assert.Contains(_harness.Zcash.Asked, asked => asked.Address == Node.Value);
        Assert.Empty(_harness.Bitcoin.Asked);
    }
}
