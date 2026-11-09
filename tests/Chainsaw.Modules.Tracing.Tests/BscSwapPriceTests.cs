using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// A swap on BSC whose BNB side has no rate (Bitstamp's BNB market is young and thin): the price is not tested, and
/// the explanation names BNB as the side without a rate rather than guessing.
/// </summary>
public sealed class BscSwapPriceTests
{
    [Fact]
    public void An_untested_swap_names_the_side_that_lacks_its_rate()
    {
        var swap = new TokenSwap(
            EthereumTxHash.Parse(Chain.Bsc, "0x" + new string('a', 64)),
            new ChainAddress(Chain.Bsc, "0x" + new string('b', 40)),
            new AssetAmount(KnownTokens.BscUsdt, BigInteger.Pow(10, 21)),
            new AssetAmount(KnownTokens.BscWbnb, BigInteger.Pow(10, 18)));
        var check = new SwapPriceCheck(swap, SwapPriceVerdict.NotTested, 10_000m, null, "SEK");

        Assert.Null(check.Gap);
        Assert.Contains("kursen för WBNB", check.Explain(), StringComparison.Ordinal);
        Assert.Contains(
            "USDT och WBNB",
            (check with { SoldValue = null }).Explain(),
            StringComparison.Ordinal);
    }
}
