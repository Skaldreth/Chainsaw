using System.Text;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// Bitcoin's newest block with its hash (<c>/blocks/tip/hash</c>), and responses sealed again in another case with
/// their index (Nytt ärende's lookups in advance): there they answer what cannot have changed without asking, while a
/// response brought in as evidence only (the background watching) answers nothing.
/// </summary>
public sealed class TipAndCacheTests : IDisposable
{
    /// <summary>A made-up block ten blocks above the fixtures' block 800 000.</summary>
    private static readonly BlockHash Tip = BlockHash.Parse("00000000000000000001" + Fixtures.Repeat("b0", 22));

    private readonly SourceHarness _harness = new();

    public TipAndCacheTests() => _harness.AnswerAll();

    public void Dispose() => _harness.Dispose();

    private FakeExitHttp Http => _harness.Http;

    [Fact]
    public async Task The_newest_block_comes_with_its_hash_height_and_time_from_the_block_itself()
    {
        AnswerTip();

        var lookup = await _harness.Source.GetTipAsync(_harness.Case);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Equal(
            new ChainTip(800_010, Tip.Value, DateTimeOffset.FromUnixTimeSeconds(1_700_006_000)), lookup.Value);
        Assert.Equal(
            [
                Fixtures.Url("blocks/tip/hash"), Fixtures.Url($"block/{Tip.Value}"),
                Fixtures.Url($"block/{Tip.Value}/header"),
            ],
            Http.Sent.Select(request => request.Uri.AbsoluteUri));
        Assert.Equal(lookup.Origins.Select(origin => origin.Sequence).Order(), lookup.Origins.Select(o => o.Sequence));
        Assert.Contains(lookup.Notes, note => note.Contains("huvud", StringComparison.Ordinal));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task The_newest_block_makes_what_is_fetched_after_it_deep_enough_to_be_taken_from_the_case()
    {
        AnswerTip();
        await _harness.Source.GetTipAsync(_harness.Case);
        await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var sent = Http.Sent.Count;

        var again = await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.FromCase, again.Status);
        Assert.Equal(sent, Http.Sent.Count);
    }

    [Fact]
    public async Task A_hash_that_is_not_one_fails_and_asks_for_no_block()
    {
        Http.AnswerBytes(Fixtures.Url("blocks/tip/hash"), Encoding.ASCII.GetBytes("not a hash"), 200);

        var lookup = await _harness.Source.GetTipAsync(_harness.Case);

        Assert.Null(lookup.Value);
        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Single(Http.Sent);
        Assert.Single(lookup.Origins);
    }

    [Fact]
    public async Task A_newest_block_that_cannot_be_read_fails_with_both_responses()
    {
        Http.AnswerBytes(Fixtures.Url("blocks/tip/hash"), Encoding.ASCII.GetBytes(Tip.Value + "\n"), 200);

        var lookup = await _harness.Source.GetTipAsync(_harness.Case);

        Assert.Null(lookup.Value);
        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Contains("höjd", lookup.Problem, StringComparison.Ordinal);
        Assert.Equal(2, lookup.Origins.Count);
    }

    [Fact]
    public async Task Offline_the_newest_block_comes_from_the_case_and_says_so()
    {
        AnswerTip();
        await _harness.Source.GetTipAsync(_harness.Case);
        Http.Connected = false;

        var lookup = await _harness.Source.GetTipAsync(_harness.Case);

        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, lookup.Status);
        Assert.Equal(800_010, lookup.Value!.Height);
        Assert.NotNull(lookup.Problem);
    }

    [Fact]
    public async Task A_settled_transaction_sealed_again_with_its_index_answers_without_asking()
    {
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        using var other = _harness.CreateCase("2026-0046");
        MoveAll(other, withIndex: true);
        var sent = Http.Sent.Count;

        var lookup = await _harness.Source.GetTransactionAsync(other, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.FromCase, lookup.Status);
        Assert.Equal(sent, Http.Sent.Count);
        Assert.Equal(2, lookup.Origins[0].Sequence);
        Assert.True(_harness.Verify(other).IsIntact);
    }

    [Fact]
    public async Task A_response_sealed_again_as_evidence_only_answers_nothing()
    {
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        using var other = _harness.CreateCase("2026-0047");
        MoveAll(other, withIndex: false);

        var lookup = await _harness.Source.GetTransactionAsync(other, Fixtures.Confirmed, LookupMode.CaseOnly);

        Assert.Null(lookup.Value);
        Assert.Equal(LookupStatus.NotInCase, lookup.Status);
    }

    [Fact]
    public async Task What_can_still_change_is_asked_for_again_although_it_was_sealed_with_its_index()
    {
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        await _harness.Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.WitnessKeyHash);
        using var other = _harness.CreateCase("2026-0048");
        MoveAll(other, withIndex: true);
        var sent = Http.Sent.Count;

        var page = await _harness.Source.GetAddressTransactionsAsync(other, Fixtures.WitnessKeyHash);

        Assert.Equal(LookupStatus.Fetched, page.Status);
        Assert.Equal(sent + 1, Http.Sent.Count);
    }

    [Fact]
    public async Task The_index_says_the_height_and_the_transactions_it_holds()
    {
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        await _harness.Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.WitnessKeyHash);

        var tip = _harness.Case.Read(reader => _harness.Transfer.ReadIndex(reader, 1))!;
        var page = _harness.Case.Read(reader => _harness.Transfer.ReadIndex(reader, 2))!;

        Assert.Equal(800_010, tip.Height);
        Assert.Empty(tip.Transactions);
        Assert.Null(page.Height);
        Assert.NotEmpty(page.Transactions);
        Assert.Contains(
            page.Transactions, each => each.Txid == Fixtures.Confirmed.Value && each.BlockHeight == 800_000);
        Assert.Null(_harness.Case.Read(reader => _harness.Transfer.ReadIndex(reader, 99)));
    }

    [Fact]
    public async Task A_response_that_answered_nothing_has_no_index()
    {
        Http.AnswerBytes(Fixtures.Url($"tx/{Fixtures.Confirmed}"), "{}"u8.ToArray(), 200);

        await _harness.Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Null(_harness.Case.Read(reader => _harness.Transfer.ReadIndex(reader, 1)));
    }

    [Fact]
    public async Task An_index_with_a_transaction_that_is_not_one_is_refused_and_nothing_is_sealed()
    {
        await _harness.Source.GetTipHeightAsync(_harness.Case);
        var read = _harness.Case.Read(reader => _harness.Transfer.Read(reader, 1))!;
        using var other = _harness.CreateCase("2026-0049");
        var index = new SealedResponseIndex(1, [new SealedResponseTransaction("nej", null)]);

        Assert.Throws<ArgumentException>(() => other.Write(transaction =>
            _harness.Transfer.Seal(transaction, read, index)));
        Assert.Null(other.Read(reader => _harness.Transfer.Read(reader, 1)));
    }

    /// <summary>Answers the newest block's hash and that block, ten blocks above the fixtures' block.</summary>
    private void AnswerTip()
    {
        Http.AnswerBytes(Fixtures.Url("blocks/tip/hash"), Encoding.ASCII.GetBytes(Tip.Value), 200);
        var block = Encoding.UTF8.GetString(Fixtures.Bytes("block.json"))
            .Replace(Fixtures.Block.Value, Tip.Value, StringComparison.Ordinal)
            .Replace("800000", "800010", StringComparison.Ordinal)
            .Replace("1700000000", "1700006000", StringComparison.Ordinal);
        Http.AnswerBytes(Fixtures.Url($"block/{Tip.Value}"), Encoding.UTF8.GetBytes(block), 200);
    }

    /// <summary>Seals every response of the harness's case again in another case, in order.</summary>
    /// <param name="other">The other case.</param>
    /// <param name="withIndex">Whether each goes with its index, as Nytt ärende moves them.</param>
    private void MoveAll(ICase other, bool withIndex)
    {
        var moved = _harness.Case.Read(reader => Enumerable.Range(1, 10)
            .Select(sequence => (Response: _harness.Transfer.Read(reader, sequence),
                Index: _harness.Transfer.ReadIndex(reader, sequence)))
            .Where(each => each.Response is not null)
            .ToList());
        other.Write(transaction =>
        {
            foreach (var (response, index) in moved)
            {
                if (withIndex && index is not null)
                {
                    _harness.Transfer.Seal(transaction, response!, index);
                }
                else
                {
                    _harness.Transfer.Seal(transaction, response!);
                }
            }

            return moved.Count;
        });
    }
}
