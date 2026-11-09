using System.Numerics;

namespace Chainsaw.SharedKernel.Tests;

/// <summary>Assets, amounts of any size, addresses with their chain and the scopes steps are registered for.</summary>
public sealed class AssetTests
{
    private const string UsdtOnEthereum = "0xdAC17F958D2ee523a2206206994597C13D831ec7";
    private const string UsdtOnTron = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";

    [Fact]
    public void Each_chain_has_its_model_and_its_coin()
    {
        Assert.Equal(LedgerModel.Utxo, Chain.Bitcoin.Model());
        Assert.Equal(LedgerModel.Account, Chain.Ethereum.Model());
        Assert.Equal(LedgerModel.Account, Chain.Tron.Model());
        Assert.Equal(("BTC", 8), (Chain.Bitcoin.NativeAsset().Symbol, Chain.Bitcoin.NativeAsset().Decimals));
        Assert.Equal(("ETH", 18), (Chain.Ethereum.NativeAsset().Symbol, Chain.Ethereum.NativeAsset().Decimals));
        Assert.Equal(("TRX", 6), (Chain.Tron.NativeAsset().Symbol, Chain.Tron.NativeAsset().Decimals));
        Assert.All(Enum.GetValues<Chain>(), chain => Assert.Equal(AssetKind.Native, chain.NativeAsset().Kind));
    }

    [Fact]
    public void A_token_is_known_by_its_chain_and_contract_not_its_symbol()
    {
        var usdt = Asset.Token(Chain.Ethereum, UsdtOnEthereum, "USDT", 6);
        var fake = Asset.Token(Chain.Ethereum, "0x" + new string('1', 40), "USDT", 6);
        var onTron = Asset.Token(Chain.Tron, UsdtOnTron, "USDT", 6);

        Assert.Equal(
            usdt, Asset.Token(Chain.Ethereum, UsdtOnEthereum.ToUpperInvariant().Replace("0X", "0x"), "USDT", 6));
        Assert.Equal(usdt, Asset.Token(Chain.Ethereum, UsdtOnEthereum, "USD₮", 6));
        Assert.Equal(usdt.GetHashCode(), Asset.Token(Chain.Ethereum, UsdtOnEthereum, "USD₮", 6).GetHashCode());
        Assert.Equal(
            new AssetAmount(usdt, 2),
            new AssetAmount(usdt, 1) + new AssetAmount(Asset.Token(Chain.Ethereum, UsdtOnEthereum, "USD₮", 6), 1));
        Assert.Equal(UsdtOnEthereum.ToLowerInvariant(), usdt.Contract);
        Assert.NotEqual(usdt, fake);
        Assert.NotEqual(usdt, onTron);
        Assert.Equal(UsdtOnTron, onTron.Contract);
        Assert.NotEqual(onTron, Asset.Token(Chain.Tron, UsdtOnTron.ToLowerInvariant(), "USDT", 6));
        Assert.Equal("USDT (Tron:" + UsdtOnTron + ")", onTron.ToString());
        Assert.Equal("BTC", Asset.Bitcoin.ToString());
    }

    [Fact]
    public void An_asset_that_is_not_valid_is_refused()
    {
        Assert.Throws<ArgumentException>(() => Asset.Token(Chain.Ethereum, " ", "USDT", 6));
        Assert.Throws<ArgumentException>(() => Asset.Token(Chain.Ethereum, UsdtOnEthereum, "", 6));
        Assert.Throws<ArgumentOutOfRangeException>(() => Asset.Token(Chain.Ethereum, UsdtOnEthereum, "X", -1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Asset.Token(Chain.Ethereum, UsdtOnEthereum, "X", Asset.MaximumDecimals + 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Asset.Token((Chain)99, UsdtOnEthereum, "X", 6));
    }

    [Fact]
    public void Wei_beyond_64_bits_adds_and_subtracts_exactly()
    {
        // 100 000 ether is 10^23 wei, far past long.MaxValue (about 9.2 × 10^18).
        var whale = AssetAmount.Parse(Asset.Ether, "100000");
        var one = new AssetAmount(Asset.Ether, BigInteger.One);

        Assert.Equal(BigInteger.Pow(10, 23), whale.Units);
        Assert.True(whale.Units > long.MaxValue);
        Assert.Equal(BigInteger.Pow(10, 23) + 1, (whale + one).Units);
        Assert.Equal(BigInteger.Pow(10, 23) - 1, (whale - one).Units);
        Assert.Equal("100000.000000000000000001", (whale + one).ToDecimalString());
        Assert.Equal("99999.999999999999999999 ETH", (whale - one).ToString());
        Assert.True(whale > one && one < whale && whale >= one + one - one && one <= whale);
    }

    [Theory]
    [InlineData("0.5120", 51_200_000, "0.51200000")]
    [InlineData("1", 100_000_000, "1.00000000")]
    [InlineData("0.00000001", 1, "0.00000001")]
    [InlineData("-0.1", -10_000_000, "-0.10000000")]
    public void Bitcoin_is_written_as_on_the_chain_and_matches_satoshi(string text, long satoshi, string written)
    {
        var amount = AssetAmount.Parse(Asset.Bitcoin, text);

        Assert.Equal(new Satoshi(satoshi).ToAmount(), amount);
        Assert.Equal(written, amount.ToDecimalString());
        Assert.Equal(new Satoshi(satoshi).ToString(), amount.ToDecimalString());
    }

    [Theory]
    [InlineData("0.000000001")]
    [InlineData("1.")]
    [InlineData(".5")]
    [InlineData("1.2.3")]
    [InlineData("1e8")]
    [InlineData("")]
    public void A_bitcoin_amount_that_is_not_valid_is_refused(string text) =>
        Assert.Throws<FormatException>(() => AssetAmount.Parse(Asset.Bitcoin, text));

    [Fact]
    public void Amounts_of_different_assets_are_never_counted_together()
    {
        var bitcoin = new Satoshi(1).ToAmount();
        var ether = new AssetAmount(Asset.Ether, 1);
        var usdt = new AssetAmount(Asset.Token(Chain.Ethereum, UsdtOnEthereum, "USDT", 6), 1);

        Assert.Throws<InvalidOperationException>(() => bitcoin + ether);
        Assert.Throws<InvalidOperationException>(() => ether - usdt);
        Assert.Throws<InvalidOperationException>(() => ether < usdt);
        Assert.NotEqual(ether, usdt);
        Assert.True(AssetAmount.Zero(Asset.Ether).IsZero);
        Assert.Throws<InvalidOperationException>(() => default(AssetAmount).Asset);
        Assert.Throws<InvalidOperationException>(() => default(AssetAmount) + bitcoin);
        var sixDecimals = Asset.Token(Chain.Ethereum, UsdtOnEthereum, "USDT", 6);
        var eighteen = Asset.Token(Chain.Ethereum, UsdtOnEthereum, "USDT", 18);
        Assert.Throws<InvalidOperationException>(() => new AssetAmount(sixDecimals, 1) + new AssetAmount(eighteen, 1));
    }

    [Fact]
    public void A_token_with_few_decimals_is_written_with_its_own()
    {
        var usdt = Asset.Token(Chain.Tron, UsdtOnTron, "USDT", 6);
        var noDecimals = Asset.Token(Chain.Ethereum, UsdtOnEthereum, "WHOLE", 0);

        Assert.Equal("12.500000 USDT", AssetAmount.Parse(usdt, "12.5").ToString());
        Assert.Equal("42 WHOLE", new AssetAmount(noDecimals, 42).ToString());
        Assert.Throws<FormatException>(() => AssetAmount.Parse(noDecimals, "1.5"));
    }

    [Fact]
    public void An_address_carries_its_chain()
    {
        var upper = new ChainAddress(Chain.Ethereum, "0xABCDEF0000000000000000000000000000000001");

        Assert.Equal("0xabcdef0000000000000000000000000000000001", upper.Value);
        Assert.NotEqual(new ChainAddress(Chain.Tron, "T1"), new ChainAddress(Chain.Ethereum, "T1"));
        Assert.Equal("Tron:T1", new ChainAddress(Chain.Tron, " T1 ").ToString());
        Assert.Throws<ArgumentException>(() => new ChainAddress(Chain.Tron, "T 1"));
        Assert.Throws<ArgumentException>(() => new ChainAddress(Chain.Tron, ""));
    }

    [Fact]
    public void A_scope_covers_the_chains_of_its_model()
    {
        var utxo = ChainScope.AllOf(LedgerModel.Utxo);
        var accounts = ChainScope.AllOf(LedgerModel.Account);
        var ethereum = ChainScope.Only(Chain.Ethereum);

        Assert.True(utxo.Covers(Chain.Bitcoin));
        Assert.False(utxo.Covers(Chain.Ethereum));
        Assert.True(accounts.Covers(Chain.Tron) && accounts.Covers(Chain.Ethereum));
        Assert.False(accounts.Covers(Chain.Bitcoin));
        Assert.True(ethereum.Covers(Chain.Ethereum));
        Assert.False(ethereum.Covers(Chain.Tron));
        Assert.Equal(ChainScope.Only(Chain.Tron, Chain.Ethereum), ChainScope.Only(Chain.Ethereum, Chain.Tron));
        Assert.Equal(
            ChainScope.Only(Chain.Tron, Chain.Ethereum).GetHashCode(),
            ChainScope.Only(Chain.Ethereum, Chain.Tron).GetHashCode());
        Assert.NotEqual(accounts, ChainScope.Only(Chain.Ethereum, Chain.Tron));
        Assert.Equal("Account: Ethereum, Tron", ChainScope.Only(Chain.Tron, Chain.Ethereum).ToString());
        Assert.Throws<ArgumentException>(() => ChainScope.Only(Chain.Bitcoin, Chain.Ethereum));
        Assert.Throws<ArgumentNullException>(() => ChainScope.Only(Chain.Ethereum, null!));
        Assert.IsNotType<Chain[]>(ethereum.Chains);
    }
}
