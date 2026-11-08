using System.Text;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

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
    public async Task A_transaction_unconfirmed_again_is_back_in_the_mempool_once_a_new_block_confirms_it()
    {
        await TipAsync(Fixtures.Base, 800_001);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        AnswerStatus(new JsonObject { ["confirmed"] = false });
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        // The same endpoint had shown the block, then answered mempool: one of its nodes may lag behind (finding I1).
        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));

        // The same answer again after a new block confirms it.
        await TipAsync(Fixtures.Base, 800_002);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));

        Assert.Equal(BlockChangeKind.Unconfirmed, change.Kind);
        Assert.Null(change.Now.Height);
        Assert.Null(change.Now.Hash);
    }

    [Fact]
    public async Task A_transaction_the_source_no_longer_knows_is_gone_once_a_new_block_confirms_it()
    {
        await TipAsync(Fixtures.Base, 800_001);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        Http.AnswerBytes(Fixtures.Url($"tx/{Fixtures.Confirmed}"), Encoding.UTF8.GetBytes("Transaction not found"), 404);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));

        await TipAsync(Fixtures.Base, 800_002);
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

    [Fact]
    public async Task An_address_page_with_the_transaction_in_another_block_at_the_same_height_is_moved()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var page = JsonNode.Parse("[" + Fixtures.Text("tx-confirmed.json") + "]")!;
        page[0]!["status"] = new JsonObject
        {
            ["confirmed"] = true, ["block_height"] = 800_000, ["block_hash"] = OtherHash, ["block_time"] = 1_700_000_000,
        };
        var address = Fixtures.AddressText("wpkh");
        Http.AnswerBytes(Fixtures.Url($"address/{address}/txs"), Encoding.UTF8.GetBytes(page.ToJsonString()), 200);
        await Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.WitnessKeyHash);

        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));

        Assert.Equal(BlockChangeKind.Moved, change.Kind);
        Assert.Equal(OtherHash, change.Now.Hash);
        Assert.Equal("esplora.test", change.Now.Endpoint);
    }

    [Fact]
    public async Task An_endpoint_that_lags_behind_and_says_mempool_is_passed_over()
    {
        var second = await OnlySecondAsync();
        AnswerSecond(new JsonObject { ["confirmed"] = false }, 200);

        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));

        // Once the same endpoint has shown the block's height, its answers count, when a new block confirms them.
        await TipAsync(Second, 800_003);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));
        await TipAsync(Second, 800_004);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var change = Assert.Single(await Source.GetBlockChangesAsync(_harness.Case));
        Assert.Equal(BlockChangeKind.Unconfirmed, change.Kind);
        Assert.Equal(second, change.Now.Endpoint);
    }

    [Fact]
    public async Task An_endpoint_that_lags_behind_and_does_not_know_the_transaction_is_passed_over()
    {
        await OnlySecondAsync();
        Http.AnswerBytes(Second + $"tx/{Fixtures.Confirmed}", Encoding.UTF8.GetBytes("Transaction not found"), 404);

        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));

        await TipAsync(Second, 800_003);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));
        await TipAsync(Second, 800_004);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        Assert.Equal(BlockChangeKind.Gone, Assert.Single(await Source.GetBlockChangesAsync(_harness.Case)).Kind);
    }

    [Fact]
    public async Task A_not_found_the_lookup_did_not_believe_is_never_a_transaction_gone()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var first = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint;
        var (second, saved) = _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", null);
        await saved;

        // The first endpoint fails once, so that its next 404 is not believed; the second then fails as well.
        Http.FailingHosts.Add("esplora.test");
        await _harness.Sources.SetEnabledAsync(Chain.Bitcoin, second.Id, false);
        await Source.GetTipHeightAsync(_harness.Case);
        Http.FailingHosts.Clear();
        await _harness.Sources.SetEnabledAsync(Chain.Bitcoin, second.Id, true);
        Http.AnswerBytes(Fixtures.Url("blocks/tip/height"), Encoding.ASCII.GetBytes("800001"), 200);
        Http.AnswerBytes(
            Fixtures.Url($"tx/{Fixtures.Confirmed}"), Encoding.UTF8.GetBytes("Transaction not found"), 404);
        Http.AnswerBytes(Second + $"tx/{Fixtures.Confirmed}", "busy"u8.ToArray(), 503);
        Http.AnswerBytes(Second + "blocks/tip/height", Encoding.ASCII.GetBytes("800001"), 200);

        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.True(_harness.Sources.Get(Chain.Bitcoin).Endpoints.Single(e => e.Endpoint.Id == first.Id).FailedSince);
        Assert.Contains(_harness.RawRows(), row => row.Status == 404);
        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));
    }

    [Fact]
    public async Task A_shallow_answer_is_asked_again_of_an_endpoint_in_step_with_the_chain()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var (second, saved) = _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", null);
        await saved;
        await _harness.Sources.SetModeAsync(Chain.Bitcoin, SourceMode.Priority);
        await _harness.Sources.MoveAsync(Chain.Bitcoin, second.Id, -1);
        Http.AnswerBytes(Fixtures.Url("blocks/tip/height"), Encoding.ASCII.GetBytes("800001"), 200);
        Http.AnswerBytes(Second + "blocks/tip/height", Encoding.ASCII.GetBytes("799999"), 200);
        AnswerSecond(new JsonObject { ["confirmed"] = false }, 200);
        await _harness.Sources.ProbeAsync(Chain.Bitcoin);
        Http.Sent.Clear();

        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.DoesNotContain(Http.Sent, request => request.Uri.AbsolutePath.Contains("/tx/", StringComparison.Ordinal)
            && request.Uri.Host == "second.example");
        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));
    }

    [Fact]
    public async Task Reading_the_changes_and_the_newest_block_sends_nothing()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        AnswerStatus(new JsonObject { ["confirmed"] = false });
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        Http.Sent.Clear();

        await Source.GetBlockChangesAsync(_harness.Case);
        await Source.GetTipAsync(_harness.Case, LookupMode.CaseOnly);

        Assert.Empty(Http.Sent);
    }

    [Fact]
    public async Task Two_endpoints_that_both_say_mempool_confirm_each_other_without_a_new_block()
    {
        await TipAsync(Fixtures.Base, 800_001);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var (second, saved) = _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", null);
        await saved;
        await TipAsync(Second, 800_001);
        AnswerStatus(new JsonObject { ["confirmed"] = false });
        AnswerSecond(new JsonObject { ["confirmed"] = false }, 200);
        var first = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint;

        await _harness.Sources.SetEnabledAsync(Chain.Bitcoin, second.Id, false);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        Assert.Empty(await Source.GetBlockChangesAsync(_harness.Case));
        await _harness.Sources.SetEnabledAsync(Chain.Bitcoin, second.Id, true);
        await _harness.Sources.SetEnabledAsync(Chain.Bitcoin, first.Id, false);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(BlockChangeKind.Unconfirmed, Assert.Single(await Source.GetBlockChangesAsync(_harness.Case)).Kind);
    }

    [Fact]
    public async Task A_believed_not_found_follows_the_response_to_another_case()
    {
        await TipAsync(Fixtures.Base, 800_001);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        Http.AnswerBytes(Fixtures.Url($"tx/{Fixtures.Confirmed}"), Encoding.UTF8.GetBytes("Transaction not found"), 404);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var sequence = _harness.RawRows().Single(row => row.Status == 404).Sequence;
        using var other = _harness.CreateCase("2026-0051");

        var index = _harness.Case.Read(reader => _harness.Transfer.ReadIndex(reader, sequence));
        var response = _harness.Case.Read(reader => _harness.Transfer.Read(reader, sequence))!;
        other.Write(transaction => _harness.Transfer.Seal(transaction, response, index!));

        Assert.True(index!.NotFound);
        Assert.True(other.Read(reader => _harness.Transfer.ReadIndex(reader, 1)!.NotFound));
    }

    /// <summary>Answers and fetches the chain's height from an endpoint.</summary>
    /// <param name="baseUrl">The endpoint's address.</param>
    /// <param name="height">The height.</param>
    /// <returns>A task that completes when it is sealed.</returns>
    private async Task TipAsync(string baseUrl, long height)
    {
        Http.AnswerBytes(baseUrl + "blocks/tip/height", Encoding.ASCII.GetBytes(height.ToString(
            System.Globalization.CultureInfo.InvariantCulture)), 200);
        await Source.GetTipHeightAsync(_harness.Case);
    }

    /// <summary>The second endpoint's address.</summary>
    private const string Second = "https://second.example/api/";

    /// <summary>
    /// Seals the confirmed transaction from the first endpoint, then leaves only a second endpoint switched on, which
    /// has shown no height yet.
    /// </summary>
    /// <returns>The second endpoint's name.</returns>
    private async Task<string> OnlySecondAsync()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var first = _harness.Sources.Get(Chain.Bitcoin).Endpoints[0].Endpoint;
        var (second, saved) = _harness.Sources.Add(Chain.Bitcoin, new Uri(Second), "esplora", null);
        await saved;
        await _harness.Sources.SetEnabledAsync(Chain.Bitcoin, first.Id, false);
        return second.Name;
    }

    /// <summary>Answers the confirmed fixture from the second endpoint with another status.</summary>
    /// <param name="status">The new status.</param>
    /// <param name="code">The HTTP status.</param>
    private void AnswerSecond(JsonObject status, int code)
    {
        var moved = JsonNode.Parse(Fixtures.Text("tx-confirmed.json"))!;
        moved["status"] = status;
        Http.AnswerBytes(Second + $"tx/{Fixtures.Confirmed}", Encoding.UTF8.GetBytes(moved.ToJsonString()), code);
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
