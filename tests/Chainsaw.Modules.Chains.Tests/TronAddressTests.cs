using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>TRON's addresses and transaction ids: Base58Check and hex give one address, on TRON and never on Ethereum.</summary>
public sealed class TronAddressTests
{
    /// <summary>A made-up address: the version byte 0x41 and 20 bytes of 0x11.</summary>
    private static readonly string Hex = "41" + Fixtures.Repeat("11", 20);

    [Fact]
    public void Base58AndHexAreTheSameAddress()
    {
        Assert.True(EthereumAddress.TryParseTron(Hex, out var fromHex, out _));
        Assert.True(EthereumAddress.TryParseTron(fromHex.Value, out var fromBase58, out _));

        Assert.Equal(fromHex, fromBase58);
        Assert.StartsWith("T", fromHex.Value, StringComparison.Ordinal);
        Assert.Equal(34, fromHex.Value.Length);
        Assert.Equal(Chain.Tron, fromHex.Chain);
        Assert.Equal(Hex, fromHex.TronHex);
        Assert.Equal("0x" + Fixtures.Repeat("11", 20), fromHex.Hex);
        Assert.Equal(new ChainAddress(Chain.Tron, fromHex.Value), fromHex.ToChainAddress());
        Assert.Equal(fromHex, EthereumAddress.Parse(Chain.Tron, "0x" + Hex));
    }

    [Fact]
    public void UsdtsContractIsReadInBothForms()
    {
        Assert.True(EthereumAddress.TryParseTron("41a614f803b6fd780986a42c78ec9c7f77e6ded13c", out var hex, out _));

        Assert.Equal(KnownTokens.TronUsdt.Contract, hex.Value);
    }

    [Fact]
    public void AWrongCharacterBreaksTheChecksum()
    {
        Assert.True(EthereumAddress.TryParseTron(Hex, out var address, out _));
        var last = address.Value[^1];
        var wrong = address.Value[..^1] + (last == 'a' ? 'b' : 'a');

        Assert.False(EthereumAddress.TryParseTron(wrong, out _, out var problem));
        Assert.Contains("kontrollsumma", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void ABitcoinAddressInBase58IsNotATronAddress()
    {
        var bitcoin = Base58Check.Encode([0x00, .. Enumerable.Repeat((byte)0x11, 20)]);

        Assert.False(EthereumAddress.TryParseTron(bitcoin, out _, out _));
        Assert.False(EthereumAddress.TryParseTron("T" + bitcoin[1..], out _, out _));
    }

    [Fact]
    public void TheSameBytesOnTwoChainsAreTwoAddresses()
    {
        var tron = EthereumAddress.Parse(Chain.Tron, Hex);
        var ethereum = EthereumAddress.Parse("0x" + Fixtures.Repeat("11", 20));

        Assert.NotEqual(tron, ethereum);
        Assert.Equal(tron.Hex, ethereum.Hex);
        Assert.NotEqual(tron.ToChainAddress(), ethereum.ToChainAddress());
        Assert.False(EthereumAddress.TryParse(Chain.Ethereum, tron.Value, out _));
        Assert.False(EthereumAddress.TryParse(Chain.Tron, ethereum.Value, out _));
    }

    [Theory]
    [InlineData("000000000000000000000000" + "1111111111111111111111111111111111111111", true)]
    [InlineData("0x1111111111111111111111111111111111111111", true)]
    [InlineData("100000000000000000000000" + "1111111111111111111111111111111111111111", false)]
    [InlineData("11", false)]
    public void AnEventsWordHoldsAnAddress(string word, bool isAddress)
    {
        Assert.Equal(isAddress, EthereumAddress.TryFromTronWord(word, out var address));
        if (isAddress)
        {
            Assert.Equal(Chain.Tron, address!.Chain);
            Assert.Equal("41" + Fixtures.Repeat("11", 20), address.TronHex);
        }
    }

    [Fact]
    public void ATransactionIdIsSixtyFourHexDigitsWithoutPrefix()
    {
        var upper = Fixtures.Repeat("AB", 32);

        Assert.True(EthereumTxHash.TryParse(Chain.Tron, upper, out var hash));
        Assert.Equal((Chain.Tron, upper.ToLowerInvariant()), (hash.Chain, hash.Value));
        Assert.False(EthereumTxHash.TryParse(Chain.Tron, "0x" + upper, out _));
        Assert.False(EthereumTxHash.TryParse(Chain.Ethereum, upper, out _));
        Assert.NotEqual(hash, EthereumTxHash.Parse("0x" + upper));
    }

    [Fact]
    public void AStartPointInHexIsKeptInBase58()
    {
        var start = ChainStartPoint.Recognise(Chain.Tron, Hex);

        Assert.Equal(StartPointKind.Address, start.Kind);
        Assert.True(start.IsSupported);
        Assert.Equal(EthereumAddress.Parse(Chain.Tron, Hex), start.EthereumAccount);
        Assert.StartsWith("T", start.Value, StringComparison.Ordinal);
    }

    [Fact]
    public void ATransactionStartPointIsAnAccountTransaction()
    {
        var start = ChainStartPoint.Recognise(Chain.Tron, Fixtures.Repeat("c1", 32));

        Assert.Equal(EthereumTxHash.Parse(Chain.Tron, Fixtures.Repeat("c1", 32)), start.AccountTransaction);
        Assert.Null(start.EthereumAccount);
    }
}
