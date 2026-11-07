using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// The Ethereum source against recorded Blockscout responses: a transaction with every movement of value, fees,
/// histories, sealing with the endpoint, never asking again for what rests on a final block, and offline answers.
/// </summary>
public sealed class EthereumSourceTests
{
    [Fact]
    public async Task ATransactionHasItsEtherInternalAndTokenTransfers()
    {
        using var harness = new EthereumHarness();
        harness.AnswerTransaction();

        var lookup = await harness.Source.GetTransactionAsync(harness.Case, EthereumHarness.H1);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        var tx = lookup.Value!;
        Assert.True(tx.IsComplete);
        Assert.True(tx.Succeeded);
        Assert.Equal(new AssetAmount(Asset.Ether, 21_000L * 1_000_000_000), tx.Fee);
        Assert.Equal(900, tx.Block!.Height);
        Assert.Equal("0x" + Fixtures.Repeat("c9", 32), tx.Block.Hash);
        Assert.Collection(
            tx.Transfers,
            native =>
            {
                Assert.Equal(EthereumTransferKind.Native, native.Kind);
                Assert.Equal(EthereumHarness.Router, native.To);
                Assert.Equal(BigInteger.Pow(10, 18), native.Amount.Units);
            },
            call =>
            {
                Assert.Equal(EthereumTransferKind.Internal, call.Kind);
                Assert.Equal(EthereumHarness.C, call.To);
                Assert.Equal(1, call.Position);
            },
            usdt =>
            {
                Assert.Equal(KnownTokens.EthereumUsdt, usdt.Amount.Asset);
                Assert.Equal(6, usdt.Amount.Asset.Decimals);
                Assert.Equal("52000.000000", usdt.Amount.ToDecimalString());
                Assert.Equal(7, usdt.Position);
            },
            foo =>
            {
                Assert.Equal("FOO", foo.Amount.Asset.Symbol);
                Assert.Equal(18, foo.Amount.Asset.Decimals);
                Assert.Equal(EthereumHarness.Foo.Value, foo.Amount.Asset.Contract);
            });

        // USDT is known by its contract, so only the unknown token's decimals were asked for.
        Assert.DoesNotContain(harness.Http.Sent, request => request.Uri.Query.Contains(KnownTokens.EthereumUsdt.Contract!,
            StringComparison.Ordinal) && request.Uri.Query.Contains("getToken", StringComparison.Ordinal));
        Assert.All(harness.Rows(), row =>
        {
            Assert.Equal("ethereum-mainnet", row.Chain);
            Assert.Equal("blockscout", row.Source);
        });
        Assert.True(harness.Verify().IsIntact);
    }

    [Fact]
    public async Task WhatRestsOnAFinalBlockIsNeverFetchedAgain()
    {
        using var harness = new EthereumHarness();
        harness.AnswerTransaction();
        await harness.Source.GetTipHeightAsync(harness.Case);
        await harness.Source.GetTransactionAsync(harness.Case, EthereumHarness.H1);
        var sent = harness.Http.Sent.Count;
        harness.Http.Connected = false;

        var again = await harness.Source.GetTransactionAsync(harness.Case, EthereumHarness.H1);

        Assert.Equal(LookupStatus.FromCase, again.Status);
        Assert.Equal(4, again.Value!.Transfers.Count);
        Assert.Equal(sent, harness.Http.Sent.Count);
        Assert.Equal(0, harness.Http.Refused);
    }

    [Fact]
    public async Task AShallowTransactionOfflineSaysItMayHaveChanged()
    {
        using var harness = new EthereumHarness();
        harness.AnswerTransaction();
        harness.Answer(EthereumHarness.Url("?module=block&action=eth_block_number"), "tip-shallow.json");
        await harness.Source.GetTipHeightAsync(harness.Case);
        await harness.Source.GetTransactionAsync(harness.Case, EthereumHarness.H1);
        harness.Http.Connected = false;

        var again = await harness.Source.GetTransactionAsync(harness.Case, EthereumHarness.H1);

        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, again.Status);
        Assert.NotNull(again.Value);
        Assert.Contains("kan ha ändrats", again.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OfflineWithoutAnAnswerSendsNothing()
    {
        using var harness = new EthereumHarness();
        harness.Http.Connected = false;

        var lookup = await harness.Source.GetTransactionAsync(harness.Case, EthereumHarness.H1);

        Assert.Equal(LookupStatus.Offline, lookup.Status);
        Assert.Null(lookup.Value);
        Assert.Empty(harness.Http.Sent);
        Assert.Empty(harness.Rows());
    }

    [Fact]
    public async Task AFailedTransactionMovesNothingButCostsItsFee()
    {
        using var harness = new EthereumHarness();
        harness.Answer(EthereumHarness.TxUrl(EthereumHarness.H2), "tx-b2-failed.json");
        harness.Answer(EthereumHarness.Url("v2/blocks/900"), "block-900.json");

        var tx = (await harness.Source.GetTransactionAsync(harness.Case, EthereumHarness.H2)).Value!;

        Assert.False(tx.Succeeded);
        Assert.Empty(tx.Transfers);
        Assert.Equal(new AssetAmount(Asset.Ether, 60_000_000_000_000L), tx.Fee);
        Assert.DoesNotContain(harness.Http.Sent, r => r.Uri.Query.Contains("txlistinternal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATokenWhoseDecimalsCannotBeHadMakesTheTransactionIncomplete()
    {
        using var harness = new EthereumHarness();
        harness.AnswerTransaction();
        harness.Answer(EthereumHarness.Url("?module=token&action=getToken&contractaddress=" + EthereumHarness.Foo.Value),
            "rate-limit.json");

        var lookup = await harness.Source.GetTransactionAsync(harness.Case, EthereumHarness.H1);

        Assert.False(lookup.Value!.IsComplete);
        Assert.Equal(3, lookup.Value.Transfers.Count);
        Assert.Contains(lookup.Notes, note => note.Contains(EthereumHarness.Foo.Checksummed, StringComparison.Ordinal));
        Assert.Contains(harness.Rows(), row => row.Kind == "ethereum.token");
    }

    [Fact]
    public async Task AnAnswerAboutAnotherTransactionIsSealedButNotUsed()
    {
        using var harness = new EthereumHarness();
        harness.Answer(EthereumHarness.TxUrl(EthereumHarness.H1), "tx-other.json");

        var lookup = await harness.Source.GetTransactionAsync(harness.Case, EthereumHarness.H1);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Null(lookup.Value);
        Assert.Single(lookup.Origins);
        Assert.Single(harness.Rows());
    }

    [Fact]
    public async Task TheTransactionsHistoryHasEtherAndFeesPaid()
    {
        using var harness = new EthereumHarness();
        harness.Answer(EthereumHarness.HistoryUrl("txlist", EthereumHarness.A), "history-txs.json");

        var page = (await harness.Source.GetAddressHistoryAsync(
            harness.Case, EthereumHarness.A, EthereumHistory.Transactions)).Value!;

        Assert.Equal(2, page.Transfers.Count);
        Assert.Contains(page.Transfers, t => t.To == EthereumHarness.A && t.Amount.Units == 2 * BigInteger.Pow(10, 18));
        Assert.Equal(2, page.Fees.Count);
        Assert.Contains(page.Fees, fee => !fee.Succeeded && fee.Transaction == EthereumHarness.H2);
        Assert.Equal("0x" + 900.ToString("x64", System.Globalization.CultureInfo.InvariantCulture),
            page.Transfers[0].Block!.Hash);
        Assert.Null(page.NextAtOrBelow);
    }

    [Fact]
    public async Task TheTokensHistoryLeavesOutWhatHasNoDecimalsAndSaysSo()
    {
        using var harness = new EthereumHarness();
        harness.Answer(EthereumHarness.HistoryUrl("tokentx", EthereumHarness.A), "history-tokens.json");

        var page = (await harness.Source.GetAddressHistoryAsync(
            harness.Case, EthereumHarness.A, EthereumHistory.Tokens)).Value!;

        var usdt = Assert.Single(page.Transfers);
        Assert.Equal(KnownTokens.EthereumUsdt, usdt.Amount.Asset);
        Assert.True(page.IsIncomplete);
    }

    [Fact]
    public async Task UnprocessedInternalTransactionsAreIncomplete()
    {
        using var harness = new EthereumHarness();
        harness.Answer(EthereumHarness.HistoryUrl("txlistinternal", EthereumHarness.A), "history-internal.json");

        var page = (await harness.Source.GetAddressHistoryAsync(
            harness.Case, EthereumHarness.A, EthereumHistory.Internal)).Value!;

        Assert.True(page.IsIncomplete);
        Assert.Equal(EthereumTransferKind.Internal, Assert.Single(page.Transfers).Kind);
    }

    [Fact]
    public async Task AnEmptyHistoryIsAnAnswer()
    {
        using var harness = new EthereumHarness();
        harness.Answer(EthereumHarness.HistoryUrl("txlist", EthereumHarness.A), "history-empty.json");

        var lookup = await harness.Source.GetAddressHistoryAsync(
            harness.Case, EthereumHarness.A, EthereumHistory.Transactions);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Empty(lookup.Value!.Transfers);
    }

    [Fact]
    public async Task APageWithAStrangersEntryIsRefused()
    {
        using var harness = new EthereumHarness();
        harness.Answer(EthereumHarness.HistoryUrl("txlist", EthereumHarness.A), "history-stranger.json");

        var lookup = await harness.Source.GetAddressHistoryAsync(
            harness.Case, EthereumHarness.A, EthereumHistory.Transactions);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Single(harness.Rows());
    }

    [Fact]
    public async Task AnOlderPageAsksBelowABlockAndIsKeptOnceFinal()
    {
        using var harness = new EthereumHarness(pageSize: 2);
        harness.Answer(EthereumHarness.Url("?module=block&action=eth_block_number"), "tip.json");
        harness.Answer(EthereumHarness.HistoryUrl("txlist", EthereumHarness.A, 2), "history-full.json");
        harness.Answer(EthereumHarness.HistoryUrl("txlist", EthereumHarness.A, 2, 899), "history-empty.json");
        await harness.Source.GetTipHeightAsync(harness.Case);

        var first = (await harness.Source.GetAddressHistoryAsync(
            harness.Case, EthereumHarness.A, EthereumHistory.Transactions)).Value!;
        var older = await harness.Source.GetAddressHistoryAsync(
            harness.Case, EthereumHarness.A, EthereumHistory.Transactions, first.NextAtOrBelow);
        var sent = harness.Http.Sent.Count;
        var again = await harness.Source.GetAddressHistoryAsync(
            harness.Case, EthereumHarness.A, EthereumHistory.Transactions, first.NextAtOrBelow);

        Assert.Equal(LookupStatus.Fetched, older.Status);
        Assert.Equal(LookupStatus.FromCase, again.Status);
        Assert.Equal(sent, harness.Http.Sent.Count);
    }

    [Fact]
    public async Task ABlockAtAnotherHeightIsRefused()
    {
        using var harness = new EthereumHarness();
        harness.Answer(EthereumHarness.Url("v2/blocks/900"), "block-wrong.json");

        var lookup = await harness.Source.GetBlockAsync(harness.Case, 900);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
    }

    [Fact]
    public async Task EthereumHasItsSortOfApiAndIsProbedByHeight()
    {
        using var harness = new EthereumHarness();
        harness.Answer(EthereumHarness.Url("?module=block&action=eth_block_number"), "tip.json");

        Assert.Contains(Chain.Ethereum, harness.Sources.Chains);
        Assert.Equal("blockscout", Assert.Single(harness.Sources.Apis(Chain.Ethereum)).Code);
        Assert.Equal(ProbeRun.Probed, await harness.Sources.ProbeAsync(Chain.Ethereum));
        var probe = harness.Sources.Get(Chain.Ethereum).Endpoints.Single().Probe!;
        Assert.Equal(1000, probe.Height);
        Assert.Empty(harness.Rows());
    }

    [Fact]
    public async Task ChainOffSendsNothing()
    {
        using var harness = new EthereumHarness();
        await harness.Sources.SetModeAsync(Chain.Ethereum, SourceMode.Off);

        var lookup = await harness.Source.GetTipHeightAsync(harness.Case);

        Assert.Equal(LookupStatus.Offline, lookup.Status);
        Assert.Contains("Ethereum", lookup.Problem, StringComparison.Ordinal);
        Assert.Empty(harness.Http.Sent);
    }
}
