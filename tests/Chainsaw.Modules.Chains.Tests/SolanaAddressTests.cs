using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// Solana's addresses and signatures (plain Base58 of 32 and 64 bytes, without a checksum), the tokens Chainsaw knows
/// on Solana by their mints, and start points recognised on Solana.
/// </summary>
public sealed class SolanaAddressTests
{
    [Fact]
    public void AnAddressIsThirtyTwoBytesInBase58()
    {
        var text = Base58.Encode(Enumerable.Repeat((byte)0x11, 32).ToArray());

        Assert.True(EthereumAddress.TryParseSolana(text, out var address, out var problem));
        Assert.Null(problem);
        Assert.Equal(Chain.Solana, address.Chain);
        Assert.Equal(text, address.Value);
        Assert.Equal("0x" + Fixtures.Repeat("11", 32), address.Hex);
        Assert.Equal(new ChainAddress(Chain.Solana, text), address.ToChainAddress());
    }

    [Theory]
    [InlineData("")]
    [InlineData("0OIl0OIl0OIl0OIl0OIl0OIl0OIl0OIl")]
    [InlineData("TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t")]
    [InlineData("0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed")]
    public void OtherTextsAreNotSolanaAddresses(string text)
    {
        Assert.False(EthereumAddress.TryParseSolana(text, out _, out var problem));
        Assert.Contains("kontrollsumma", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ANonCanonicalFormIsRefused()
    {
        // A leading 1 is a zero byte: the same 32 bytes written with one more leading zero would be 33 bytes.
        var text = "1" + Base58.Encode(Enumerable.Repeat((byte)0x11, 32).ToArray());

        Assert.False(EthereumAddress.TryParse(Chain.Solana, text, out _));
    }

    [Fact]
    public void TheSameKeyOnSolanaIsNotAnAddressElsewhere()
    {
        var text = Base58.Encode(Enumerable.Repeat((byte)0x22, 32).ToArray());

        Assert.True(EthereumAddress.TryParse(Chain.Solana, text, out _));
        Assert.False(EthereumAddress.TryParse(Chain.Tron, text, out _));
        Assert.False(EthereumAddress.TryParse(Chain.Ethereum, text, out _));
    }

    [Fact]
    public void ASignatureIsSixtyFourBytesKeptAsWritten()
    {
        var text = Base58.Encode(Enumerable.Repeat((byte)0xA1, 64).ToArray());

        Assert.True(EthereumTxHash.TryParse(Chain.Solana, text, out var hash));
        Assert.Equal(text, hash.Value);
        Assert.False(EthereumTxHash.TryParse(Chain.Solana, Fixtures.Repeat("a1", 32), out _));
        Assert.False(EthereumTxHash.TryParse(Chain.Solana, text.ToLowerInvariant(), out _));
    }

    [Fact]
    public void StartPointsAreRecognisedOnSolanaWithOrWithoutTheChain()
    {
        var address = Base58.Encode(Enumerable.Repeat((byte)0x33, 32).ToArray());
        var signature = Base58.Encode(Enumerable.Repeat((byte)0xA2, 64).ToArray());

        var bare = ChainStartPoint.Recognise(address);
        var named = ChainStartPoint.Recognise(Chain.Solana, signature);

        Assert.Equal((Chain.Solana, StartPointKind.Address), (bare.Chain!.Value, bare.Kind));
        Assert.True(bare.IsSupported);
        Assert.NotNull(bare.EthereumAccount);
        Assert.Equal((Chain.Solana, StartPointKind.Transaction), (named.Chain!.Value, named.Kind));
        Assert.Equal(signature, named.AccountTransaction!.Value);
        Assert.Equal(Chain.Solana, ChainStartPoint.Recognise(signature).Chain);
    }

    [Fact]
    public void UsdtAndUsdcOnSolanaAreKnownByTheirMintsAndCanBeFrozen()
    {
        var mint = Base58.Encode(Enumerable.Repeat((byte)0x77, 32).ToArray());
        var lookalike = Asset.Token(Chain.Solana, mint, "USDT", 6);

        Assert.True(KnownTokens.IsKnown(KnownTokens.SolanaUsdt));
        Assert.Equal("Tether", KnownTokens.IssuerOf(KnownTokens.SolanaUsdt)?.Name);
        Assert.Equal("Circle", KnownTokens.IssuerOf(KnownTokens.SolanaUsdc)?.Name);
        Assert.False(KnownTokens.IsKnown(lookalike));
        Assert.Null(KnownTokens.IssuerOf(lookalike));
        Assert.Equal(9, Asset.Sol.Decimals);
        Assert.Equal(Asset.Sol, Chain.Solana.NativeAsset());
        Assert.Equal(LedgerModel.Account, Chain.Solana.Model());
    }
}
