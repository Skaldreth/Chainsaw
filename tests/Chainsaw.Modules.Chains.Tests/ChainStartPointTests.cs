using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

public sealed class ChainStartPointTests
{
    /// <summary>A made-up TRON address: Base58Check with the version byte 0x41 and twenty bytes.</summary>
    private static readonly string TronAddress =
        Base58Check.Encode([0x41, .. Enumerable.Range(1, 20).Select(b => (byte)b)]);

    [Fact]
    public void A_bitcoin_address_and_txid_are_recognised_and_supported()
    {
        var address = ChainStartPoint.Recognise(" " + Fixtures.AddressText("tr") + " ");
        var transaction = ChainStartPoint.Recognise(Fixtures.Confirmed.Value.ToUpperInvariant());

        Assert.Equal((Chain.Bitcoin, StartPointKind.Address, true), (address.Chain, address.Kind, address.IsSupported));
        Assert.Equal(Fixtures.AddressText("tr"), address.Value);
        Assert.NotNull(address.Bitcoin?.Address);
        Assert.Equal((Chain.Bitcoin, StartPointKind.Transaction), (transaction.Chain, transaction.Kind));
        Assert.Equal(Fixtures.Confirmed.Value, transaction.Value);
    }

    [Fact]
    public void A_tron_address_is_recognised_and_supported()
    {
        var tron = ChainStartPoint.Recognise(TronAddress);

        Assert.Equal((Chain.Tron, StartPointKind.Address), (tron.Chain, tron.Kind));
        Assert.True(tron.IsRecognised);
        Assert.True(tron.IsSupported);
        Assert.Null(tron.Bitcoin);
    }

    [Fact]
    public void A_tron_address_with_a_broken_checksum_is_unknown()
    {
        var broken = TronAddress[..^1] + (TronAddress[^1] == 'a' ? 'b' : 'a');

        Assert.Equal(StartPointKind.Unknown, ChainStartPoint.Recognise(broken).Kind);
    }

    [Fact]
    public void Ethereum_addresses_and_hashes_are_recognised_in_lower_case()
    {
        var address = ChainStartPoint.Recognise("0x" + new string('A', 40));
        var transaction = ChainStartPoint.Recognise("0x" + new string('b', 64));

        Assert.Equal((Chain.Ethereum, StartPointKind.Address), (address.Chain, address.Kind));
        Assert.Equal("0x" + new string('a', 40), address.Value);
        Assert.Equal((Chain.Ethereum, StartPointKind.Transaction), (transaction.Chain, transaction.Kind));
        Assert.True(address.IsSupported);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hej")]
    [InlineData("0x1234")]
    [InlineData("0xzz00000000000000000000000000000000000000")]
    public void Other_text_is_unknown(string text)
    {
        var unknown = ChainStartPoint.Recognise(text);

        Assert.Equal(StartPointKind.Unknown, unknown.Kind);
        Assert.Null(unknown.Chain);
        Assert.False(unknown.IsRecognised);
    }

    [Fact]
    public void A_very_long_text_is_not_looked_at()
    {
        Assert.Equal(StartPointKind.Unknown, ChainStartPoint.Recognise(new string('1', 10_000)).Kind);
    }

    [Fact]
    public void A_bare_hash_is_bitcoin_unless_the_chain_is_named()
    {
        var hash = new string('c', 64);

        Assert.Equal(Chain.Bitcoin, ChainStartPoint.Recognise(hash).Chain);
        var tron = ChainStartPoint.Recognise(Chain.Tron, hash);
        Assert.Equal((Chain.Tron, StartPointKind.Transaction), (tron.Chain, tron.Kind));
    }

    [Fact]
    public void A_named_chain_only_recognises_its_own_values()
    {
        Assert.Equal(StartPointKind.Unknown, ChainStartPoint.Recognise(Chain.Bitcoin, TronAddress).Kind);
        Assert.Equal(StartPointKind.Unknown, ChainStartPoint.Recognise(Chain.Ethereum, Fixtures.AddressText("tr")).Kind);
        Assert.Equal(Chain.Bitcoin, ChainStartPoint.Recognise(Chain.Bitcoin, Fixtures.AddressText("tr")).Chain);
    }

    [Fact]
    public void An_Ethereum_address_with_a_wrong_checksum_is_not_recognised()
    {
        var right = ChainStartPoint.Recognise("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed");
        Assert.Equal((Chain.Ethereum, "0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed"), (right.Chain, right.Value));
        Assert.Equal(StartPointKind.Unknown,
            ChainStartPoint.Recognise("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAeD").Kind);
        Assert.Equal(StartPointKind.Unknown,
            ChainStartPoint.Recognise(Chain.Ethereum, "0x5AAeb6053F3E94C9b9A09f33669435E7Ef1BeAed").Kind);
    }

    [Fact]
    public void A_litecoin_address_is_recognised_and_fetched_but_not_traced()
    {
        var address = BitcoinAddress.FromWitness(0, Enumerable.Repeat((byte)0x33, 20).ToArray(), BitcoinNetwork.Litecoin);

        var litecoin = ChainStartPoint.Recognise(address.Value);

        Assert.Equal((Chain.Litecoin, StartPointKind.Address), (litecoin.Chain, litecoin.Kind));
        Assert.False(litecoin.IsSupported);
        Assert.True(litecoin.CanFetch);
        Assert.Equal(address, litecoin.UtxoAddress);
        Assert.Null(litecoin.Bitcoin);
    }

    [Fact]
    public void A_bsc_value_is_recognised_only_with_its_chain()
    {
        var text = "0x" + new string('A', 40);

        var bare = ChainStartPoint.Recognise(text);
        var bsc = ChainStartPoint.Recognise(Chain.Bsc, text);
        var hash = ChainStartPoint.Recognise(Chain.Bsc, "0x" + new string('b', 64));

        Assert.Equal(Chain.Ethereum, bare.Chain);
        Assert.Equal((Chain.Bsc, StartPointKind.Address, true), (bsc.Chain, bsc.Kind, bsc.IsSupported));
        Assert.Equal(Chain.Bsc, bsc.EthereumAccount!.Chain);
        Assert.Equal((Chain.Bsc, StartPointKind.Transaction), (hash.Chain, hash.Kind));
        Assert.Equal(Chain.Bsc, hash.AccountTransaction!.Chain);
    }

    [Fact]
    public void A_bare_hash_named_litecoin_is_a_litecoin_transaction()
    {
        var litecoin = ChainStartPoint.Recognise(Chain.Litecoin, new string('C', 64));

        Assert.Equal((Chain.Litecoin, StartPointKind.Transaction), (litecoin.Chain, litecoin.Kind));
        Assert.Equal(new string('c', 64), litecoin.Value);
    }

    [Fact]
    public void An_mweb_address_is_litecoin_where_the_trail_ends_and_nothing_is_fetched()
    {
        var mweb = "ltcmweb1" + new string('q', 112);

        var start = ChainStartPoint.Recognise(mweb);

        Assert.Equal((Chain.Litecoin, StartPointKind.Address), (start.Chain, start.Kind));
        Assert.True(start.IsShielded);
        Assert.False(start.CanFetch);
        Assert.False(start.IsSupported);
        Assert.Null(start.UtxoAddress);
        Assert.False(ChainStartPoint.Recognise("ltcmweb1" + new string('b', 112)).IsShielded);
    }
}
