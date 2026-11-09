using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// BSC's source against the same recorded Blockscout responses as Ethereum's: every address and transaction is on
/// BSC, the chain's coin is BNB, BEP-20 USDT is known by its contract (never by its symbol) and is not freezable, the
/// responses are sealed under BSC's own name, and without an endpoint nothing is sent.
/// </summary>
public sealed class BscSourceTests
{
    [Fact]
    public async Task ATransactionOnBscIsOnBscWithItsCoinInBnb()
    {
        using var harness = new EthereumHarness(bsc: true);
        harness.AnswerTransaction();
        var hash = EthereumTxHash.Parse(Chain.Bsc, EthereumHarness.H1.Value);

        var lookup = await harness.Bsc.GetTransactionAsync(harness.Case, hash);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        var tx = lookup.Value!;
        Assert.Equal(Chain.Bsc, tx.Hash.Chain);
        Assert.Equal(Chain.Bsc, tx.From.Chain);
        Assert.Equal(new AssetAmount(Asset.Bnb, 21_000L * 1_000_000_000), tx.Fee);
        Assert.All(tx.Transfers, transfer =>
        {
            Assert.Equal(Chain.Bsc, transfer.From.Chain);
            Assert.Equal(Chain.Bsc, transfer.To.Chain);
            Assert.Equal(Chain.Bsc, transfer.Amount.Asset.Chain);
        });
        Assert.Equal(Asset.Bnb, tx.Transfers[0].Amount.Asset);

        // The fixture's Ethereum USDT contract is a stranger on BSC: shown by its contract, never as a known token.
        Assert.DoesNotContain(tx.Transfers, transfer => KnownTokens.IsKnown(transfer.Amount.Asset));
        Assert.All(harness.Rows(), row =>
        {
            Assert.Equal("bsc-mainnet", row.Chain);
            Assert.Equal("blockscout", row.Source);
        });
        Assert.True(harness.Verify().IsIntact);
    }

    [Fact]
    public async Task Bep20UsdtIsKnownByItsContractAndCannotBeFrozen()
    {
        using var harness = new EthereumHarness(bsc: true);
        var address = EthereumHarness.A.OnChain(Chain.Bsc);
        harness.Http.Answer(EthereumHarness.HistoryUrl("tokentx", address), "../Bsc/history-usdt.json");

        var lookup = await harness.Bsc.GetAddressHistoryAsync(harness.Case, address, EthereumHistory.Tokens);

        var transfers = lookup.Value!.Transfers;
        Assert.Equal(2, transfers.Count);
        var usdt = transfers[0].Amount;
        Assert.Equal(KnownTokens.BscUsdt, usdt.Asset);
        Assert.Equal(18, usdt.Asset.Decimals);
        Assert.Equal(BigInteger.Pow(10, 21), usdt.Units);
        Assert.True(KnownTokens.IsKnown(usdt.Asset));
        Assert.Null(KnownTokens.IssuerOf(usdt.Asset));
        Assert.Equal("₮", KnownTokens.GlyphOf(usdt.Asset));

        // A token that calls itself USDT under another contract stays a stranger.
        var impostor = transfers[1].Amount.Asset;
        Assert.Equal("USDT", impostor.Symbol);
        Assert.False(KnownTokens.IsKnown(impostor));
    }

    [Fact]
    public async Task TheSourceRefusesAnAddressOnAnotherChain()
    {
        using var harness = new EthereumHarness(bsc: true);

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Bsc.GetAddressHistoryAsync(
            harness.Case, EthereumHarness.A, EthereumHistory.Transactions));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            harness.Bsc.GetTransactionAsync(harness.Case, EthereumHarness.H1));
        Assert.Empty(harness.Http.Sent);
    }

    [Fact]
    public async Task WithoutAnEndpointBscSendsNothingAndSaysSo()
    {
        using var harness = new EthereumHarness();

        Assert.Contains(Chain.Bsc, harness.Sources.Chains);
        Assert.Empty(harness.Sources.Get(Chain.Bsc).Endpoints);
        var lookup = await harness.Bsc.GetTipHeightAsync(harness.Case);

        Assert.Null(lookup.Value);
        Assert.False(string.IsNullOrWhiteSpace(lookup.Problem));
        Assert.Empty(harness.Http.Sent);
    }

    [Fact]
    public async Task BscHasItsOwnBlockscoutAndIsProbedByHeight()
    {
        using var harness = new EthereumHarness(bsc: true);
        harness.Answer(EthereumHarness.Url("?module=block&action=eth_block_number"), "tip.json");

        Assert.Equal("blockscout", Assert.Single(harness.Sources.Apis(Chain.Bsc)).Code);
        Assert.Equal(ProbeRun.Probed, await harness.Sources.ProbeAsync(Chain.Bsc));
        var endpoint = harness.Sources.Get(Chain.Bsc).Endpoints.Single();
        Assert.Equal(Chain.Bsc, endpoint.Endpoint.Chain);
        Assert.Equal(1000, endpoint.Probe!.Height);
    }

    [Fact]
    public void TheSameBytesOnEthereumAndBscAreTwoAddresses()
    {
        var ethereum = EthereumHarness.A;
        var bsc = ethereum.OnChain(Chain.Bsc);

        Assert.NotEqual(ethereum, bsc);
        Assert.Equal(ethereum.Value, bsc.Value);
        Assert.NotEqual(ethereum.ToChainAddress(), bsc.ToChainAddress());
        Assert.True(EthereumAddress.TryParse(Chain.Bsc, ethereum.Checksummed, out var parsed));
        Assert.Equal(bsc, parsed);
        Assert.Throws<ArgumentException>(() => ethereum.OnChain(Chain.Tron));
    }

    [Fact]
    public void Wbnb_is_bscs_coin_wrapped_as_weth_is_ethers()
    {
        Assert.Equal(KnownTokens.BscWbnb, KnownTokens.WrappedNative(Chain.Bsc));
        Assert.Equal(KnownTokens.EthereumWeth, KnownTokens.WrappedNative(Chain.Ethereum));
        Assert.True(KnownTokens.IsWrappedNative(KnownTokens.BscWbnb));
        Assert.False(KnownTokens.IsWrappedNative(
            Asset.Token(Chain.Ethereum, KnownTokens.BscWbnb.Contract!, "WBNB", 18)));
        Assert.Null(KnownTokens.WrappedNative(Chain.Tron));
    }
}
