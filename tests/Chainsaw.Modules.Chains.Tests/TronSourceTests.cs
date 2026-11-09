using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// The TRON source against synthetic TronGrid responses: a transaction with its TRX, internal TRX and TRC-20 transfers,
/// fees, the histories (by time), sealing with the endpoint, never asking again for what rests on a final block, and
/// offline answers.
/// </summary>
public sealed class TronSourceTests
{
    [Fact]
    public async Task ATransactionHasItsInternalTrxAndItsUsdt()
    {
        using var harness = new TronHarness();
        harness.AnswerTransaction();

        var lookup = await harness.Source.GetTransactionAsync(harness.Case, TronHarness.C1);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        var tx = lookup.Value!;
        Assert.True(tx.Succeeded);
        Assert.Equal(TronHarness.A, tx.From);
        Assert.Equal(new AssetAmount(Asset.Tronix, 345_000), tx.Fee);
        Assert.Equal(900, tx.Block!.Height);
        Assert.Equal("0000000000000384" + Fixtures.Repeat("c9", 24), tx.Block.Hash);
        Assert.Collection(
            tx.Transfers,
            call =>
            {
                Assert.Equal(EthereumTransferKind.Internal, call.Kind);
                Assert.Equal((TronHarness.X, TronHarness.C), (call.From, call.To));
                Assert.Equal(new AssetAmount(Asset.Tronix, 2_000_000), call.Amount);
                Assert.Equal(1, call.Position);
            },
            usdt =>
            {
                Assert.Equal(EthereumTransferKind.Token, usdt.Kind);
                Assert.Equal(KnownTokens.TronUsdt, usdt.Amount.Asset);
                Assert.Equal("52000.000000", usdt.Amount.ToDecimalString());
                Assert.Equal((TronHarness.A, TronHarness.B), (usdt.From, usdt.To));
                Assert.Equal(0, usdt.Position);
            });

        // The unknown token's decimals are not in a whole transaction's answer: it is said, and not counted.
        Assert.False(tx.IsComplete);
        Assert.Contains(lookup.Notes, note => note.Contains(TronHarness.Foo.Value, StringComparison.Ordinal));
        Assert.All(harness.Rows(), row =>
        {
            Assert.Equal("tron-mainnet", row.Chain);
            Assert.Equal("trongrid", row.Source);
        });
        Assert.True(harness.Verify().IsIntact);
    }

    [Fact]
    public async Task ATransferOfTrxIsTheTransactionsOwnMovement()
    {
        using var harness = new TronHarness();
        harness.Answer(TronHarness.TxUrl(TronHarness.C2), "tx-c2.json");
        harness.Answer(TronHarness.InfoUrl(TronHarness.C2), "tx-c2-info.json");
        harness.Answer(TronHarness.BlockUrl(900), "block-900.json");

        var tx = (await harness.Source.GetTransactionAsync(harness.Case, TronHarness.C2)).Value!;

        Assert.True(tx.IsComplete);
        Assert.True(tx.Succeeded);
        Assert.True(tx.Fee.IsZero);
        var native = Assert.Single(tx.Transfers);
        Assert.Equal(EthereumTransferKind.Native, native.Kind);
        Assert.Equal((TronHarness.A, TronHarness.B), (native.From, native.To));
        Assert.Equal(new AssetAmount(Asset.Tronix, 1_000_000), native.Amount);
    }

    [Fact]
    public async Task AFailedCallMovesNothingButCostsItsFee()
    {
        using var harness = new TronHarness();
        harness.Answer(TronHarness.TxUrl(TronHarness.C4), "tx-c4.json");
        harness.Answer(TronHarness.InfoUrl(TronHarness.C4), "tx-c4-info.json");
        harness.Answer(TronHarness.BlockUrl(900), "block-900.json");

        var tx = (await harness.Source.GetTransactionAsync(harness.Case, TronHarness.C4)).Value!;

        Assert.Equal(EthereumOutcome.Failed, tx.Outcome);
        Assert.Empty(tx.Transfers);
        Assert.Equal(new AssetAmount(Asset.Tronix, 1_000_000), tx.Fee);
    }

    [Fact]
    public async Task ATransactionNotInABlockIsPendingAndSaysSo()
    {
        using var harness = new TronHarness();
        harness.Answer(TronHarness.TxUrl(TronHarness.C3), "tx-c3.json");
        harness.Answer(TronHarness.InfoUrl(TronHarness.C3), "not-found.json");

        var lookup = await harness.Source.GetTransactionAsync(harness.Case, TronHarness.C3);

        var tx = lookup.Value!;
        Assert.Equal(EthereumOutcome.Pending, tx.Outcome);
        Assert.Null(tx.Block);
        Assert.False(tx.IsComplete);
        Assert.Contains(lookup.Notes, note => note.Contains("obekräftad", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnUnknownTransactionIsNotFound()
    {
        using var harness = new TronHarness();
        harness.Answer(TronHarness.TxUrl(TronHarness.C3), "not-found.json");

        var lookup = await harness.Source.GetTransactionAsync(harness.Case, TronHarness.C3);

        Assert.Equal(LookupStatus.NotFound, lookup.Status);
        Assert.Null(lookup.Value);
    }

    [Fact]
    public async Task WhatRestsOnAFinalBlockIsNeverFetchedAgain()
    {
        using var harness = new TronHarness();
        harness.AnswerTransaction();
        await harness.Source.GetTipHeightAsync(harness.Case);
        await harness.Source.GetTransactionAsync(harness.Case, TronHarness.C1);
        var sent = harness.Http.Sent.Count;
        harness.Http.Connected = false;

        var again = await harness.Source.GetTransactionAsync(harness.Case, TronHarness.C1);

        Assert.Equal(LookupStatus.FromCase, again.Status);
        Assert.Equal(2, again.Value!.Transfers.Count);
        Assert.Equal(sent, harness.Http.Sent.Count);
        Assert.Equal(0, harness.Http.Refused);
    }

    [Fact]
    public async Task AShallowTransactionOfflineSaysItMayHaveChanged()
    {
        using var harness = new TronHarness();
        harness.AnswerTransaction();
        harness.Answer(TronHarness.TipUrl, "tip-shallow.json");
        await harness.Source.GetTipHeightAsync(harness.Case);
        await harness.Source.GetTransactionAsync(harness.Case, TronHarness.C1);
        harness.Http.Connected = false;

        var again = await harness.Source.GetTransactionAsync(harness.Case, TronHarness.C1);

        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, again.Status);
        Assert.NotNull(again.Value);
    }

    [Fact]
    public async Task ABlockWhoseIdIsNotItsNumberIsNotUsed()
    {
        using var harness = new TronHarness();
        harness.Answer(TronHarness.BlockUrl(900), "block-wrong.json");

        var lookup = await harness.Source.GetBlockAsync(harness.Case, 900);

        Assert.Null(lookup.Value);
        Assert.Single(harness.Rows());
    }

    [Fact]
    public async Task TheNewestBlockHasItsHeightHashAndTime()
    {
        using var harness = new TronHarness();
        harness.Answer(TronHarness.TipUrl, "tip.json");

        var tip = (await harness.Source.GetTipAsync(harness.Case)).Value!;

        Assert.Equal(1000, tip.Height);
        Assert.Equal("00000000000003e8" + Fixtures.Repeat("c9", 24), tip.Hash);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(TronHarness.Time + 300_000), tip.Time);
    }

    [Fact]
    public async Task TheTransactionsHistoryHasTrxInternalTrxAndFees()
    {
        using var harness = new TronHarness();
        harness.Answer(harness.HistoryUrl("transactions", TronHarness.A), "history-txs.json");

        var page = (await harness.Source.GetAddressHistoryAsync(
            harness.Case, TronHarness.A, EthereumHistory.Transactions)).Value!;

        Assert.Collection(
            page.Transfers,
            native =>
            {
                Assert.Equal((TronHarness.C2, EthereumTransferKind.Native), (native.Transaction, native.Kind));
                Assert.Equal(new AssetAmount(Asset.Tronix, 1_000_000), native.Amount);
                Assert.Equal(900, native.Block!.Height);
                Assert.Null(native.Block.Hash);
            },
            call =>
            {
                Assert.Equal(EthereumTransferKind.Internal, call.Kind);
                Assert.Equal((TronHarness.X, TronHarness.A), (call.From, call.To));
                Assert.Equal(new AssetAmount(Asset.Tronix, 3_000_000), call.Amount);
            });
        Assert.Collection(
            page.Fees,
            free => Assert.Equal((TronHarness.C2, true), (free.Transaction, free.Succeeded)),
            failed =>
            {
                Assert.Equal((TronHarness.C4, false), (failed.Transaction, failed.Succeeded));
                Assert.Equal(new AssetAmount(Asset.Tronix, 1_000_000), failed.Amount);
            },
            trc10 => Assert.Equal(TronHarness.Hash("c5"), trc10.Transaction));

        // The TRC-10 transfer is skipped, and the transaction not in a block yet moves nothing yet.
        Assert.Equal(1, page.Skipped);
        Assert.Null(page.NextAtOrBelow);
        Assert.False(page.IsCutShort);
    }

    [Fact]
    public async Task TheTokenHistoryTakesItsBlockAndPlaceFromTheTransactionsInfo()
    {
        using var harness = new TronHarness();
        harness.Answer(harness.HistoryUrl("transactions/trc20", TronHarness.A), "history-trc20.json");
        harness.Answer(TronHarness.InfoUrl(TronHarness.C1), "tx-c1-info.json");

        var lookup = await harness.Source.GetAddressHistoryAsync(harness.Case, TronHarness.A, EthereumHistory.Tokens);

        var page = lookup.Value!;
        var usdt = Assert.Single(page.Transfers);
        Assert.Equal(KnownTokens.TronUsdt, usdt.Amount.Asset);
        Assert.Equal(new BigInteger(52_000_000_000), usdt.Amount.Units);
        Assert.Equal((900L, 0L), (usdt.Block!.Height, usdt.Position));
        Assert.Equal(1, page.Skipped);
        Assert.Equal(1, page.Unread);

        // The info that placed it is sealed too, and answers the whole transaction later.
        Assert.Equal(["tron.address-trc20", "tron.tx-info"], harness.Rows().Select(row => row.Kind));
        Assert.Equal(2, lookup.Origins.Count);
    }

    [Fact]
    public async Task AnInfoTheSourceCannotGiveNowMakesThePageIncompleteNotUnread()
    {
        using var harness = new TronHarness();
        harness.Answer(harness.HistoryUrl("transactions/trc20", TronHarness.A), "history-trc20.json");
        harness.Http.AnswerBusy(TronHarness.InfoUrl(TronHarness.C1), TimeSpan.FromMinutes(10));

        var page = (await harness.Source.GetAddressHistoryAsync(
            harness.Case, TronHarness.A, EthereumHistory.Tokens)).Value!;

        // The transfer is read again later; only the entry without decimals counts as unread.
        Assert.Empty(page.Transfers);
        Assert.True(page.IsIncomplete);
        Assert.Equal(1, page.Unread);
    }

    [Fact]
    public async Task AScamTokenWithoutAReadableEventInAFinalBlockIsLeftOutAndNeverHoldsThePage()
    {
        using var harness = new TronHarness();
        AnswerScam(harness);
        harness.Answer(TronHarness.TipUrl, "tip.json");
        await harness.Source.GetTipHeightAsync(harness.Case);

        var lookup = await harness.Source.GetAddressHistoryAsync(harness.Case, TronHarness.A, EthereumHistory.Tokens);

        var page = lookup.Value!;
        Assert.False(page.IsIncomplete);
        Assert.Empty(page.Waiting);
        Assert.Equal(1, page.Unplaced);
        var usdt = Assert.Single(page.Transfers);
        Assert.Equal(KnownTokens.TronUsdt, usdt.Amount.Asset);
        Assert.Contains(lookup.Notes, note => note.Contains("ingen läsbar Transfer-händelse", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AnEventMissingFromAShallowBlocksInfoWaitsForTheSource()
    {
        using var harness = new TronHarness();
        AnswerScam(harness);
        harness.Answer(TronHarness.TipUrl, "tip-shallow.json");
        await harness.Source.GetTipHeightAsync(harness.Case);

        var page = (await harness.Source.GetAddressHistoryAsync(
            harness.Case, TronHarness.A, EthereumHistory.Tokens)).Value!;

        Assert.True(page.IsIncomplete);
        Assert.Equal([TronHarness.Hash("c8")], page.Waiting);
        Assert.Equal(0, page.Unplaced);
    }

    [Fact]
    public async Task ACheckNeverLooksUpATokenChainsawDoesNotKnow()
    {
        using var harness = new TronHarness();
        AnswerScam(harness);

        var page = (await harness.Source.GetRecentHistoryAsync(
            harness.Case, TronHarness.A, EthereumHistory.Tokens, null, LookupMode.AsNeeded,
            DateTimeOffset.MinValue)).Value!;

        Assert.False(page.IsIncomplete);
        Assert.Single(page.Transfers);
        Assert.DoesNotContain(harness.Http.Sent, request => request.Uri.AbsoluteUri
            == TronHarness.InfoUrl(TronHarness.Hash("c8")));
    }

    [Fact]
    public async Task AFinalAnswerWithoutAUsdtEventIsAskedOfAnotherEndpointAndPlacedThere()
    {
        using var harness = new TronHarness();
        await AddSecondAsync(harness);
        harness.Answer(harness.HistoryUrl("transactions/trc20", TronHarness.A), "history-trc20.json");
        harness.Answer(TronHarness.InfoUrl(TronHarness.C1), "tx-c1-info-nolog.json");
        harness.Answer(SecondInfoUrl, "tx-c1-info.json");

        var lookup = await harness.Source.GetAddressHistoryAsync(harness.Case, TronHarness.A, EthereumHistory.Tokens);

        var page = lookup.Value!;
        Assert.False(page.IsIncomplete);
        Assert.Empty(page.Disputed);
        Assert.Equal(0, page.Unplaced);
        var usdt = Assert.Single(page.Transfers);
        Assert.Equal((new BigInteger(52_000_000_000), 900L), (usdt.Amount.Units, usdt.Block!.Height));
        Assert.Contains(harness.Http.Sent, request => request.Uri.AbsoluteUri == SecondInfoUrl);
    }

    [Fact]
    public async Task AUsdtMovementAFinalAnswerLacksWaitsAndIsNeverLeftOut()
    {
        using var harness = new TronHarness();
        await AddSecondAsync(harness);
        harness.Answer(harness.HistoryUrl("transactions/trc20", TronHarness.A), "history-trc20.json");
        harness.Answer(TronHarness.InfoUrl(TronHarness.C1), "tx-c1-info-nolog.json");
        harness.Http.AnswerBusy(SecondInfoUrl, TimeSpan.FromMinutes(10));

        var lookup = await harness.Source.GetAddressHistoryAsync(harness.Case, TronHarness.A, EthereumHistory.Tokens);

        var page = lookup.Value!;
        Assert.True(page.IsIncomplete);
        Assert.Equal([TronHarness.C1], page.Waiting);
        var disputed = Assert.Single(page.Disputed);
        Assert.Equal(TronHarness.C1, disputed.Transaction);
        Assert.Equal("trongrid.test", disputed.Endpoint);
        Assert.Equal(0, page.Unplaced);
        Assert.Empty(page.Transfers);
        Assert.Contains(harness.Http.Sent, request => request.Uri.AbsoluteUri == SecondInfoUrl);
        Assert.Contains(lookup.Notes, note => note.Contains("trongrid.test", StringComparison.Ordinal)
            && note.Contains(TronHarness.C1.Brief(), StringComparison.Ordinal));
    }

    [Fact]
    public async Task WithOnlyOneEndpointADoubtedAnswerIsAskedOfItAgainAfterAWhileAndPlacedThen()
    {
        using var harness = new TronHarness();
        harness.Answer(TronHarness.TipUrl, "tip.json");
        await harness.Source.GetTipHeightAsync(harness.Case);
        harness.Answer(harness.HistoryUrl("transactions/trc20", TronHarness.A), "history-trc20.json");
        harness.Answer(TronHarness.InfoUrl(TronHarness.C1), "tx-c1-info-nolog.json");

        var first = await harness.Source.GetAddressHistoryAsync(harness.Case, TronHarness.A, EthereumHistory.Tokens);

        // The doubted answer stays in the case, but is never taken as the chain's word: the movement waits.
        var disputed = Assert.Single(first.Value!.Disputed);
        var wait = disputed.AskedAgainAt!.Value - harness.Clock.UtcNow;
        Assert.InRange(wait, TimeSpan.FromSeconds(55), TimeSpan.FromSeconds(65));
        Assert.Empty(first.Value.Transfers);
        Assert.Contains(first.Notes, note => note.Contains(
            "ingen annan källa för TRON är påslagen; frågas igen hos trongrid.test kl.", StringComparison.Ordinal));
        Assert.Equal(1, InfoAsks(harness));

        // Before the wait is over nothing is asked; after it the same endpoint is, and its new answer places it.
        harness.Answer(TronHarness.InfoUrl(TronHarness.C1), "tx-c1-info.json");
        var early = await harness.Source.GetAddressHistoryAsync(harness.Case, TronHarness.A, EthereumHistory.Tokens);
        Assert.True(early.Value!.IsIncomplete);
        Assert.Equal(1, InfoAsks(harness));

        harness.Clock.UtcNow += wait;
        var later = await harness.Source.GetAddressHistoryAsync(harness.Case, TronHarness.A, EthereumHistory.Tokens);

        Assert.False(later.Value!.IsIncomplete);
        Assert.Empty(later.Value.Disputed);
        Assert.Equal(KnownTokens.TronUsdt, Assert.Single(later.Value.Transfers).Amount.Asset);
        Assert.Equal(2, InfoAsks(harness));
        Assert.Equal(2, harness.Rows().Count(row => row.Kind == "tron.tx-info"));
    }

    [Fact]
    public async Task AnotherEndpointAtTheSameHostIsNotAnotherSource()
    {
        using var harness = new TronHarness();
        var (_, saved) = harness.Sources.Add(Chain.Tron, new Uri(TronHarness.Base + "v2/"), "trongrid", null);
        await saved;
        await harness.Sources.SetModeAsync(Chain.Tron, SourceMode.Priority);
        harness.Answer(TronHarness.TipUrl, "tip.json");
        await harness.Source.GetTipHeightAsync(harness.Case);
        harness.Answer(harness.HistoryUrl("transactions/trc20", TronHarness.A), "history-trc20.json");
        harness.Answer(TronHarness.InfoUrl(TronHarness.C1), "tx-c1-info-nolog.json");

        var page = (await harness.Source.GetAddressHistoryAsync(
            harness.Case, TronHarness.A, EthereumHistory.Tokens)).Value!;

        Assert.NotNull(Assert.Single(page.Disputed).AskedAgainAt);
        Assert.DoesNotContain(harness.Http.Sent, request => request.Uri.AbsolutePath.StartsWith(
            "/v2/", StringComparison.Ordinal) && request.Uri.AbsolutePath.Contains("info", StringComparison.Ordinal));
    }

    /// <summary>Counts the asks for C1's info.</summary>
    /// <param name="harness">The harness.</param>
    /// <returns>How many.</returns>
    private static int InfoAsks(TronHarness harness) =>
        harness.Http.Sent.Count(request => request.Uri.AbsoluteUri == TronHarness.InfoUrl(TronHarness.C1));

    [Fact]
    public async Task AnUnknownTokensMissingEventIsSkippedAndNeverAskedAgain()
    {
        using var harness = new TronHarness();
        await AddSecondAsync(harness);
        AnswerScam(harness);

        var page = (await harness.Source.GetAddressHistoryAsync(
            harness.Case, TronHarness.A, EthereumHistory.Tokens)).Value!;

        Assert.False(page.IsIncomplete);
        Assert.Equal(1, page.Unplaced);
        Assert.Empty(page.Disputed);
        Assert.DoesNotContain(harness.Http.Sent, request => request.Uri.Host == SecondHost);
    }

    /// <summary>The second endpoint's host in the tests that ask another endpoint.</summary>
    private const string SecondHost = "trongrid2.test";

    /// <summary>The second endpoint's address for C1's info.</summary>
    private static string SecondInfoUrl =>
        $"https://{SecondHost}/wallet/gettransactioninfobyid?value=" + TronHarness.C1.Value;

    /// <summary>
    /// Adds a second TronGrid endpoint after the first, in the user's order, and makes the newest block deep above
    /// block 900, so that the fixtures' block is final.
    /// </summary>
    /// <param name="harness">The harness.</param>
    private static async Task AddSecondAsync(TronHarness harness)
    {
        var (_, saved) = harness.Sources.Add(Chain.Tron, new Uri($"https://{SecondHost}/"), "trongrid", null);
        await saved;
        await harness.Sources.SetModeAsync(Chain.Tron, SourceMode.Priority);
        harness.Answer(TronHarness.TipUrl, "tip.json");
        await harness.Source.GetTipHeightAsync(harness.Case);
    }

    /// <summary>Answers the page with the scam token and the real USDT, and both transactions' info.</summary>
    /// <param name="harness">The harness.</param>
    private static void AnswerScam(TronHarness harness)
    {
        harness.Answer(harness.HistoryUrl("transactions/trc20", TronHarness.A), "history-scam.json");
        harness.Answer(TronHarness.InfoUrl(TronHarness.Hash("c8")), "tx-c8-info.json");
        harness.Answer(TronHarness.InfoUrl(TronHarness.C1), "tx-c1-info.json");
    }

    [Fact]
    public async Task ACheckReadsOnlyTheTokenEntriesItNeedsAndLooksNothingOlderUp()
    {
        using var harness = new TronHarness();
        harness.Answer(harness.HistoryUrl("transactions/trc20", TronHarness.A), "history-trc20.json");
        var later = DateTimeOffset.FromUnixTimeMilliseconds(TronHarness.Time + 1);

        var page = (await harness.Source.GetRecentHistoryAsync(
            harness.Case, TronHarness.A, EthereumHistory.Tokens, null, LookupMode.AsNeeded, later)).Value!;

        Assert.Empty(page.Transfers);
        Assert.False(page.IsIncomplete);
        Assert.DoesNotContain(harness.Http.Sent, request => request.Uri.AbsolutePath.Contains(
            "gettransactioninfobyid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFullPageInOneBlockIsReadAgainWithTheMostAPageHolds()
    {
        using var harness = new TronHarness(pageSize: 2);
        harness.Answer(harness.HistoryUrl("transactions/trc20", TronHarness.A), "history-trc20.json");
        var wide = harness.HistoryUrl("transactions/trc20", TronHarness.A).Replace(
            "limit=2", "limit=200", StringComparison.Ordinal);
        harness.Answer(wide, "history-trc20.json");
        harness.Answer(TronHarness.InfoUrl(TronHarness.C1), "tx-c1-info.json");

        var page = (await harness.Source.GetAddressHistoryAsync(
            harness.Case, TronHarness.A, EthereumHistory.Tokens)).Value!;

        Assert.False(page.IsCutShort);
        Assert.Single(page.Transfers);
        Assert.Contains(harness.Http.Sent, request => request.Uri.AbsoluteUri == wide);
    }

    [Fact]
    public async Task AResultFromAShallowBlockIsAskedForAgain()
    {
        using var harness = new TronHarness();
        harness.AnswerTransaction();
        harness.Answer(TronHarness.TipUrl, "tip-shallow.json");
        await harness.Source.GetTipHeightAsync(harness.Case);
        await harness.Source.GetTransactionAsync(harness.Case, TronHarness.C1);

        await harness.Source.GetTransactionAsync(harness.Case, TronHarness.C1);

        Assert.Equal(2, harness.Http.Sent.Count(request =>
            request.Uri.AbsoluteUri == TronHarness.TxUrl(TronHarness.C1)));
    }

    [Fact]
    public async Task ATokenPageWithAStrangersTransferIsNotUsed()
    {
        using var harness = new TronHarness();
        harness.Answer(harness.HistoryUrl("transactions/trc20", TronHarness.A), "history-stranger.json");

        var lookup = await harness.Source.GetAddressHistoryAsync(harness.Case, TronHarness.A, EthereumHistory.Tokens);

        Assert.Null(lookup.Value);
        Assert.Single(harness.Rows());
    }

    [Fact]
    public async Task TheInternalHistoryIsInTheTransactionsAndAsksNothing()
    {
        using var harness = new TronHarness();

        var page = (await harness.Source.GetAddressHistoryAsync(
            harness.Case, TronHarness.A, EthereumHistory.Internal)).Value!;

        Assert.Empty(page.Transfers);
        Assert.Null(page.NextAtOrBelow);
        Assert.Empty(harness.Http.Sent);
    }

    [Fact]
    public async Task AnOlderPageWellBehindTheSourcesClockIsNeverFetchedAgain()
    {
        using var harness = new TronHarness();
        harness.Answer(harness.HistoryUrl("transactions", TronHarness.A, TronHarness.Time), "history-txs.json");
        harness.Answer(harness.HistoryUrl("transactions", TronHarness.A), "history-txs.json");
        await harness.Source.GetAddressHistoryAsync(
            harness.Case, TronHarness.A, EthereumHistory.Transactions, TronHarness.Time);
        await harness.Source.GetAddressHistoryAsync(harness.Case, TronHarness.A, EthereumHistory.Transactions);
        var sent = harness.Http.Sent.Count;

        var older = await harness.Source.GetAddressHistoryAsync(
            harness.Case, TronHarness.A, EthereumHistory.Transactions, TronHarness.Time);
        var newest = await harness.Source.GetAddressHistoryAsync(
            harness.Case, TronHarness.A, EthereumHistory.Transactions);

        Assert.Equal(LookupStatus.FromCase, older.Status);
        Assert.Equal(LookupStatus.Fetched, newest.Status);
        Assert.Equal(sent + 1, harness.Http.Sent.Count);
    }

    [Fact]
    public async Task ABalanceHasTrxAndTheTokensChainsawKnows()
    {
        using var harness = new TronHarness();
        harness.Answer(TronHarness.Url("v1/accounts/" + TronHarness.A.Value), "account.json");

        var lookup = await harness.Source.GetBalanceAsync(harness.Case, TronHarness.A);

        var balance = lookup.Value!;
        Assert.Equal(new AssetAmount(Asset.Tronix, 5_000_000), balance.Ether);
        Assert.Equal([new AssetAmount(KnownTokens.TronUsdt, 1_000_000)], balance.Tokens);
        Assert.Equal(1, balance.Skipped);
    }

    [Fact]
    public async Task AnAddressNeverUsedHoldsNothing()
    {
        using var harness = new TronHarness();
        harness.Answer(TronHarness.Url("v1/accounts/" + TronHarness.A.Value), "account-unused.json");

        var balance = (await harness.Source.GetBalanceAsync(harness.Case, TronHarness.A)).Value!;

        Assert.True(balance.Ether.IsZero);
        Assert.Empty(balance.Tokens);
    }

    [Fact]
    public async Task OfflineWithoutAnAnswerSendsNothing()
    {
        using var harness = new TronHarness();
        harness.Http.Connected = false;

        var lookup = await harness.Source.GetTransactionAsync(harness.Case, TronHarness.C1);

        Assert.Equal(LookupStatus.Offline, lookup.Status);
        Assert.Empty(harness.Http.Sent);
    }

    [Fact]
    public async Task AnAddressOrTransactionOnAnotherChainIsRefused()
    {
        using var harness = new TronHarness();
        var ethereum = EthereumAddress.Parse("0x" + Fixtures.Repeat("11", 20));

        await Assert.ThrowsAsync<ArgumentException>(() => harness.Source.GetBalanceAsync(harness.Case, ethereum));
        await Assert.ThrowsAsync<ArgumentException>(() => harness.Source.GetTransactionAsync(
            harness.Case, EthereumTxHash.Parse("0x" + Fixtures.Repeat("c1", 32))));
    }

    [Fact]
    public void ThePagesStartAtABlocksTimeInMilliseconds()
    {
        using var harness = new TronHarness();
        var block = new EthereumBlockReference(900, null, DateTimeOffset.FromUnixTimeMilliseconds(TronHarness.Time));

        Assert.Equal(TronHarness.Time, harness.Source.PageStartOf(block));
        Assert.Equal(Chain.Tron, harness.Source.Chain);
    }

    [Fact]
    public void TronGridIsTronsEndpointAtItsOwnPaceWithItsKeyHeader()
    {
        using var harness = new TronHarness();

        var view = harness.Sources.Get(Chain.Tron);

        var endpoint = Assert.Single(view.Endpoints).Endpoint;
        Assert.Equal(("trongrid", new Uri(TronHarness.Base)), (endpoint.Api, endpoint.Address));
        Assert.Equal(TimeSpan.FromSeconds(1), endpoint.Interval);
        Assert.Equal(
            new Chainsaw.Modules.Network.Contracts.ExitCredential("TRON-PRO-API-KEY", "k"),
            new TronGridApi(new TronSourceOptions()).Credential("k"));
    }

    [Fact]
    public void AnAnswerAboutTheLimitAsksChainsawToWait()
    {
        Assert.Throws<SourceBusyException>(() => TronGridParser.ParseTip(Fixtures.Bytes("../Tron/rate-limit.json")));
    }
}
