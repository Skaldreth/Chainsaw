using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// The Solana source against synthetic JSON-RPC answers: movements read from the balances (SOL, a known token between
/// owners, a token created), a failed transaction, a transaction whose balances cannot be paired, the history by slot,
/// the newest slot, the balance, sealing with the endpoint and never asking again for what is finalized.
/// </summary>
public sealed class SolanaSourceTests
{
    [Fact]
    public async Task SolSentIsReadFromTheBalancesWithTheFeeApart()
    {
        using var harness = new SolanaHarness();
        harness.AnswerTransaction(SolanaHarness.S1, "tx-s1.json");

        var lookup = await harness.Source.GetTransactionAsync(harness.Case, SolanaHarness.S1);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        var tx = lookup.Value!;
        Assert.True(tx.IsComplete);
        Assert.True(tx.Succeeded);
        Assert.Equal(SolanaHarness.A, tx.From);
        Assert.Null(tx.To);
        Assert.Equal(new AssetAmount(Asset.Sol, 5000), tx.Fee);
        Assert.Equal(SolanaHarness.Slot, tx.Block!.Height);
        Assert.Equal(SolanaHarness.Key(0x99).Value, tx.Block.Hash);
        Assert.Equal(12, tx.Block.TransactionIndex);
        var sol = Assert.Single(tx.Transfers);
        Assert.Equal(EthereumTransferKind.Native, sol.Kind);
        Assert.Equal((SolanaHarness.A, SolanaHarness.B), (sol.From, sol.To));
        Assert.Equal(new AssetAmount(Asset.Sol, 1_500_000_000), sol.Amount);
        Assert.All(harness.Rows(), row =>
        {
            Assert.Equal("solana-mainnet", row.Chain);
            Assert.Equal("solana-rpc", row.Source);
        });
        Assert.All(harness.Http.Sent, request => Assert.Equal(ExitMethod.Post, request.Method));
        Assert.All(
            harness.Calls(), call => Assert.Contains("\"commitment\":\"finalized\"", call, StringComparison.Ordinal));
        Assert.True(harness.Verify().IsIntact);
    }

    [Fact]
    public async Task UsdtMovesBetweenTheOwnersOfItsTokenAccounts()
    {
        using var harness = new SolanaHarness();
        harness.AnswerTransaction(SolanaHarness.S2, "tx-s2.json");
        harness.Answer(SolanaRpcApi.BlockCall(SolanaHarness.Slot + 10), "block.json");

        var tx = (await harness.Source.GetTransactionAsync(harness.Case, SolanaHarness.S2)).Value!;

        var usdt = Assert.Single(tx.Transfers);
        Assert.Equal(EthereumTransferKind.Token, usdt.Kind);
        Assert.Equal(KnownTokens.SolanaUsdt, usdt.Amount.Asset);
        Assert.Equal("USDT", usdt.Amount.Asset.Symbol);
        Assert.Equal("250.000000", usdt.Amount.ToDecimalString());
        Assert.Equal((SolanaHarness.A, SolanaHarness.B), (usdt.From, usdt.To));
        Assert.Equal("Tether", KnownTokens.IssuerOf(usdt.Amount.Asset)?.Name);
    }

    [Fact]
    public async Task AFailedTransactionMovesNothingButCostsItsFee()
    {
        using var harness = new SolanaHarness();
        harness.Answer(SolanaRpcApi.TransactionCall(SolanaHarness.S3), "tx-s3.json");
        harness.Answer(SolanaRpcApi.BlockCall(SolanaHarness.Slot + 20), "block.json");

        var tx = (await harness.Source.GetTransactionAsync(harness.Case, SolanaHarness.S3)).Value!;

        Assert.Equal(EthereumOutcome.Failed, tx.Outcome);
        Assert.Empty(tx.Transfers);
        Assert.Equal(new AssetAmount(Asset.Sol, 5000), tx.Fee);
    }

    [Fact]
    public async Task SeveralPayersAndSeveralPayeesAreNotGuessed()
    {
        using var harness = new SolanaHarness();
        harness.Answer(SolanaRpcApi.TransactionCall(SolanaHarness.S4), "tx-s4.json");
        harness.Answer(SolanaRpcApi.BlockCall(SolanaHarness.Slot + 30), "block.json");

        var lookup = await harness.Source.GetTransactionAsync(harness.Case, SolanaHarness.S4);

        Assert.False(lookup.Value!.IsComplete);
        Assert.Empty(lookup.Value.Transfers);
        Assert.Contains(lookup.Notes, note => note.Contains("flera avsändare", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ATokenCreatedComesFromItsMintAndRentIsSol()
    {
        using var harness = new SolanaHarness();
        harness.Answer(SolanaRpcApi.TransactionCall(SolanaHarness.S5), "tx-s5.json");
        harness.Answer(SolanaRpcApi.BlockCall(SolanaHarness.Slot + 40), "block.json");

        var tx = (await harness.Source.GetTransactionAsync(harness.Case, SolanaHarness.S5)).Value!;

        Assert.True(tx.IsComplete);
        Assert.Collection(
            tx.Transfers,
            rent =>
            {
                Assert.Equal((SolanaHarness.A, SolanaHarness.TokenAccountB), (rent.From, rent.To));
                Assert.Equal(new AssetAmount(Asset.Sol, 2_039_280), rent.Amount);
                Assert.Equal(0, rent.Position);
            },
            created =>
            {
                Assert.Equal((SolanaHarness.Foo, SolanaHarness.B), (created.From, created.To));
                Assert.Equal(SolanaHarness.Foo.Value, created.Amount.Asset.Contract);
                Assert.False(KnownTokens.IsKnown(created.Amount.Asset));
                Assert.Equal(1, created.Position);
            });
    }

    [Fact]
    public async Task WrappedSolOutOfAPoolComesFromThePoolsOwner()
    {
        using var harness = new SolanaHarness();
        var swap = SolanaHarness.Signature(0xA6);
        harness.Answer(SolanaRpcApi.TransactionCall(swap), "tx-s6.json");
        harness.Answer(SolanaRpcApi.BlockCall(SolanaHarness.Slot + 50), "block.json");

        var tx = (await harness.Source.GetTransactionAsync(harness.Case, swap)).Value!;

        Assert.True(tx.IsComplete);
        Assert.Collection(
            tx.Transfers,
            sol =>
            {
                Assert.Equal((SolanaHarness.Pool, SolanaHarness.A), (sol.From, sol.To));
                Assert.Equal(new AssetAmount(Asset.Sol, 500_000_000), sol.Amount);
            },
            usdc =>
            {
                Assert.Equal((SolanaHarness.A, SolanaHarness.Pool), (usdc.From, usdc.To));
                Assert.Equal(new AssetAmount(KnownTokens.SolanaUsdc, 100_000_000), usdc.Amount);
            });
    }

    [Fact]
    public async Task AFinalizedTransactionIsNeverAskedForAgain()
    {
        using var harness = new SolanaHarness();
        harness.AnswerTransaction(SolanaHarness.S1, "tx-s1.json");
        await harness.Source.GetTransactionAsync(harness.Case, SolanaHarness.S1);
        var sent = harness.Http.Sent.Count;

        var again = await harness.Source.GetTransactionAsync(harness.Case, SolanaHarness.S1);

        Assert.Equal(LookupStatus.FromCase, again.Status);
        Assert.Equal(sent, harness.Http.Sent.Count);
    }

    [Fact]
    public async Task AnUnknownTransactionIsNotFound()
    {
        using var harness = new SolanaHarness();
        harness.Answer(SolanaRpcApi.TransactionCall(SolanaHarness.S1), "not-found.json");

        var lookup = await harness.Source.GetTransactionAsync(harness.Case, SolanaHarness.S1);

        Assert.Equal(LookupStatus.NotFound, lookup.Status);
        Assert.Null(lookup.Value);
    }

    [Fact]
    public async Task ARateLimitInTheBodyIsWaitedFor()
    {
        using var harness = new SolanaHarness();
        harness.Answer(SolanaRpcApi.TransactionCall(SolanaHarness.S1), "rate-limit.json");

        var lookup = await harness.Source.GetTransactionAsync(harness.Case, SolanaHarness.S1);

        Assert.Null(lookup.Value);
        Assert.Contains("vänta", lookup.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHistoryReadsEachTransactionAndPagesBackBySlot()
    {
        using var harness = new SolanaHarness(pageSize: 2);
        var a = SolanaHarness.A;
        harness.Answer(SolanaRpcApi.SignaturesCall(a, 2, null), "signatures-newest.json");
        harness.Answer(SolanaRpcApi.SignaturesCall(a, 2, SolanaHarness.S4), "signatures-older.json");
        foreach (var (hash, fixture) in new[]
        {
            (SolanaHarness.S1, "tx-s1.json"), (SolanaHarness.S2, "tx-s2.json"), (SolanaHarness.S3, "tx-s3.json"),
            (SolanaHarness.S4, "tx-s4.json"),
        })
        {
            harness.Answer(SolanaRpcApi.TransactionCall(hash), fixture);
        }

        var newest = (await harness.Source.GetAddressHistoryAsync(
            harness.Case, a, EthereumHistory.Transactions)).Value!;

        // S4 cannot be paired and S3 failed: two fees, no movement, and S4 is said.
        Assert.Empty(newest.Transfers);
        Assert.Equal(2, newest.Fees.Count);
        Assert.False(Assert.Single(newest.Fees, fee => fee.Transaction == SolanaHarness.S3).Succeeded);
        Assert.Equal(1, newest.Skipped);
        Assert.Equal(SolanaHarness.Slot + 20, newest.NextAtOrBelow);

        var older = (await harness.Source.GetAddressHistoryAsync(
            harness.Case, a, EthereumHistory.Transactions, newest.NextAtOrBelow)).Value!;

        Assert.Equal(2, older.Transfers.Count);
        Assert.Contains(older.Transfers, transfer => transfer.Amount.Asset == Asset.Sol);
        Assert.Contains(older.Transfers, transfer => transfer.Amount.Asset == KnownTokens.SolanaUsdt);
        Assert.Equal(SolanaHarness.Slot, older.NextAtOrBelow);

        Assert.Contains(harness.Calls(), call => call.Contains(SolanaHarness.S4.Value, StringComparison.Ordinal)
            && call.Contains("getSignaturesForAddress", StringComparison.Ordinal));
        Assert.True(harness.Verify().IsIntact);
    }

    [Fact]
    public async Task AnOlderPageIsNotAskedForBeforeTheNewer()
    {
        using var harness = new SolanaHarness(pageSize: 2);

        var lookup = await harness.Source.GetAddressHistoryAsync(
            harness.Case, SolanaHarness.A, EthereumHistory.Transactions, SolanaHarness.Slot + 20);

        Assert.Null(lookup.Value);
        Assert.Empty(harness.Http.Sent);
    }

    [Fact]
    public async Task TheTokenHistoryReadsTheOwnersTokenAccounts()
    {
        using var harness = new SolanaHarness();
        var a = SolanaHarness.A;
        harness.Answer(
            SolanaRpcApi.TokenAccountsCall(a, "mint", KnownTokens.SolanaUsdt.Contract!), "token-accounts.json");
        harness.Answer(
            SolanaRpcApi.TokenAccountsCall(a, "mint", KnownTokens.SolanaUsdc.Contract!), "token-accounts-none.json");
        harness.Answer(
            SolanaRpcApi.SignaturesCall(SolanaHarness.TokenAccountA, 25, null), "signatures-token-account.json");
        harness.Answer(SolanaRpcApi.TransactionCall(SolanaHarness.S2), "tx-s2.json");

        var page = (await harness.Source.GetAddressHistoryAsync(harness.Case, a, EthereumHistory.Tokens)).Value!;

        var usdt = Assert.Single(page.Transfers);
        Assert.Equal((a, SolanaHarness.B), (usdt.From, usdt.To));
        Assert.Null(page.NextAtOrBelow);
        Assert.False(page.IsIncomplete);
    }

    [Fact]
    public async Task TheTipIsTheNewestFinalizedSlotWithItsHash()
    {
        using var harness = new SolanaHarness();
        harness.Answer(SolanaRpcApi.SlotCall(), "tip.json");
        harness.Answer(SolanaRpcApi.BlockCall(SolanaHarness.Slot + 100), "block.json");

        var height = await harness.Source.GetTipHeightAsync(harness.Case);
        var tip = await harness.Source.GetTipAsync(harness.Case);

        Assert.Equal(SolanaHarness.Slot + 100, height.Value!.Height);
        Assert.Equal(SolanaHarness.Slot + 100, tip.Value!.Height);
        Assert.Equal(SolanaHarness.Key(0x99).Value, tip.Value.Hash);
    }

    [Fact]
    public async Task ASkippedSlotHasNoBlock()
    {
        using var harness = new SolanaHarness();
        harness.Answer(SolanaRpcApi.BlockCall(SolanaHarness.Slot + 1), "block-skipped.json");

        var block = await harness.Source.GetBlockAsync(harness.Case, SolanaHarness.Slot + 1);

        Assert.Equal(LookupStatus.NotFound, block.Status);
    }

    [Fact]
    public async Task TheBalanceIsSolAndTheTokensHeld()
    {
        using var harness = new SolanaHarness();
        var a = SolanaHarness.A;
        harness.Answer(SolanaRpcApi.BalanceCall(a), "balance.json");
        harness.Answer(
            SolanaRpcApi.TokenAccountsCall(a, "programId", SolanaRpcSource.TokenProgram), "token-accounts.json");

        var balance = (await harness.Source.GetBalanceAsync(harness.Case, a)).Value!;

        Assert.Equal(new AssetAmount(Asset.Sol, 3_000_000_000), balance.Ether);
        Assert.True(balance.IsComplete);
        var usdt = Assert.Single(balance.Tokens);
        Assert.Equal(new AssetAmount(KnownTokens.SolanaUsdt, 750_000_000), usdt);
    }

    [Fact]
    public async Task OfflineTheCaseAnswersAndNothingIsSent()
    {
        using var harness = new SolanaHarness();
        harness.AnswerTransaction(SolanaHarness.S1, "tx-s1.json");
        await harness.Source.GetTransactionAsync(harness.Case, SolanaHarness.S1);
        harness.Http.Connected = false;

        var offline = await harness.Source.GetTransactionAsync(harness.Case, SolanaHarness.S1);
        var unknown = await harness.Source.GetTransactionAsync(harness.Case, SolanaHarness.S2);

        Assert.Equal(LookupStatus.FromCase, offline.Status);
        Assert.Equal(LookupStatus.Offline, unknown.Status);
    }
}
