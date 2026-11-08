using System.Text;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// Sketch GR9a: a transaction that a newer answer shows in another block, unconfirmed again or no longer known, read
/// from the sealed answers only; and the newest block the case has, read without asking.
/// </summary>
public sealed class BlockChangeTests : IDisposable
{
    private const string OtherHash = "00000000000000000002b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1b1";

    private readonly SourceHarness _harness = new();

    public BlockChangeTests()
    {
        _harness.AnswerAll();
        _harness.AnswerReal();
    }

    private IBitcoinSource Source => _harness.Source;

    private FakeExitHttp Http => _harness.Http;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_case_without_changes_has_none()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));
    }

    [Fact]
    public async Task A_transaction_in_another_block_is_moved_and_both_answers_stay()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        AnswerStatus(new JsonObject
        {
            ["confirmed"] = true, ["block_height"] = 800_001, ["block_hash"] = OtherHash, ["block_time"] = 1_700_000_600,
        });
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var rows = _harness.RawRows().Count;

        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));

        Assert.Equal(BlockChangeKind.Moved, change.Kind);
        Assert.Equal(Fixtures.Confirmed, change.Txid);
        Assert.Equal(800_000, change.Before.Height);
        Assert.Equal(Fixtures.Block.Value, change.Before.Hash);
        Assert.Equal(800_001, change.Now.Height);
        Assert.Equal(OtherHash, change.Now.Hash);
        Assert.True(change.Now.Sequence > change.Before.Sequence);
        Assert.Equal(rows, _harness.RawRows().Count);
    }

    [Fact]
    public async Task A_transaction_in_another_block_at_the_same_height_is_moved()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        AnswerStatus(new JsonObject
        {
            ["confirmed"] = true, ["block_height"] = 800_000, ["block_hash"] = OtherHash, ["block_time"] = 1_700_000_000,
        });
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));

        Assert.Equal(BlockChangeKind.Moved, change.Kind);
        Assert.Equal(change.Before.Height, change.Now.Height);
    }

    [Fact]
    public async Task A_transaction_unconfirmed_again_is_back_in_the_mempool()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        AnswerStatus(new JsonObject { ["confirmed"] = false });
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));

        Assert.Equal(BlockChangeKind.Unconfirmed, change.Kind);
        Assert.Null(change.Now.Height);
        Assert.Null(change.Now.Hash);
    }

    [Fact]
    public async Task A_transaction_the_source_no_longer_knows_is_gone()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        Http.AnswerBytes(Fixtures.Url($"tx/{Fixtures.Confirmed}"), Encoding.UTF8.GetBytes("Transaction not found"), 404);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));

        Assert.Equal(BlockChangeKind.Gone, change.Kind);
        Assert.Equal(800_000, change.Before.Height);
        Assert.Null(change.Now.Hash);
    }

    [Fact]
    public async Task An_unconfirmed_transaction_that_is_confirmed_later_is_no_change()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        var confirmed = JsonNode.Parse(Fixtures.Text("tx-unconfirmed.json"))!;
        confirmed["status"] = new JsonObject
        {
            ["confirmed"] = true, ["block_height"] = 800_000, ["block_hash"] = Fixtures.Block.Value,
            ["block_time"] = 1_700_000_000,
        };
        Http.AnswerBytes(
            Fixtures.Url($"tx/{Fixtures.Unconfirmed}"), Encoding.UTF8.GetBytes(confirmed.ToJsonString()), 200);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);

        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));
    }

    [Fact]
    public async Task The_newest_block_in_the_case_is_read_without_asking()
    {
        var empty = await Source.GetTipAsync(_harness.Case, LookupMode.CaseOnly);
        Http.AnswerBytes(Fixtures.Url("blocks/tip/hash"), Encoding.ASCII.GetBytes(Fixtures.RealBlock.Value), 200);
        var fetched = await Source.GetTipAsync(_harness.Case, LookupMode.AsNeeded);
        var sent = Http.Sent.Count;

        var stored = await Source.GetTipAsync(_harness.Case, LookupMode.CaseOnly);

        Assert.Equal(LookupStatus.NotInCase, empty.Status);
        Assert.NotNull(fetched.Value);
        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, stored.Status);
        Assert.Equal(fetched.Value, stored.Value);
        Assert.Equal(sent, Http.Sent.Count);
    }

    /// <summary>Answers the confirmed fixture again with another status.</summary>
    /// <param name="status">The new status.</param>
    private void AnswerStatus(JsonObject status)
    {
        var moved = JsonNode.Parse(Fixtures.Text("tx-confirmed.json"))!;
        moved["status"] = status;
        Http.AnswerBytes(
            Fixtures.Url($"tx/{Fixtures.Confirmed}"), Encoding.UTF8.GetBytes(moved.ToJsonString()), 200);
    }
}
