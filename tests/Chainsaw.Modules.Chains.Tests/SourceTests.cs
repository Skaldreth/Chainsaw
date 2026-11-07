using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Chains.Tests;

public sealed class SourceTests : IDisposable
{
    private readonly SourceHarness _harness = new();

    public SourceTests() => _harness.AnswerAll();

    private IBitcoinSource Source => _harness.Source;

    private FakeExitHttp Http => _harness.Http;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_fetched_transaction_is_sealed_with_its_raw_bytes_and_hash()
    {
        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Equal(Fixtures.Confirmed, lookup.Value!.Txid);
        var row = Assert.Single(_harness.RawRows());
        var raw = Fixtures.Bytes("tx-confirmed.json");
        Assert.Equal(raw, row.Body);
        Assert.Equal(SHA256.HashData(raw), row.Sha256);
        Assert.Equal("bitcoin-mainnet", row.Chain);
        Assert.Equal("esplora", row.Source);
        Assert.Equal(Fixtures.Url($"tx/{Fixtures.Confirmed}"), row.Url);
        Assert.Equal("bitcoin.tx", row.Kind);
        Assert.Equal(200, row.Status);
        Assert.Equal("application/json", row.MediaType);
        Assert.Equal(FakeExitHttp.ExitId.ToString("D"), row.ExitId);
        Assert.Equal(TimeZoneInfo.Local.Id, row.TimeZone);
        Assert.Equal(_harness.Clock.UtcNow.AddMilliseconds(120), DateTimeOffset.Parse(row.ReceivedUtc, null));
        Assert.Equal(_harness.Clock.UtcNow, DateTimeOffset.Parse(row.RequestedUtc, null));
        Assert.Equal(TimeZoneInfo.ConvertTime(_harness.Clock.UtcNow.AddMilliseconds(120), TimeZoneInfo.Local).Offset,
            DateTimeOffset.Parse(row.ReceivedLocal, null).Offset);
        Assert.Equal(_harness.Profiles.Profile, JsonSerializer.Deserialize<OperatorProfile>(row.FetchedBy));
        Assert.Equal(32, row.Hash.Length);

        var origin = Assert.Single(lookup.Origins);
        Assert.Equal(row.Sequence, origin.Sequence);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(raw)), origin.Sha256);
        Assert.Equal(_harness.Profiles.Profile, origin.FetchedBy);
        Assert.Equal(new Uri(row.Url), origin.Url);
    }

    [Fact]
    public async Task The_sealed_responses_pass_verification_and_cannot_be_changed()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        await Source.GetBlockAsync(_harness.Case, Fixtures.Block);

        var verification = _harness.Verify();

        Assert.True(verification.IsIntact);
        var chain = Assert.Single(verification.Chains, chain => chain.Chain == "chains.raw_responses");
        Assert.Equal(3, chain.RecordCount); // the transaction, the block and its header (which the fake lacks)
        Assert.ThrowsAny<Exception>(() => _harness.Case.Write(transaction =>
        {
            using var command = transaction.CreateCommand("UPDATE chains_raw_responses SET body = x'00'");
            return command.ExecuteNonQuery();
        }));
        Assert.ThrowsAny<Exception>(() => _harness.Case.Write(transaction =>
        {
            using var command = transaction.CreateCommand("DELETE FROM chains_raw_responses");
            return command.ExecuteNonQuery();
        }));
        Assert.Equal(3, _harness.RawRows().Count);
    }

    [Fact]
    public async Task A_transaction_deep_when_it_was_fetched_is_never_fetched_again()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        // When it was fetched the case only knew block 800 000, so it is fetched once more after the height (800 010,
        // 11 blocks deep): a block that was shallow then may have left the chain since.
        var second = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var third = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var fourth = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Fetched, second.Status);
        Assert.Equal(LookupStatus.FromCase, third.Status);
        Assert.Equal(LookupStatus.FromCase, fourth.Status);
        Assert.Equal(
            [Fixtures.Url($"tx/{Fixtures.Confirmed}"), Fixtures.Url("blocks/tip/height"),
                Fixtures.Url($"tx/{Fixtures.Confirmed}")],
            Http.Sent.Select(request => request.Uri.AbsoluteUri));
        Assert.Equal(3, fourth.Origins[0].Sequence);
    }

    [Fact]
    public async Task A_shallow_transaction_is_fetched_again()
    {
        Http.AnswerBytes(Fixtures.Url("blocks/tip/height"), Encoding.ASCII.GetBytes("800002"), 200);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        var second = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Fetched, second.Status);
        Assert.Equal(3, Http.Sent.Count);
    }

    [Fact]
    public async Task The_height_is_not_asked_for_again_within_a_minute()
    {
        Http.AnswerBytes(Fixtures.Url("blocks/tip/height"), Encoding.ASCII.GetBytes("800002"), 200);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        _harness.Clock.UtcNow += TimeSpan.FromMinutes(2);
        Http.AnswerBytes(Fixtures.Url("blocks/tip/height"), Encoding.ASCII.GetBytes("800010"), 200);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var settled = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(
            2, Http.Sent.Count(request => request.Uri.AbsolutePath.EndsWith("/tip/height", StringComparison.Ordinal)));
        Assert.Equal(LookupStatus.FromCase, settled.Status);
    }

    [Fact]
    public async Task An_unconfirmed_transaction_is_fetched_every_time_without_asking_for_the_height()
    {
        var first = await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        var second = await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);

        Assert.Equal(LookupStatus.Fetched, first.Status);
        Assert.Equal(LookupStatus.Fetched, second.Status);
        Assert.False(second.Value!.IsConfirmed);
        Assert.Equal(2, Http.Sent.Count);
        Assert.Equal(2, _harness.RawRows().Count);
    }

    [Fact]
    public async Task Case_only_never_asks_the_source_and_says_what_is_missing()
    {
        var missing = await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed, LookupMode.CaseOnly);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        var kept = await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed, LookupMode.CaseOnly);

        Assert.Equal(LookupStatus.NotInCase, missing.Status);
        Assert.Null(missing.Value);
        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, kept.Status);
        Assert.Contains("kan ha ändrats", kept.Problem, StringComparison.Ordinal);
        Assert.Single(Http.Sent);
    }

    [Fact]
    public async Task Case_only_gives_a_settled_answer_without_a_warning()
    {
        // The second lookup asks for the height once and finds the transaction deep enough.
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var sent = Http.Sent.Count;

        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed, LookupMode.CaseOnly);

        Assert.Equal(LookupStatus.FromCase, lookup.Status);
        Assert.Equal(sent, Http.Sent.Count);
    }

    [Fact]
    public async Task Without_an_exit_and_without_an_answer_in_the_case_nothing_is_made_up()
    {
        Http.Connected = false;

        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Offline, lookup.Status);
        Assert.Null(lookup.Value);
        Assert.Empty(lookup.Origins);
        Assert.Contains("Ingen utgång", lookup.Problem, StringComparison.Ordinal);
        Assert.Empty(Http.Sent);
        Assert.Empty(_harness.RawRows());
    }

    [Fact]
    public async Task Without_an_exit_the_case_answers_and_says_it_may_be_outdated()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        Http.Connected = false;

        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);

        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, lookup.Status);
        Assert.Equal(Fixtures.Unconfirmed, lookup.Value!.Txid);
        Assert.Contains("kan ha ändrats", lookup.Problem, StringComparison.Ordinal);
        Assert.Equal(1, Assert.Single(lookup.Origins).Sequence);
        Assert.Single(Http.Sent);
    }

    [Fact]
    public async Task Without_an_exit_a_settled_answer_comes_from_the_case_without_a_warning()
    {
        await Source.GetTipHeightAsync(_harness.Case);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        Http.Connected = false;

        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.FromCase, lookup.Status);
        Assert.Null(lookup.Problem);
        Assert.Equal(0, Http.Refused);
    }

    [Fact]
    public async Task Address_pages_follow_one_another_until_a_page_is_empty()
    {
        var first = await Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.WitnessKeyHash);
        var second = await Source.GetAddressTransactionsAsync(
            _harness.Case, Fixtures.WitnessKeyHash, first.Value!.NextAfter);
        var third = await Source.GetAddressTransactionsAsync(
            _harness.Case, Fixtures.WitnessKeyHash, second.Value!.NextAfter);

        Assert.Equal(LookupStatus.Fetched, first.Status);
        Assert.Equal(3, first.Value.Transactions.Count);
        Assert.Equal(Fixtures.Confirmed, first.Value.NextAfter);
        Assert.Null(first.Value.After);
        Assert.Equal(Fixtures.Older, Assert.Single(second.Value.Transactions).Txid);
        Assert.Equal(Fixtures.Confirmed, second.Value.After);
        Assert.Equal(Fixtures.Older, second.Value.NextAfter);
        Assert.Empty(third.Value!.Transactions);
        Assert.Null(third.Value.NextAfter);
        var address = Fixtures.AddressText("wpkh");
        Assert.Equal(
            [
                Fixtures.Url($"address/{address}/txs"),
                Fixtures.Url($"address/{address}/txs/chain/{Fixtures.Confirmed}"),
                Fixtures.Url($"address/{address}/txs/chain/{Fixtures.Older}"),
            ],
            Http.Sent.Select(request => request.Uri.AbsoluteUri));
    }

    [Fact]
    public async Task The_first_address_page_is_always_fetched_again()
    {
        await Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.WitnessKeyHash);
        await Source.GetTipHeightAsync(_harness.Case);

        var again = await Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.WitnessKeyHash);

        Assert.Equal(LookupStatus.Fetched, again.Status);
    }

    [Fact]
    public async Task An_older_address_page_that_is_deep_is_not_fetched_again()
    {
        await Source.GetTipHeightAsync(_harness.Case);
        await Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.WitnessKeyHash, Fixtures.Confirmed);

        var again = await Source.GetAddressTransactionsAsync(
            _harness.Case, Fixtures.WitnessKeyHash, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.FromCase, again.Status);
        Assert.Equal(2, Http.Sent.Count);
    }

    [Fact]
    public async Task Address_pages_come_from_the_case_without_an_exit()
    {
        await Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.WitnessKeyHash);
        Http.Connected = false;

        var page = await Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.WitnessKeyHash);
        var missing = await Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.Taproot);

        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, page.Status);
        Assert.Equal(3, page.Value!.Transactions.Count);
        Assert.Equal(LookupStatus.Offline, missing.Status);
    }

    [Fact]
    public async Task A_transaction_seen_on_an_address_page_is_answered_from_the_case()
    {
        await Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.WitnessKeyHash);
        await Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.WitnessKeyHash, Fixtures.Confirmed);
        Http.Connected = false;

        // Block 799 000 is more than a thousand blocks under 800 001, the highest block the case has seen.
        var older = await Source.GetTransactionAsync(_harness.Case, Fixtures.Older);
        var shallow = await Source.GetTransactionAsync(_harness.Case, Fixtures.Coinbase);

        Assert.Equal(LookupStatus.FromCase, older.Status);
        Assert.Equal(new Chainsaw.SharedKernel.Satoshi(40_000_000), older.Value!.Outputs[1].Amount);
        Assert.Equal(2, older.Origins[0].Sequence);
        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, shallow.Status);
    }

    [Fact]
    public async Task An_address_on_another_network_is_refused_before_anything_is_sent()
    {
        var testnet = BitcoinAddress.Parse("tb1qrp33g0q5c5txsp9arysrx4k6zdkfs4nce4xj0gdcccefvpysxf3q0sl5k7");

        var lookup = await Source.GetAddressTransactionsAsync(_harness.Case, testnet);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Empty(Http.Sent);
    }

    [Fact]
    public async Task A_transaction_the_source_does_not_know_is_not_found_and_the_answer_is_sealed()
    {
        var unknown = Txid.Parse(Fixtures.Repeat("dd", 32));

        var lookup = await Source.GetTransactionAsync(_harness.Case, unknown);

        Assert.Equal(LookupStatus.NotFound, lookup.Status);
        Assert.Null(lookup.Value);
        var row = Assert.Single(_harness.RawRows());
        Assert.Equal(404, row.Status);
        Assert.Equal(row.Sequence, Assert.Single(lookup.Origins).Sequence);
    }

    [Fact]
    public async Task A_response_that_does_not_add_up_is_sealed_but_never_used()
    {
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Confirmed}"), "tx-bad-fee.json");

        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        Http.Connected = false;
        var offline = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Null(lookup.Value);
        Assert.Equal(Fixtures.Bytes("tx-bad-fee.json"), Assert.Single(_harness.RawRows()).Body);
        Assert.Equal(LookupStatus.Offline, offline.Status);
    }

    [Fact]
    public async Task A_server_error_falls_back_on_the_case_and_is_sealed()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        Http.AnswerBytes(Fixtures.Url($"tx/{Fixtures.Unconfirmed}"), Encoding.ASCII.GetBytes("busy"), 503);

        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);

        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, lookup.Status);
        Assert.Contains("503", lookup.Problem, StringComparison.Ordinal);
        Assert.Equal([1L, 2L], lookup.Origins.Select(origin => origin.Sequence));
        Assert.Equal(503, _harness.RawRows()[1].Status);
    }

    [Fact]
    public async Task A_failure_on_the_way_gives_the_exits_message()
    {
        Http.Failure = ExitError.Timeout;

        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Equal(new ExitException(ExitError.Timeout).Message, lookup.Problem);
        Assert.Empty(_harness.RawRows());
    }

    [Fact]
    public async Task Nothing_is_fetched_without_an_operator_profile()
    {
        _harness.Profiles.Profile = null;

        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Contains("operatörsprofil", lookup.Problem, StringComparison.Ordinal);
        Assert.Empty(Http.Sent);
    }

    [Fact]
    public async Task Nothing_is_fetched_while_the_vault_is_locked()
    {
        _harness.Profiles.Locked = true;

        var lookup = await Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Empty(Http.Sent);
    }

    [Fact]
    public async Task Output_spends_are_added_to_the_transaction_from_the_case()
    {
        var spends = await Source.GetOutputSpendsAsync(_harness.Case, Fixtures.Confirmed);
        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Fetched, spends.Status);
        Assert.Null(lookup.Value!.Outputs[0].SpentBy);
        Assert.Equal(Fixtures.Unconfirmed, lookup.Value.Outputs[1].SpentBy!.Txid);
        Assert.Equal([1L, 2L], lookup.Origins.Select(origin => origin.Sequence));
        Assert.NotNull(lookup.Problem);
    }

    [Fact]
    public async Task Open_output_spends_are_fetched_again_and_settled_ones_are_not()
    {
        await Source.GetOutputSpendsAsync(_harness.Case, Fixtures.Confirmed);
        var open = await Source.GetOutputSpendsAsync(_harness.Case, Fixtures.Confirmed);
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Confirmed}/outspends"), "outspends-settled.json");
        await Source.GetOutputSpendsAsync(_harness.Case, Fixtures.Confirmed);
        var sent = Http.Sent.Count;

        // Fetched when the case knew no height: asked once more after the height, then never again.
        var deep = await Source.GetOutputSpendsAsync(_harness.Case, Fixtures.Confirmed);
        var settled = await Source.GetOutputSpendsAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Fetched, open.Status);
        Assert.Equal(LookupStatus.Fetched, deep.Status);
        Assert.Equal(LookupStatus.FromCase, settled.Status);
        Assert.Equal(sent + 2, Http.Sent.Count); // the height and the spends, once each
    }

    [Fact]
    public async Task A_block_and_its_header_are_never_fetched_again()
    {
        _harness.AnswerReal();
        var first = await Source.GetBlockAsync(_harness.Case, Fixtures.RealBlock);
        var second = await Source.GetBlockAsync(_harness.Case, Fixtures.RealBlock);

        Assert.Equal(LookupStatus.Fetched, first.Status);
        Assert.Equal(LookupStatus.FromCase, second.Status);
        Assert.Equal(800_005, second.Value!.Height);
        Assert.True(first.Value!.HeaderVerified);
        Assert.True(second.Value.HeaderVerified);
        Assert.Equal(2, Http.Sent.Count);
    }

    [Fact]
    public async Task The_height_is_always_fetched_and_comes_from_the_case_only_with_a_warning()
    {
        var first = await Source.GetTipHeightAsync(_harness.Case);
        var second = await Source.GetTipHeightAsync(_harness.Case);
        Http.Connected = false;
        var offline = await Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(800_010, first.Value!.Height);
        Assert.Equal(LookupStatus.Fetched, second.Status);
        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, offline.Status);
        Assert.Equal(800_010, offline.Value!.Height);
    }

    [Fact]
    public async Task Requests_carry_only_the_lookup_and_never_the_case()
    {
        await Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.WitnessKeyHash);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Unconfirmed);
        await Source.GetOutputSpendsAsync(_harness.Case, Fixtures.Confirmed);
        await Source.GetBlockAsync(_harness.Case, Fixtures.Block);
        await Source.GetTipHeightAsync(_harness.Case);

        Assert.Equal(6, Http.Sent.Count); // the block's header too
        Assert.All(Http.Sent, request =>
        {
            Assert.Equal(ExitMethod.Get, request.Method);
            Assert.True(request.Body.IsEmpty);
            Assert.StartsWith(Fixtures.Base, request.Uri.AbsoluteUri, StringComparison.Ordinal);
            Assert.DoesNotContain(SourceHarness.CaseNumber, request.Uri.AbsoluteUri, StringComparison.Ordinal);
            Assert.Empty(request.Uri.Query);
        });
    }

    [Theory]
    [InlineData("\"sequence\": 4294967293", "\"sequence\": -1")]
    [InlineData("\"locktime\": 799999", "\"locktime\": 5000000000")]
    [InlineData("\"size\": 400", "\"size\": 5000000000")]
    [InlineData("\"vout\": 1,", "\"vout\": 3000000000,")]
    public async Task A_number_out_of_range_is_sealed_and_refused_without_throwing(string from, string to)
    {
        var body = Fixtures.Text("tx-confirmed.json").Replace(from, to, StringComparison.Ordinal);
        Assert.NotEqual(Fixtures.Text("tx-confirmed.json"), body);
        Http.AnswerBytes(Fixtures.Url($"tx/{Fixtures.Confirmed}"), Encoding.UTF8.GetBytes(body), 200);

        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Equal(Encoding.UTF8.GetBytes(body), Assert.Single(_harness.RawRows()).Body);
    }

    [Fact]
    public async Task An_address_that_is_not_text_is_sealed_and_refused_without_throwing()
    {
        var address = Fixtures.AddressText("wsh");
        var body = Fixtures.Text("tx-confirmed.json")
            .Replace(
                $"\"scriptpubkey_address\": \"{address}\"", "\"scriptpubkey_address\": 1", StringComparison.Ordinal);
        Assert.NotEqual(Fixtures.Text("tx-confirmed.json"), body);
        Http.AnswerBytes(Fixtures.Url($"tx/{Fixtures.Confirmed}"), Encoding.UTF8.GetBytes(body), 200);

        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Single(_harness.RawRows());
    }

    [Fact]
    public async Task A_bad_request_is_a_failure_and_not_a_missing_transaction()
    {
        Http.AnswerBytes(Fixtures.Url($"tx/{Fixtures.Confirmed}"), Encoding.ASCII.GetBytes("Too many"), 400);

        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Contains("400", lookup.Problem, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(404)]
    public async Task An_error_does_not_hide_what_the_case_already_has(int status)
    {
        await Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.WitnessKeyHash);
        var address = Fixtures.AddressText("wpkh");
        Http.AnswerBytes(Fixtures.Url($"address/{address}/txs"), Encoding.ASCII.GetBytes("error"), status);

        var lookup = await Source.GetAddressTransactionsAsync(_harness.Case, Fixtures.WitnessKeyHash);

        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, lookup.Status);
        Assert.Equal(3, lookup.Value!.Transactions.Count);
        Assert.Equal([1L, 2L], lookup.Origins.Select(origin => origin.Sequence));
    }

    [Fact]
    public async Task Answers_from_another_network_are_never_used()
    {
        var testnet = new EsploraBitcoinSource(
            Http, _harness.Profiles, _harness.Clock,
            new BitcoinSourceOptions { EsploraBaseUri = new Uri(Fixtures.Base), Network = BitcoinNetwork.Testnet },
            TimeZoneInfo.Utc);
        Http.AnswerBytes(Fixtures.Url("blocks/tip/height"), Encoding.ASCII.GetBytes("4800000"), 200);
        await testnet.GetTipHeightAsync(_harness.Case);
        Http.Connected = false;

        var mainnet = await Source.GetTipHeightAsync(_harness.Case);
        var stillTestnet = await testnet.GetTipHeightAsync(_harness.Case);

        Assert.Equal(LookupStatus.Offline, mainnet.Status);
        Assert.Equal(4_800_000, stillTestnet.Value!.Height);
    }

    [Fact]
    public async Task An_index_that_points_at_another_response_is_not_believed()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        _harness.Case.Write(transaction =>
        {
            using var command = transaction.CreateCommand(
                "INSERT INTO chains_lookup VALUES ('bitcoin-mainnet', 'bitcoin.outspends', "
                + $"'{Fixtures.Confirmed}', 1, NULL)");
            return command.ExecuteNonQuery();
        });
        Http.Connected = false;

        var spends = await Source.GetOutputSpendsAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Offline, spends.Status);
    }

    [Fact]
    public async Task Spends_that_can_still_change_are_marked_on_the_transaction()
    {
        await Source.GetOutputSpendsAsync(_harness.Case, Fixtures.Confirmed);

        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.Contains("spenderade", lookup.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_closed_case_is_refused()
    {
        _harness.Case.Close();

        await Assert.ThrowsAsync<Chainsaw.Modules.Cases.Contracts.CaseException>(
            () => Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed));
    }
}
