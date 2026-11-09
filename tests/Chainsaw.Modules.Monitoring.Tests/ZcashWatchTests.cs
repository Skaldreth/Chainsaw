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
}
