using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// <em>Ersatt av vilken tx</em> (sketch GR9a): which transaction spends the coins of a transaction the source no longer
/// knows, asked coin by coin and sealed, against a synthetic facit: replace-by-fee (one replacer spends every coin), a
/// double spend of one coin while the other is free again, and a transaction that leaves the mempool with nothing in
/// its place.
/// </summary>
public sealed class ReplacementTests : IDisposable
{
    /// <summary>The first coin <see cref="Fixtures.Confirmed"/> spends (<c>b1…:0</c>).</summary>
    private static readonly Txid FirstPrevious = Txid.Parse(Fixtures.Repeat("b1", 32));

    /// <summary>The replacer in the facit.</summary>
    private static readonly Txid Replacer = Txid.Parse(Fixtures.Repeat("c1", 32));

    private readonly SourceHarness _harness = new();

    public ReplacementTests() => _harness.AnswerAll();

    private IBitcoinSource Source => _harness.Source;

    private FakeExitHttp Http => _harness.Http;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task Replace_by_fee_names_the_replacer_of_every_coin_and_seals_each_answer()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        AnswerSpend(FirstPrevious, 0, Replacer, 0);
        AnswerSpend(Fixtures.Older, 1, Replacer, 1);
        var rows = _harness.RawRows().Count;

        var lookup = await Source.GetReplacementAsync(_harness.Case, Fixtures.Confirmed, LookupMode.AsNeeded);

        var replacement = lookup.Value!;
        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Equal(ReplacementKind.Replaced, replacement.Kind);
        Assert.Equal([Replacer], replacement.Replacers);
        Assert.Equal(1, replacement.SpenderOf(Fixtures.Older, 1)!.InputIndex);
        Assert.All(replacement.Coins, coin => Assert.NotNull(coin.Sequence));
        Assert.Equal(rows + 2, _harness.RawRows().Count);
        Assert.Equal(2, lookup.Origins.Count);
    }

    [Fact]
    public async Task A_double_spend_of_one_coin_is_partly_replaced_when_the_other_is_free_again()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        AnswerSpend(FirstPrevious, 0, Replacer, 0);
        AnswerUnspent(Fixtures.Older, 1);

        var replacement = (await Source.GetReplacementAsync(
            _harness.Case, Fixtures.Confirmed, LookupMode.AsNeeded)).Value!;

        Assert.Equal(ReplacementKind.PartlyReplaced, replacement.Kind);
        Assert.Equal([Replacer], replacement.Replacers);
        Assert.Null(replacement.SpenderOf(Fixtures.Older, 1));
    }

    [Fact]
    public async Task A_transaction_that_leaves_the_mempool_without_a_replacer_is_dropped()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        AnswerUnspent(FirstPrevious, 0);
        AnswerUnspent(Fixtures.Older, 1);

        var replacement = (await Source.GetReplacementAsync(
            _harness.Case, Fixtures.Confirmed, LookupMode.AsNeeded)).Value!;

        Assert.Equal(ReplacementKind.Dropped, replacement.Kind);
        Assert.Empty(replacement.Replacers);
    }

    [Fact]
    public async Task Reading_from_the_case_sends_nothing_and_says_what_was_not_asked()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        Http.Sent.Clear();

        var before = (await Source.GetReplacementAsync(_harness.Case, Fixtures.Confirmed)).Value!;
        AnswerSpend(FirstPrevious, 0, Replacer, 0);
        AnswerSpend(Fixtures.Older, 1, Replacer, 1);
        await Source.GetReplacementAsync(_harness.Case, Fixtures.Confirmed, LookupMode.AsNeeded);
        var sent = Http.Sent.Count;
        var after = (await Source.GetReplacementAsync(_harness.Case, Fixtures.Confirmed)).Value!;

        Assert.Equal(ReplacementKind.Unknown, before.Kind);
        Assert.All(before.Coins, coin => Assert.Equal(ReplacedCoinState.NotAsked, coin.State));
        Assert.Equal(2, sent);
        Assert.Equal(sent, Http.Sent.Count);
        Assert.Equal(ReplacementKind.Replaced, after.Kind);
    }

    [Fact]
    public async Task A_known_replacer_is_not_asked_about_again_but_a_free_coin_is()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        AnswerSpend(FirstPrevious, 0, Replacer, 0);
        AnswerUnspent(Fixtures.Older, 1);
        await Source.GetReplacementAsync(_harness.Case, Fixtures.Confirmed, LookupMode.AsNeeded);
        Http.Sent.Clear();

        await Source.GetReplacementAsync(_harness.Case, Fixtures.Confirmed, LookupMode.AsNeeded);

        var asked = Assert.Single(Http.Sent);
        Assert.EndsWith($"tx/{Fixtures.Older}/outspend/1", asked.Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_answers_never_change_what_the_graph_reads_about_spends()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        AnswerSpend(FirstPrevious, 0, Replacer, 0);
        AnswerSpend(Fixtures.Older, 1, Replacer, 1);

        await Source.GetReplacementAsync(_harness.Case, Fixtures.Confirmed, LookupMode.AsNeeded);

        var spends = await Source.GetOutputSpendsAsync(_harness.Case, Fixtures.Older, LookupMode.CaseOnly);
        Assert.Null(spends.Value);
    }

    [Fact]
    public async Task A_source_that_still_names_the_gone_transaction_conflicts()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        AnswerSpend(FirstPrevious, 0, Fixtures.Confirmed, 0);
        AnswerSpend(Fixtures.Older, 1, Replacer, 1);

        var replacement = (await Source.GetReplacementAsync(
            _harness.Case, Fixtures.Confirmed, LookupMode.AsNeeded)).Value!;

        Assert.Equal(ReplacementKind.Conflicting, replacement.Kind);
    }

    [Fact]
    public async Task A_transaction_the_case_does_not_hold_is_not_asked_about()
    {
        var lookup = await Source.GetReplacementAsync(_harness.Case, Replacer, LookupMode.AsNeeded);

        Assert.Equal(LookupStatus.NotInCase, lookup.Status);
        Assert.Empty(Http.Sent);
    }

    /// <summary>Answers that a coin is spent by an input of another transaction, unconfirmed.</summary>
    private void AnswerSpend(Txid previous, int index, Txid spender, int input) => Http.AnswerBytes(
        Fixtures.Url($"tx/{previous}/outspend/{index}"),
        Encoding.UTF8.GetBytes(
            $$$"""{"spent":true,"txid":"{{{spender}}}","vin":{{{input}}},"status":{"confirmed":false}}"""),
        200);

    /// <summary>Answers that a coin is unspent.</summary>
    private void AnswerUnspent(Txid previous, int index) => Http.AnswerBytes(
        Fixtures.Url($"tx/{previous}/outspend/{index}"), Encoding.UTF8.GetBytes("""{"spent":false}"""), 200);
}
