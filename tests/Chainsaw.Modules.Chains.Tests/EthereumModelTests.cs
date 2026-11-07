using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// Ethereum's model: Keccak-256 against an independent implementation (<c>Ethereum/keccak.py</c>), EIP-55 against the
/// EIP's own examples, the known tokens by contract, and how a page of history is read and paged.
/// </summary>
public sealed class EthereumModelTests
{
    [Theory]
    [InlineData("", "c5d2460186f7233c927e7db2dcc703c0e500b653ca82273b7bfad8045d85a470")]
    [InlineData("abc", "4e03657aea45a94fc7d47ba826c8d667c0d1e6e33a64a036ec44f58fa12d6c45")]
    [InlineData("Transfer(address,address,uint256)",
        "ddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef")]
    public void KeccakMatchesTheIndependentImplementation(string text, string hex) =>
        Assert.Equal(hex, Convert.ToHexStringLower(Keccak256.Hash(text)));

    [Theory]
    [InlineData('a', 300, "5b7e0e47a96f32a88b4f14ca177982790807c40e1a105742ba0fc1babe1ef826")]
    [InlineData('b', 135, "4cc4e6a6deebdec4c9c6d68f91082ef4e5c608215f017742d4d90cdc77860650")]
    [InlineData('c', 136, "b52cbecb78a65761dd3965636d65784ba8d2b3687f6f27e48680dba106239f16")]
    public void KeccakPadsAtTheBlocksEdges(char letter, int count, string hex) =>
        Assert.Equal(hex, Convert.ToHexStringLower(Keccak256.Hash(Encoding.ASCII.GetBytes(new string(letter, count)))));

    [Theory]
    [InlineData("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed")]
    [InlineData("0xfB6916095ca1df60bB79Ce92cE3Ea74c37c5d359")]
    [InlineData("0xdbF03B407c01E7cD3CBea99509d93f8DDDC8C6FB")]
    [InlineData("0xD1220A0cf47c7B9Be7A2E6BA89F429762e7b9aDb")]
    public void AnAddressWithItsChecksumIsRead(string text)
    {
        Assert.True(EthereumAddress.TryParse(text, out var address));
        Assert.Equal(text, address.Checksummed);
        Assert.Equal(text.ToLowerInvariant(), address.Value);
        Assert.Equal(new ChainAddress(Chain.Ethereum, text), address.ToChainAddress());
    }

    [Fact]
    public void AWrongChecksumIsRefusedButNoChecksumIsTaken()
    {
        Assert.False(EthereumAddress.TryParse("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAeD", out _, out var problem));
        Assert.Contains("EIP-55", problem, StringComparison.Ordinal);
        Assert.True(EthereumAddress.TryParse("0x5aaeb6053f3e94c9b9a09f33669435e7ef1beaed", out var lower));
        Assert.True(EthereumAddress.TryParse("0x5AAEB6053F3E94C9B9A09F33669435E7EF1BEAED", out var upper));
        Assert.Equal(lower, upper);
        Assert.Equal("0x5aAeb6053F3E94C9b9A09f33669435E7Ef1BeAed", lower.ToString());
        Assert.False(EthereumAddress.TryParse("0x5aaeb6053f3e94c9b9a09f33669435e7ef1beae", out _));
        Assert.False(EthereumAddress.TryParse("5aaeb6053f3e94c9b9a09f33669435e7ef1beaed", out _));
    }

    [Fact]
    public void AHashIsKeptInLowerCase()
    {
        Assert.True(EthereumTxHash.TryParse(" 0x" + Fixtures.Repeat("AB", 32) + " ", out var hash));
        Assert.Equal("0x" + Fixtures.Repeat("ab", 32), hash.Value);
        Assert.False(EthereumTxHash.TryParse(Fixtures.Repeat("ab", 32), out _));
    }

    [Fact]
    public void UsdtAndUsdcAreKnownByContractNeverBySymbol()
    {
        var spelled = Asset.Token(Chain.Ethereum, KnownTokens.EthereumUsdt.Contract!.ToUpperInvariant(), "Tether", 6);
        Assert.Equal("Tether", KnownTokens.IssuerOf(spelled)!.Name);
        Assert.Equal("USDT", KnownTokens.Canonical(spelled).Symbol);
        Assert.Equal("Circle", KnownTokens.IssuerOf(KnownTokens.EthereumUsdc)!.Name);
        var impostor = Asset.Token(Chain.Ethereum, "0x" + Fixtures.Repeat("99", 20), "USDT", 6);
        Assert.Null(KnownTokens.IssuerOf(impostor));
        Assert.Null(KnownTokens.GlyphOf(impostor));
        Assert.Equal("₮", KnownTokens.GlyphOf(KnownTokens.EthereumUsdt));
        Assert.Equal("Ξ", KnownTokens.GlyphOf(Asset.Ether));
        Assert.Null(KnownTokens.IssuerOf(Asset.Ether));
    }

    [Fact]
    public void AFullPageGoesOnFromItsOldestBlock()
    {
        var page = BlockscoutParser.ParseHistory(
            Fixtures.Bytes("../Ethereum/history-full.json"), EthereumHarness.A, EthereumHistory.Transactions, null, 2);
        Assert.Equal(899, page.NextAtOrBelow);
        Assert.False(page.IsCutShort);
        var partial = BlockscoutParser.ParseHistory(
            Fixtures.Bytes("../Ethereum/history-full.json"), EthereumHarness.A, EthereumHistory.Transactions, null, 3);
        Assert.Null(partial.NextAtOrBelow);
    }

    [Fact]
    public void AFullPageInOneBlockIsCutShort()
    {
        var page = BlockscoutParser.ParseHistory(Fixtures.Bytes("../Ethereum/history-one-block.json"),
            EthereumHarness.A, EthereumHistory.Transactions, null, 2);
        Assert.True(page.IsCutShort);
        Assert.Null(page.NextAtOrBelow);
    }

    [Fact]
    public void AnEntryAboveThePagesBlockIsRefused() =>
        Assert.Throws<FormatException>(() => BlockscoutParser.ParseHistory(
            Fixtures.Bytes("../Ethereum/history-full.json"), EthereumHarness.A, EthereumHistory.Transactions, 899,
            100));

    [Fact]
    public void ARateLimitIsNotAnEmptyHistory() =>
        Assert.Throws<FormatException>(() => BlockscoutParser.ParseHistory(
            Fixtures.Bytes("../Ethereum/rate-limit.json"), EthereumHarness.A, EthereumHistory.Tokens, null, 100));

    [Fact]
    public void TheTransferTopicIsKeccakOfTheEvent() =>
        Assert.Equal("0xddf252ad1be2c89b69c2b068fc378daa952ba7f163c4a11628f55a4df523b3ef", BlockscoutParser.TransferTopic);
}
