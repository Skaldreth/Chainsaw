using System.Globalization;

namespace Chainsaw.SharedKernel.Tests;

public sealed class SatoshiTests
{
    [Theory]
    [InlineData("0.5120", 51_200_000)]
    [InlineData("1", 100_000_000)]
    [InlineData("0.00000001", 1)]
    public void FromBitcoin_gives_whole_satoshi(string bitcoin, long satoshi)
    {
        var amount = decimal.Parse(bitcoin, CultureInfo.InvariantCulture);

        Assert.Equal(new Satoshi(satoshi), Satoshi.FromBitcoin(amount));
    }

    [Fact]
    public void FromBitcoin_refuses_more_than_eight_decimals()
    {
        Assert.Throws<ArgumentException>(() => Satoshi.FromBitcoin(0.000000001m));
    }

    [Fact]
    public void Sums_add_up_exactly()
    {
        var sum = Satoshi.FromBitcoin(0.1m) + Satoshi.FromBitcoin(0.2m);

        Assert.Equal(Satoshi.FromBitcoin(0.3m), sum);
        Assert.Equal(0.3m, sum.ToBitcoin());
    }

    [Fact]
    public void ToString_writes_eight_decimals()
    {
        Assert.Equal("0.51200000", Satoshi.FromBitcoin(0.512m).ToString());
    }
}
