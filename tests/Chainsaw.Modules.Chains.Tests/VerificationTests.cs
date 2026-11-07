using System.Text;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// Stage 15: the source checks a transaction against its own bytes and a block against its own header, never waits
/// for an OP_RETURN output to be spent, gives an address's summary, and notices a transaction that left its block.
/// </summary>
public sealed class VerificationTests : IDisposable
{
    private readonly SourceHarness _harness = new();

    public VerificationTests()
    {
        _harness.AnswerAll();
        _harness.AnswerReal();
    }

    private IBitcoinSource Source => _harness.Source;

    private FakeExitHttp Http => _harness.Http;

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_transaction_is_checked_against_its_bytes_and_stays_checked_without_asking_again()
    {
        var verified = await Source.VerifyTransactionAsync(_harness.Case, Fixtures.Real);
        Http.Connected = false;
        var later = await Source.GetTransactionAsync(_harness.Case, Fixtures.Real, LookupMode.CaseOnly);

        Assert.True(verified.Value!.BytesVerified);
        Assert.Null(verified.Problem);
        Assert.Equal(2, verified.Origins.Count);
        Assert.True(later.Value!.BytesVerified);
        Assert.Equal(0, Http.Refused);
        Assert.Contains(Http.Sent, request => request.Uri.AbsoluteUri.EndsWith("/hex", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_transaction_without_checked_bytes_is_not_marked_checked()
    {
        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Real);

        Assert.False(lookup.Value!.BytesVerified);
        Assert.DoesNotContain(Http.Sent, request => request.Uri.AbsoluteUri.EndsWith("/hex", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_description_that_contradicts_the_bytes_is_refused_now_and_later()
    {
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Real}"), "tx-real-forged.json");

        var verified = await Source.VerifyTransactionAsync(_harness.Case, Fixtures.Real);
        var later = await Source.GetTransactionAsync(_harness.Case, Fixtures.Real);

        Assert.Equal(LookupStatus.Failed, verified.Status);
        Assert.Null(verified.Value);
        Assert.Contains("bytes", verified.Problem, StringComparison.Ordinal);
        Assert.Equal(LookupStatus.Failed, later.Status);
        Assert.Null(later.Value);
    }

    [Fact]
    public async Task Bytes_of_another_transaction_are_sealed_but_never_used()
    {
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Confirmed}/hex"), "tx-real-hex.txt");

        var lookup = await Source.VerifyTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.NotNull(lookup.Value);
        Assert.False(lookup.Value.BytesVerified);
        Assert.Contains(lookup.Notes, note => note.Contains("inte kontrollerad", StringComparison.Ordinal));
        Assert.Contains(_harness.RawRows(), row => row.Kind == "bitcoin.tx-hex");
    }

    [Fact]
    public async Task Offline_the_transaction_comes_from_the_case_unchecked_and_says_so()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Real);
        Http.Connected = false;

        var lookup = await Source.VerifyTransactionAsync(_harness.Case, Fixtures.Real);

        Assert.NotNull(lookup.Value);
        Assert.False(lookup.Value.BytesVerified);
        Assert.Contains(lookup.Notes, note => note.Contains("bytes kunde inte hämtas", StringComparison.Ordinal));
    }

    [Fact]
    public void The_txid_is_the_double_sha256_of_the_bytes_without_the_witness()
    {
        var withWitness = RawTransactionReader.Read(Hex("tx-real-hex.txt"));
        var legacy = RawTransactionReader.Read(Hex("tx-real-base-hex.txt"));

        Assert.Equal(Fixtures.Real, withWitness.Txid);
        Assert.Equal(Fixtures.Real, legacy.Txid);
        Assert.Equal(3, withWitness.Outputs.Count);
        Assert.Equal(30_000_000, withWitness.Outputs[0].Value);
        Assert.Equal(Fixtures.Older.Value, withWitness.Inputs[0].PreviousTxid);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    [InlineData(60)]
    public void Bytes_that_end_too_early_are_refused(int cut)
    {
        var bytes = Hex("tx-real-hex.txt");

        Assert.Throws<EsploraFormatException>(() => RawTransactionReader.Read(bytes.AsSpan(0, bytes.Length - cut)));
    }

    [Fact]
    public void Bytes_that_go_on_after_the_transaction_are_refused()
    {
        byte[] bytes = [.. Hex("tx-real-hex.txt"), 0];

        Assert.Throws<EsploraFormatException>(() => RawTransactionReader.Read(bytes));
    }

    [Fact]
    public void A_count_larger_than_the_bytes_is_refused_without_allocating()
    {
        // Version, then an input count of 2^32 - 1.
        byte[] bytes = [2, 0, 0, 0, 0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0];

        Assert.Throws<EsploraFormatException>(() => RawTransactionReader.Read(bytes));
    }

    [Fact]
    public void Hex_of_another_transaction_is_refused()
    {
        Assert.Throws<EsploraFormatException>(
            () => RawTransactionReader.ParseHex(Fixtures.Bytes("tx-real-hex.txt"), Fixtures.Confirmed));
        Assert.Throws<EsploraFormatException>(
            () => RawTransactionReader.ParseHex(Encoding.ASCII.GetBytes("not hex"), Fixtures.Real));
    }

    [Fact]
    public async Task A_block_that_contradicts_its_header_is_refused()
    {
        var forged = JsonNode.Parse(Fixtures.Text("block-real.json"))!;
        forged["merkle_root"] = Fixtures.Repeat("5f", 32);
        Http.AnswerBytes(
            Fixtures.Url($"block/{Fixtures.RealBlock}"), Encoding.UTF8.GetBytes(forged.ToJsonString()), 200);

        var lookup = await Source.GetBlockAsync(_harness.Case, Fixtures.RealBlock);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Null(lookup.Value);
        Assert.Contains("huvud", lookup.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_block_whose_header_cannot_be_had_is_given_unchecked_and_says_so()
    {
        var lookup = await Source.GetBlockAsync(_harness.Case, Fixtures.Block);

        Assert.NotNull(lookup.Value);
        Assert.False(lookup.Value.HeaderVerified);
        Assert.Contains(lookup.Notes, note => note.Contains("huvud kunde inte hämtas", StringComparison.Ordinal));
    }

    [Fact]
    public void A_header_must_meet_the_target_its_bits_name()
    {
        var header = Fixtures.Bytes("block-real-header.txt");

        Assert.Equal(Fixtures.RealBlock, BlockHeaderReader.ParseHex(header, Fixtures.RealBlock).Hash);
        Assert.True(BlockHeaderReader.MeetsTarget(Fixtures.RealBlock, 0x207FFFFF));
        Assert.False(BlockHeaderReader.MeetsTarget(Fixtures.RealBlock, 0x1D00FFFF));
        Assert.False(BlockHeaderReader.MeetsTarget(Fixtures.RealBlock, 0x20800000)); // negative
        Assert.Throws<EsploraFormatException>(
            () => BlockHeaderReader.ParseHex(Encoding.ASCII.GetBytes("00"), Fixtures.RealBlock));
        Assert.Throws<EsploraFormatException>(() => BlockHeaderReader.ParseHex(header, Fixtures.Block));
    }

    [Fact]
    public async Task An_op_return_output_never_keeps_the_spends_open()
    {
        await Source.GetTipHeightAsync(_harness.Case);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Real);
        await Source.GetOutputSpendsAsync(_harness.Case, Fixtures.Real);
        var sent = Http.Sent.Count;

        var again = await Source.GetOutputSpendsAsync(_harness.Case, Fixtures.Real);
        var transaction = await Source.GetTransactionAsync(_harness.Case, Fixtures.Real, LookupMode.CaseOnly);

        Assert.Equal(LookupStatus.FromCase, again.Status);
        Assert.Equal(sent, Http.Sent.Count);
        Assert.Null(transaction.Problem); // no warning that the spends may have changed
    }

    [Fact]
    public async Task Without_the_transaction_in_the_case_every_output_waits_for_a_spend()
    {
        await Source.GetTipHeightAsync(_harness.Case);
        await Source.GetOutputSpendsAsync(_harness.Case, Fixtures.Real);

        var again = await Source.GetOutputSpendsAsync(_harness.Case, Fixtures.Real);

        Assert.Equal(LookupStatus.Fetched, again.Status);
    }

    [Fact]
    public void An_output_that_can_never_be_spent_is_recognised()
    {
        var opReturn = new BitcoinOutput(0, BitcoinScriptType.OpReturn, null, default, "6a00", null);
        var huge = new BitcoinOutput(0, BitcoinScriptType.NonStandard, null, default, new string('5', 20_002), null);
        var normal = new BitcoinOutput(0, BitcoinScriptType.NonStandard, null, default, "51", null);

        Assert.True(EsploraBitcoinSource.IsUnspendable(opReturn));
        Assert.True(EsploraBitcoinSource.IsUnspendable(huge));
        Assert.False(EsploraBitcoinSource.IsUnspendable(normal));
    }

    [Fact]
    public async Task The_address_summary_is_fetched_every_time_and_from_the_case_offline()
    {
        var first = await Source.GetAddressSummaryAsync(_harness.Case, Fixtures.WitnessKeyHash);
        var second = await Source.GetAddressSummaryAsync(_harness.Case, Fixtures.WitnessKeyHash);
        Http.Connected = false;
        var offline = await Source.GetAddressSummaryAsync(_harness.Case, Fixtures.WitnessKeyHash);

        Assert.Equal(4, first.Value!.TransactionCount);
        Assert.Equal(49_985_000, first.Value.Confirmed.Funded.Value);
        Assert.Equal(LookupStatus.Fetched, second.Status);
        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, offline.Status);
        Assert.Equal(4, offline.Value!.TransactionCount);
    }

    [Theory]
    [InlineData("\"address\":\"bc1", "\"address\":\"bc1x")]
    [InlineData("\"spent_txo_count\":1", "\"spent_txo_count\":9")]
    [InlineData("\"tx_count\":3", "\"tx_count\":-3")]
    public async Task A_summary_that_does_not_add_up_is_sealed_but_not_used(string from, string to)
    {
        var text = JsonNode.Parse(Fixtures.Text("address-summary.json"))!.ToJsonString().Replace(
            from, to, StringComparison.Ordinal);
        Http.AnswerBytes(
            Fixtures.Url($"address/{Fixtures.AddressText("wpkh")}"), Encoding.UTF8.GetBytes(text), 200);

        var lookup = await Source.GetAddressSummaryAsync(_harness.Case, Fixtures.WitnessKeyHash);

        Assert.Equal(LookupStatus.Failed, lookup.Status);
        Assert.Single(_harness.RawRows());
    }

    [Fact]
    public async Task A_transaction_that_left_its_block_says_so()
    {
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        var moved = JsonNode.Parse(Fixtures.Text("tx-confirmed.json"))!;
        moved["status"] = new JsonObject { ["confirmed"] = false };
        Http.AnswerBytes(
            Fixtures.Url($"tx/{Fixtures.Confirmed}"), Encoding.UTF8.GetBytes(moved.ToJsonString()), 200);

        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        Assert.False(lookup.Value!.IsConfirmed);
        Assert.Contains(lookup.Notes, note => note.Contains("omorganisation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_transaction_seen_in_a_shallow_block_is_asked_again_once_it_is_deep()
    {
        Http.AnswerBytes(Fixtures.Url("blocks/tip/height"), Encoding.ASCII.GetBytes("800001"), 200);
        await Source.GetTipHeightAsync(_harness.Case);
        await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed);
        Http.AnswerBytes(Fixtures.Url("blocks/tip/height"), Encoding.ASCII.GetBytes("800100"), 200);
        await Source.GetTipHeightAsync(_harness.Case);

        // The case now knows the block is deep, but the answer was fetched while it was two blocks deep.
        var lookup = await Source.GetTransactionAsync(_harness.Case, Fixtures.Confirmed, LookupMode.CaseOnly);

        Assert.Equal(LookupStatus.FromCaseMayBeOutdated, lookup.Status);
    }

    [Fact]
    public async Task A_contradicting_answer_never_blocks_the_transaction_once_the_source_is_right_again()
    {
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Real}"), "tx-real-forged.json");
        var refused = await Source.VerifyTransactionAsync(_harness.Case, Fixtures.Real);
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Real}"), "tx-real.json");

        var later = await Source.GetTransactionAsync(_harness.Case, Fixtures.Real);

        Assert.Equal(LookupStatus.Failed, refused.Status);
        Assert.Equal(LookupStatus.Fetched, later.Status);
        Assert.True(later.Value!.BytesVerified);
    }

    [Fact]
    public async Task A_contradicting_answer_never_hides_an_earlier_good_one()
    {
        await Source.VerifyTransactionAsync(_harness.Case, Fixtures.Real);
        Http.Answer(Fixtures.Url($"tx/{Fixtures.Real}"), "tx-real-forged.json");
        var refused = await Source.GetTransactionAsync(_harness.Case, Fixtures.Real);

        var fromCase = await Source.GetTransactionAsync(_harness.Case, Fixtures.Real, LookupMode.CaseOnly);

        Assert.Equal(LookupStatus.Failed, refused.Status);
        Assert.NotNull(fromCase.Value);
        Assert.True(fromCase.Value.BytesVerified);
        Assert.Equal(30_000_000, fromCase.Value.Outputs[0].Amount.Value);
    }

    [Fact]
    public void A_target_beyond_256_bits_is_never_met()
    {
        Assert.False(BlockHeaderReader.MeetsTarget(Fixtures.RealBlock, 0x2200FFFF));
    }

    private static byte[] Hex(string fixture) => Convert.FromHexString(Fixtures.Text(fixture).Trim());
}
