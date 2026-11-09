using System.Globalization;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// Sketch GR9a on Ethereum: a transaction fetched while its block was shallow, which a newer answer shows in another
/// block (another height, or another hash at the same height), or that the source no longer knows; compared as
/// Bitcoin's answers are, with the same guard against an endpoint that lags behind, and read from the case only.
/// </summary>
public sealed class EthereumBlockChangeTests : IDisposable
{
    private const string FirstHash = "0x" + "c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9c9";

    private const string Second = "https://second.example/api/";

    private const string OtherHash = "0x" + "d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7d7";

    private readonly EthereumHarness _harness = new();

    public EthereumBlockChangeTests()
    {
        _harness.AnswerTransaction();
        AnswerTip(920);
    }

    private IEthereumSource Source => _harness.Source;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_transaction_answered_alike_twice_has_no_change()
    {
        await FetchTwiceAsync();

        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));
    }

    [Fact]
    public async Task A_transaction_in_another_block_at_the_same_height_is_moved_on_ethereum()
    {
        await TipAsync(920);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        AnswerBlock(900, OtherHash);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        var rows = _harness.Rows().Count;

        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));

        Assert.Equal(Chain.Ethereum, change.Chain);
        Assert.Equal(EthereumHarness.H1.Value, change.TransactionId);
        Assert.Equal(BlockChangeKind.Moved, change.Kind);
        Assert.Equal(900, change.Before.Height);
        Assert.Equal(FirstHash, change.Before.Hash);
        Assert.Equal(900, change.Now.Height);
        Assert.Equal(OtherHash, change.Now.Hash);
        Assert.Equal(rows, _harness.Rows().Count);
    }

    [Fact]
    public async Task A_transaction_in_a_later_block_is_moved()
    {
        await TipAsync(920);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        AnswerTransactionAt(901);
        AnswerBlock(901, OtherHash);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);

        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));

        Assert.Equal(BlockChangeKind.Moved, change.Kind);
        Assert.Equal(901, change.Now.Height);
    }

    [Fact]
    public async Task A_transaction_the_source_no_longer_knows_is_gone_only_once_a_new_block_confirms_it()
    {
        await TipAsync(920);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        _harness.Answer(EthereumHarness.TxUrl(EthereumHarness.H1), "tx-unknown.json");
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);

        // The same endpoint had shown the block, then said it does not know the transaction: one of its nodes may lag
        // behind, so a single such answer is no change.
        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));

        await TipAsync(921);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));

        Assert.Equal(BlockChangeKind.Gone, change.Kind);
        Assert.Equal(900, change.Before.Height);
        Assert.Null(change.Now.Height);
        Assert.Null(change.Now.Hash);
    }

    [Fact]
    public async Task An_endpoint_that_lags_behind_and_does_not_know_the_transaction_is_passed_over()
    {
        await TipAsync(920);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        var first = _harness.Sources.Get(Chain.Ethereum).Endpoints[0].Endpoint;
        var (_, saved) = _harness.Sources.Add(Chain.Ethereum, new Uri(Second), BlockscoutApi.Code, null);
        await saved;
        await _harness.Sources.SetEnabledAsync(Chain.Ethereum, first.Id, false);
        _harness.Http.Answer(Second + "?module=transaction&action=gettxinfo&txhash=" + EthereumHarness.H1.Value,
            "../Ethereum/tx-unknown.json");

        // The second endpoint has shown no block yet, so its answers say nothing about block 900.
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));

        // Once it has shown the block's height, its answers count, when a new block confirms them.
        await TipAsync(921, Second);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));
        await TipAsync(922, Second);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        Assert.Equal(BlockChangeKind.Gone, Assert.Single(await Source.GetBlockChangesAsync(_harness.Case)).Kind);
    }

    [Fact]
    public async Task A_gone_transaction_names_its_hosts_and_rests_on_one_source_when_the_case_heard_from_one()
    {
        await TipAsync(920);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        _harness.Answer(EthereumHarness.TxUrl(EthereumHarness.H1), "tx-unknown.json");
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        await TipAsync(921);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);

        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));

        Assert.Equal(["blockscout.test"], change.GoneHosts);
        Assert.True(change.IsSingleSource);
        Assert.True(change.IsCorroborated);
        Assert.False(change.NeverInBlock);
    }

    [Fact]
    public async Task One_host_that_says_gone_while_the_case_heard_from_another_is_not_corroborated()
    {
        await TipAsync(920);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        await OnlySecondAsync();
        _harness.Http.Answer(Second + "?module=transaction&action=gettxinfo&txhash=" + EthereumHarness.H1.Value,
            "../Ethereum/tx-unknown.json");
        await TipAsync(921, Second);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        await TipAsync(922, Second);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);

        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));

        Assert.Equal(BlockChangeKind.Gone, change.Kind);
        Assert.Equal(["second.example"], change.GoneHosts);
        Assert.False(change.IsSingleSource);
        Assert.False(change.IsCorroborated);
    }

    [Fact]
    public async Task A_pending_transaction_the_source_no_longer_knows_is_gone_from_the_mempool_with_a_journal_line()
    {
        var pending = EthereumTxHash.Parse("0x" + Fixtures.Repeat("b4", 32));
        await TipAsync(920);
        _harness.Answer(EthereumHarness.TxUrl(pending), "tx-pending.json");
        await Source.GetTransactionAsync(_harness.Case, pending);
        _harness.Answer(EthereumHarness.TxUrl(pending), "tx-unknown.json");
        await Source.GetTransactionAsync(_harness.Case, pending);
        await TipAsync(921);
        await Source.GetTransactionAsync(_harness.Case, pending);
        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));

        _harness.Clock.UtcNow += TimeSpan.FromMinutes(61);
        await TipAsync(922);
        await Source.GetTransactionAsync(_harness.Case, pending);
        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));
        var gone = MempoolLines();

        _harness.Answer(EthereumHarness.TxUrl(pending), "tx-pending.json");
        await Source.GetTransactionAsync(_harness.Case, pending);

        Assert.Equal(BlockChangeKind.Gone, change.Kind);
        Assert.True(change.NeverInBlock);
        Assert.Null(change.Before.Height);
        Assert.Equal("chains.mempool-gone", Assert.Single(gone).Kind);
        Assert.Contains("Ethereum-tx", gone[0].Summary, StringComparison.Ordinal);
        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));
        Assert.Equal(["chains.mempool-gone", "chains.mempool-back"], MempoolLines().Select(line => line.Kind));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task Reading_the_changes_sends_nothing()
    {
        await FetchTwiceAsync();
        var sent = _harness.Http.Sent.Count;
        _harness.Http.Connected = false;

        await Source.GetBlockChangesAsync(_harness.Case);

        Assert.Equal(sent, _harness.Http.Sent.Count);
    }

    /// <summary>The journal's lines about the mempool rule, oldest first.</summary>
    private List<Cases.Contracts.JournalEntry> MempoolLines() =>
        [.. _harness.Case.ReadJournal().Where(line => line.Kind.StartsWith("chains.mempool", StringComparison.Ordinal))];

    /// <summary>Adds a second endpoint and leaves only it switched on.</summary>
    private async Task OnlySecondAsync()
    {
        var first = _harness.Sources.Get(Chain.Ethereum).Endpoints[0].Endpoint;
        var (_, saved) = _harness.Sources.Add(Chain.Ethereum, new Uri(Second), BlockscoutApi.Code, null);
        await saved;
        await _harness.Sources.SetEnabledAsync(Chain.Ethereum, first.Id, false);
    }

    /// <summary>Fetches the transaction twice while its block is shallow, so that it is asked again.</summary>
    private async Task FetchTwiceAsync()
    {
        await TipAsync(920);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
        await Source.GetTransactionAsync(_harness.Case, EthereumHarness.H1);
    }

    /// <summary>Asks the chain's height, answered as a given block by an endpoint.</summary>
    private async Task TipAsync(long height, string endpoint = EthereumHarness.Base)
    {
        AnswerTip(height, endpoint);
        await Source.GetTipHeightAsync(_harness.Case);
    }

    /// <summary>Answers the chain's height as a given block.</summary>
    private void AnswerTip(long height, string endpoint = EthereumHarness.Base) => _harness.Http.AnswerBytes(
        endpoint + "?module=block&action=eth_block_number",
        Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture, $"{{\"jsonrpc\":\"2.0\",\"result\":\"0x{height:x}\",\"id\":1}}")),
        200);

    /// <summary>Answers the block at a height with a hash.</summary>
    private void AnswerBlock(long height, string hash) => _harness.Http.AnswerBytes(
        EthereumHarness.Url(string.Create(CultureInfo.InvariantCulture, $"v2/blocks/{height}")),
        Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture,
            $"{{\"hash\":\"{hash}\",\"height\":{height},\"timestamp\":\"2026-11-01T10:00:12.000000Z\"}}")),
        200);

    /// <summary>Answers the transaction as being in another block.</summary>
    private void AnswerTransactionAt(long height) => _harness.Http.AnswerBytes(
        EthereumHarness.TxUrl(EthereumHarness.H1),
        Encoding.UTF8.GetBytes(Fixtures.Text("../Ethereum/tx-b1.json").Replace(
            "\"blockNumber\": \"900\"",
            string.Create(CultureInfo.InvariantCulture, $"\"blockNumber\": \"{height}\""),
            StringComparison.Ordinal)),
        200);
}
