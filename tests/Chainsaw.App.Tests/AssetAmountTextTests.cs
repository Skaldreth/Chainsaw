using Chainsaw.App.Graph;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>The graph's amounts are written from the asset's own decimals, and Bitcoin's exactly as before.</summary>
public sealed class AssetAmountTextTests
{
    private static readonly Asset Usdt = Asset.Token(Chain.Tron, "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t", "USDT", 6);

    [Theory]
    [InlineData(51_200_000L, "0,5120 BTC")]
    [InlineData(12_345L, "0,00012345 BTC")]
    [InlineData(1L, "0,00000001 BTC")]
    [InlineData(100_000_000L, "1,0000 BTC")]
    public void Bitcoin_is_written_as_before(long satoshi, string written)
    {
        Assert.Equal(written, AmountText.WithUnit(new Satoshi(satoshi).ToAmount()));
        Assert.Equal(written, AmountText.BtcWithUnit(new Satoshi(satoshi)));
    }

    [Fact]
    public void Ether_and_tokens_are_written_with_their_own_decimals_and_symbol()
    {
        Assert.Equal("1,0000 ETH", AmountText.WithUnit(AssetAmount.Parse(Asset.Ether, "1")));
        Assert.Equal("0,000000000000000001", AmountText.Number(new AssetAmount(Asset.Ether, 1)));
        Assert.Equal("12,5000 USDT", AmountText.WithUnit(AssetAmount.Parse(Usdt, "12.5")));
        Assert.Equal("0,000001", AmountText.Number(new AssetAmount(Usdt, 1)));
    }

    [Fact]
    public void An_amount_longer_than_a_decimal_holds_is_written_in_full()
    {
        // 12 345 678 901 ether and one wei: 29 digits, more than a decimal holds exactly.
        var amount = AssetAmount.Parse(Asset.Ether, "12345678901.000000000000000001");

        var text = AmountText.Number(amount);

        Assert.EndsWith("901,000000000000000001", text, StringComparison.Ordinal);
        Assert.StartsWith("12", text, StringComparison.Ordinal);
        Assert.EndsWith(",0000", AmountText.Number(AssetAmount.Parse(Asset.Ether, "12345678901")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_compares_amounts_of_one_asset_only()
    {
        var bitcoin = new Satoshi(100).ToAmount();
        var ether = new AssetAmount(Asset.Ether, 50);

        Assert.Equal(EdgeWidth.Untraced, EdgeWidth.For(ether, bitcoin));
        Assert.Equal(AmountText.Number(bitcoin), AmountText.OnLine(bitcoin, ether));
    }
}
