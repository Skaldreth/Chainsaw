using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>
/// Watches on Litecoin and BSC: each is read through its own source in step with the chain, a Litecoin hit is counted
/// in LTC on Litecoin's addresses, a BSC hit in BEP-20 USDT on BSC's accounts, and both are read back from the case.
/// </summary>
public sealed class LitecoinBscWatchTests : IDisposable
{
    /// <summary>A made-up Litecoin address (<c>ltc1q…</c>) of 20 bytes of 0x44.</summary>
    private static readonly BitcoinAddress Node =
        BitcoinAddress.FromWitness(0, Enumerable.Repeat((byte)0x44, 20).ToArray(), BitcoinNetwork.Litecoin);

    /// <summary>A made-up Litecoin address (<c>L…</c>) of 20 bytes of 0x55.</summary>
    private static readonly BitcoinAddress Payer = BitcoinAddress.FromHash(
        BitcoinScriptType.PayToPublicKeyHash, Enumerable.Repeat((byte)0x55, 20).ToArray(), BitcoinNetwork.Litecoin);

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_litecoin_hit_is_counted_in_ltc_through_litecoins_source()
    {
        _harness.Service.Add(
            _harness.Case,
            new WatchSubject(WatchSubjectKind.Address, Node.ToChainAddress(), "ltc1q…node"),
            new WatchFilter(Chain.Litecoin, WatchDirection.In, [], [], null, null));
        await _harness.Service.CheckAsync(_harness.Case, Chain.Litecoin, 3_000_000);
        var payment = Synthetic.Tx("ltc-in", 3_000_002).In(Payer, 150_000_000).Out(Node, 149_990_000).Build();
        _harness.Litecoin.Holds(Node, payment);

        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Litecoin, 3_000_002);

        var hit = Assert.Single(run.Hits);
        Assert.Equal(new AssetAmount(Asset.Litecoin, 149_990_000), hit.Match.Amount);
        Assert.Equal(LookupMode.InStep, Assert.Single(_harness.Litecoin.Asked).Mode);
        Assert.Empty(_harness.Bitcoin.Asked);
        _harness.Reopen();
        Assert.Equal(Chain.Litecoin, _harness.Service.GetHits(_harness.Case).Single().Match.Amount.Asset.Chain);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task A_bsc_hit_in_usdt_is_read_through_bscs_source()
    {
        var node = EthereumAddress.Parse(Chain.Bsc, "0x" + new string('a', 40));
        var sender = EthereumAddress.Parse(Chain.Bsc, "0x" + new string('b', 40));
        _harness.Service.Add(
            _harness.Case,
            new WatchSubject(WatchSubjectKind.Address, node.ToChainAddress(), "0x…node"),
            new WatchFilter(Chain.Bsc, WatchDirection.In, [], [sender.ToChainAddress()], KnownTokens.BscUsdt, null));
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bsc, 21_000_000);
        var usdt = new AssetAmount(KnownTokens.BscUsdt, new BigInteger(2_500) * BigInteger.Pow(10, 18));
        var block = new EthereumBlockReference(
            21_000_040, Synthetic.EthBlockHash(21_000_040), DateTimeOffset.UnixEpoch);
        _harness.Bsc.Holds(node, EthereumHistory.Tokens, new EthereumTransfer(
            EthereumTxHash.Parse(Chain.Bsc, "0x" + new string('c', 64)), EthereumTransferKind.Token, sender, node, usdt,
            0, block));

        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Bsc, 21_000_050);

        var hit = Assert.Single(run.Hits);
        Assert.Equal(usdt, hit.Match.Amount);
        Assert.Empty(_harness.Ethereum.Asked);
        Assert.All(_harness.Bsc.Asked, asked => Assert.Equal(LookupMode.InStep, asked.Mode));
    }

    [Fact]
    public async Task On_bsc_a_token_that_calls_itself_usdt_is_no_hit_and_the_real_one_is()
    {
        var node = EthereumAddress.Parse(Chain.Bsc, "0x" + new string('a', 40));
        var sender = EthereumAddress.Parse(Chain.Bsc, "0x" + new string('b', 40));
        _harness.Service.Add(
            _harness.Case,
            new WatchSubject(WatchSubjectKind.Address, node.ToChainAddress(), "0x…node"),
            new WatchFilter(Chain.Bsc, WatchDirection.In, [], [], null, null));
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bsc, 21_000_000);
        var block = new EthereumBlockReference(
            21_000_040, Synthetic.EthBlockHash(21_000_040), DateTimeOffset.UnixEpoch);
        var fake = Asset.Token(Chain.Bsc, "0x" + new string('4', 40), "USDT", 18);
        var usdt = new AssetAmount(KnownTokens.BscUsdt, BigInteger.Pow(10, 21));
        _harness.Bsc.Holds(node, EthereumHistory.Tokens,
            new EthereumTransfer(EthereumTxHash.Parse(Chain.Bsc, "0x" + new string('d', 64)),
                EthereumTransferKind.Token, sender, node, new AssetAmount(fake, BigInteger.Pow(10, 22)), 0, block),
            new EthereumTransfer(EthereumTxHash.Parse(Chain.Bsc, "0x" + new string('e', 64)),
                EthereumTransferKind.Token, sender, node, usdt, 1, block));

        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Bsc, 21_000_050);

        Assert.Equal(usdt, Assert.Single(run.Hits).Match.Amount);
    }

    [Theory]
    [InlineData(Chain.Ethereum)]
    [InlineData(Chain.Bsc)]
    public async Task A_watch_on_every_asset_counts_the_wrapped_coin(Chain chain)
    {
        var node = EthereumAddress.Parse(chain, "0x" + new string('a', 40));
        var sender = EthereumAddress.Parse(chain, "0x" + new string('b', 40));
        FakeEthereumSource source = chain == Chain.Bsc ? _harness.Bsc : _harness.Ethereum;
        _harness.Service.Add(
            _harness.Case,
            new WatchSubject(WatchSubjectKind.Address, node.ToChainAddress(), "0x…node"),
            new WatchFilter(chain, WatchDirection.In, [], [], null, null));
        await _harness.Service.CheckAsync(_harness.Case, chain, 21_000_000);
        var block = new EthereumBlockReference(
            21_000_040, Synthetic.EthBlockHash(21_000_040), DateTimeOffset.UnixEpoch);
        var wrapped = new AssetAmount(KnownTokens.WrappedNative(chain)!, BigInteger.Pow(10, 18));
        source.Holds(node, EthereumHistory.Tokens, new EthereumTransfer(
            EthereumTxHash.Parse(chain, "0x" + new string('f', 64)), EthereumTransferKind.Token, sender, node, wrapped,
            0, block));

        var run = await _harness.Service.CheckAsync(_harness.Case, chain, 21_000_050);

        Assert.Equal(wrapped, Assert.Single(run.Hits).Match.Amount);
    }

    [Fact]
    public void A_bitcoin_address_is_not_a_litecoin_node()
    {
        var bitcoin = Synthetic.Wpkh("bitcoin");

        Assert.Throws<ArgumentException>(() => _harness.Service.Add(
            _harness.Case,
            new WatchSubject(WatchSubjectKind.Address, new ChainAddress(Chain.Litecoin, bitcoin.Value), "x"),
            new WatchFilter(Chain.Litecoin, WatchDirection.In, [], [], null, null)));
    }
}
