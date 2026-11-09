using System.Globalization;
using System.Text;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// A key for the rule <c>mempool-gone v1</c> (sketch GR9a): a transaction that never was in a block and that the source
/// no longer knows counts as gone only after two confirming answers, an hour and a new block after it was last seen,
/// from an endpoint that had come as far; it is sealed with a line in the journal, and taken back, with another line,
/// as soon as a source has it again.
/// </summary>
public sealed class MempoolGoneTests : IDisposable
{
    private const string Second = "https://second.example/api/";

    private readonly SourceHarness _harness = new();

    public MempoolGoneTests()
    {
        _harness.AnswerAll();
        _harness.AnswerReal();
    }

    private IBitcoinSource Source => _harness.Source;

    private FakeExitHttp Http => _harness.Http;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Answers_within_the_grace_time_leave_a_mempool_transaction_as_it_is()
    {
        await SeenThenNotKnownTwiceAsync();

        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));
        Assert.Empty(MempoolLines());
    }

    [Fact]
    public async Task After_the_grace_time_and_a_new_block_it_is_gone_with_a_line_in_the_journal()
    {
        await SeenThenNotKnownTwiceAsync();
        _harness.Clock.UtcNow += TimeSpan.FromMinutes(61);
        await TipAsync(Fixtures.Base, 800_003);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);

        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));

        Assert.Equal(BlockChangeKind.Gone, change.Kind);
        Assert.Equal(Fixtures.Unconfirmed.Value, change.TransactionId);
        Assert.True(change.NeverInBlock);
        Assert.Null(change.Before.Height);
        Assert.Null(change.Now.Height);
        Assert.True(change.Now.Sequence > change.Before.Sequence);
        Assert.True(change.IsCorroborated);
        var line = Assert.Single(MempoolLines());
        Assert.Equal("chains.mempool-gone", line.Kind);
        Assert.Contains(Fixtures.Unconfirmed.Value, line.Summary, StringComparison.Ordinal);
        Assert.Contains("mempool-gone v1", line.Details, StringComparison.Ordinal);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task Asking_again_while_it_is_gone_seals_it_once()
    {
        await GoneAsync();
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);

        Assert.Single(MempoolLines());
        Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));
    }

    [Fact]
    public async Task It_is_taken_back_with_a_line_in_the_journal_when_the_source_knows_it_again()
    {
        await GoneAsync();
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Unconfirmed}"), "tx-unconfirmed.json");

        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);

        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));
        Assert.Equal(["chains.mempool-gone", "chains.mempool-back"], MempoolLines().Select(line => line.Kind));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task Gone_again_after_coming_back_is_a_new_record()
    {
        await GoneAsync();
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Unconfirmed}"), "tx-unconfirmed.json");
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        NotKnown();
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        await TipAsync(Fixtures.Base, 800_004);
        _harness.Clock.UtcNow += TimeSpan.FromMinutes(61);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);

        Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));
        Assert.Equal(
            ["chains.mempool-gone", "chains.mempool-back", "chains.mempool-gone"],
            MempoolLines().Select(line => line.Kind));
    }

    [Fact]
    public async Task Without_a_new_block_the_chain_has_not_gone_on_without_it()
    {
        await TipAsync(Fixtures.Base, 800_001);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        NotKnown();
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        _harness.Clock.UtcNow += TimeSpan.FromMinutes(61);

        // Another endpoint confirms the first, but no block came after the transaction was last seen.
        await OtherEndpointOnlyAsync(800_001);
        Http.AnswerBytes(Second + $"tx/{Fixtures.Unconfirmed}", Encoding.UTF8.GetBytes("Transaction not found"), 404);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);

        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));
        Assert.Empty(MempoolLines());
    }

    [Fact]
    public async Task An_endpoint_that_lags_behind_the_case_does_not_count()
    {
        await TipAsync(Fixtures.Base, 800_001);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        _harness.Clock.UtcNow += TimeSpan.FromMinutes(61);

        // The second endpoint has shown only an older block than the case knew when it last saw the transaction, so
        // its first answer says nothing; counted, it and the next would settle it, a block apart.
        await OtherEndpointOnlyAsync(799_990);
        Http.AnswerBytes(Second + $"tx/{Fixtures.Unconfirmed}", Encoding.UTF8.GetBytes("Transaction not found"), 404);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        await TipAsync(Second, 800_002);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));
        Assert.Empty(MempoolLines());

        // Once it is in step, two of its answers a block apart do.
        await TipAsync(Second, 800_003);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        Assert.True(Assert.Single(await Source.GetBlockChangesAsync(_harness.Case)).NeverInBlock);
        Assert.Single(MempoolLines());
    }

    [Fact]
    public async Task A_transaction_that_was_in_a_block_is_left_to_the_block_rule()
    {
        await TipAsync(Fixtures.Base, 800_001);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        Http.AnswerBytes(Fixtures.Url($"tx/{Fixtures.Confirmed}"), Encoding.UTF8.GetBytes("Transaction not found"), 404);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        await TipAsync(Fixtures.Base, 800_002);
        _harness.Clock.UtcNow += TimeSpan.FromMinutes(61);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));

        Assert.Equal(BlockChangeKind.Gone, change.Kind);
        Assert.False(change.NeverInBlock);
        Assert.Empty(MempoolLines());
    }

    [Fact]
    public async Task Reading_the_changes_writes_and_sends_nothing()
    {
        await GoneAsync();
        var sent = Http.Sent.Count;
        var journal = _harness.Case.ReadJournal().Count;

        await Source.GetBlockChangesAsync(_harness.Case);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed, LookupMode.CaseOnly);

        Assert.Equal(sent, Http.Sent.Count);
        Assert.Equal(journal, _harness.Case.ReadJournal().Count);
    }

    /// <summary>
    /// Seals the transaction unconfirmed, then twice that the source does not know it, a block apart, at once.
    /// </summary>
    private async Task SeenThenNotKnownTwiceAsync()
    {
        await TipAsync(Fixtures.Base, 800_001);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        NotKnown();
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        await TipAsync(Fixtures.Base, 800_002);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
    }

    /// <summary>Makes the transaction gone from the mempool by the rule.</summary>
    private async Task GoneAsync()
    {
        await SeenThenNotKnownTwiceAsync();
        _harness.Clock.UtcNow += TimeSpan.FromMinutes(61);
        await TipAsync(Fixtures.Base, 800_003);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        Assert.Single(MempoolLines());
    }

    /// <summary>Answers that the first endpoint does not know the unconfirmed transaction.</summary>
    private void NotKnown() => Http.AnswerBytes(
        Fixtures.Url($"tx/{Fixtures.Unconfirmed}"), Encoding.UTF8.GetBytes("Transaction not found"), 404);

    /// <summary>The journal's lines about the mempool rule, oldest first.</summary>
    private List<Cases.Contracts.JournalEntry> MempoolLines() =>
        [.. _harness.Case.ReadJournal().Where(line => line.Kind.StartsWith("chains.mempool", StringComparison.Ordinal))];

    /// <summary>Adds a second endpoint, has it show a height, and leaves only it switched on.</summary>
    /// <param name="height">The height it shows.</param>
    private async Task OtherEndpointOnlyAsync(long height)
    {
        var first = _harness.Sources.Get(SharedKernel.Chain.Bitcoin).Endpoints[0].Endpoint;
        var (_, saved) = _harness.Sources.Add(SharedKernel.Chain.Bitcoin, new Uri(Second), "esplora", null);
        await saved;
        await _harness.Sources.SetEnabledAsync(SharedKernel.Chain.Bitcoin, first.Id, false);
        await TipAsync(Second, height);
    }

    /// <summary>Answers and fetches the chain's height from an endpoint.</summary>
    /// <param name="baseUrl">The endpoint's address.</param>
    /// <param name="height">The height.</param>
    /// <returns>A task that completes when it is sealed.</returns>
    private async Task TipAsync(string baseUrl, long height)
    {
        Http.AnswerBytes(
            baseUrl + "blocks/tip/height",
            Encoding.ASCII.GetBytes(height.ToString(CultureInfo.InvariantCulture)),
            200);
        await Source.GetTipHeightAsync(_harness.Case);
    }
}
