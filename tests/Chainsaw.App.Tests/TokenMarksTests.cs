using Chainsaw.App.Graph;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>Tokens after TK1a: the mark first in an amount, and the inspector's balance per token with Kan
/// frysas.</summary>
public sealed class TokenMarksTests
{
    [Fact]
    public void TheMarkStandsFirstForEtherAndTheStablecoinsButNotForBitcoin()
    {
        var usdt = TokenMarks.Label(new AssetAmount(KnownTokens.EthereumUsdt, 52_000_000_000));
        Assert.Equal("₮", usdt.Glyph);
        Assert.Equal("52 000,0000", usdt.Number.Replace(' ', ' '));
        Assert.EndsWith("USDT", usdt.Tip, StringComparison.Ordinal);
        Assert.Equal("Ξ", TokenMarks.Label(new AssetAmount(Asset.Ether, 1)).Glyph);
        Assert.False(TokenMarks.Label(new Satoshi(1).ToAmount()).HasGlyph);
        var impostor = Asset.Token(Chain.Ethereum, "0x" + new string('9', 40), "USDT", 6);
        Assert.False(TokenMarks.Label(new AssetAmount(impostor, 1)).HasGlyph);
    }

    [Fact]
    public void UsdtLeftAtAnAddressCanBeFrozenByTether()
    {
        var row = TokenMarks.Balance(
            new AssetAmount(KnownTokens.EthereumUsdt, 12_000_000_000), AssetAmount.Zero(KnownTokens.EthereumUsdt));

        Assert.True(row.CanBeFrozen);
        Assert.Equal("Utgivare: Tether", row.Note);
        Assert.StartsWith("Tether kan frysa 12", row.FreezeText, StringComparison.Ordinal);
        Assert.EndsWith("inte hos börsen.", row.FreezeText, StringComparison.Ordinal);
    }

    [Fact]
    public void NothingLeftOrNoIssuerMeansNoSnowflake()
    {
        var usdc = new AssetAmount(KnownTokens.EthereumUsdc, 5);
        Assert.False(TokenMarks.Balance(usdc, usdc).CanBeFrozen);
        var ether = TokenMarks.Balance(new AssetAmount(Asset.Ether, 5), AssetAmount.Zero(Asset.Ether));
        Assert.False(ether.CanBeFrozen);
        Assert.Equal(TokenMarks.NativeNote, ether.Note);
        var foo = Asset.Token(Chain.Ethereum, "0x" + new string('4', 40), "FOO", 18);
        var row = TokenMarks.Balance(new AssetAmount(foo, 5), AssetAmount.Zero(foo));
        Assert.Equal("FOO", row.Glyph);
        Assert.Equal("Kontrakt 0x4444…4444", row.Note);
    }

    [Fact]
    public void MoreOutThanInShowsNoBalanceAndNoSnowflake()
    {
        var row = TokenMarks.Balance(
            new AssetAmount(KnownTokens.EthereumUsdt, 1), new AssetAmount(KnownTokens.EthereumUsdt, 5));

        Assert.Equal("–", row.Left);
        Assert.Equal(TokenMarks.IncompleteNote, row.Note);
        Assert.False(row.CanBeFrozen);
    }

    [Fact]
    public void ARowIsOneAsset() =>
        Assert.Throws<ArgumentException>(() => TokenMarks.Balance(
            new AssetAmount(Asset.Ether, 1), AssetAmount.Zero(KnownTokens.EthereumUsdt)));
}
