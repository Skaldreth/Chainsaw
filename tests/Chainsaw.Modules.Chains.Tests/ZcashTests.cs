using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

/// <summary>
/// Zcash's transparent part: its addresses (<c>t1…</c>, <c>t3…</c>) never taken for another chain's, and its source
/// against synthetic Blockbook answers shaped as the real ones (Zelcore's Blockbook, read 2026-11-09): an input's
/// index of 0 left out, amounts as strings of zatoshi, zcashd's own answer with the pools' value balances, and who
/// spent an output read from its address's history. No real address or transaction is used.
/// </summary>
public sealed class ZcashTests
{
    /// <summary>The payer's synthetic address.</summary>
    private static readonly BitcoinAddress Payer = Pkh(0x11);

    /// <summary>The payee's synthetic address.</summary>
    private static readonly BitcoinAddress Payee = Pkh(0x22);

    /// <summary>The payment: from the payer to the payee, with change.</summary>
    private static readonly Txid Payment = Txid.Parse(Chain.Zcash, Fixtures.Repeat("f1", 32));

    /// <summary>The shielding: the payee puts most of it into the Sapling pool.</summary>
    private static readonly Txid Shielding = Txid.Parse(Chain.Zcash, Fixtures.Repeat("a5", 32));

    [Fact]
    public void A_transparent_address_is_on_zcash_and_never_on_another_chain()
    {
        var script = BitcoinAddress.FromHash(
            BitcoinScriptType.PayToScriptHash, Enumerable.Repeat((byte)0x33, 20).ToArray(), BitcoinNetwork.Zcash);

        Assert.StartsWith("t1", Payer.Value, StringComparison.Ordinal);
        Assert.StartsWith("t3", script.Value, StringComparison.Ordinal);
        Assert.True(BitcoinAddress.TryParse(Chain.Zcash, Payer.Value, out var read));
        Assert.Equal(Chain.Zcash, read.Chain);
        Assert.Equal(BitcoinScriptType.PayToPublicKeyHash, read.Type);
        Assert.Equal(BitcoinScriptType.PayToScriptHash, BitcoinAddress.Parse(Chain.Zcash, script.Value).Type);
        Assert.False(BitcoinAddress.TryParse(Payer.Value, out _));
        Assert.False(BitcoinAddress.TryParse(Chain.Litecoin, Payer.Value, out _));
        Assert.True(BitcoinAddress.TryParseOnAnyChain(Payer.Value, out var any));
        Assert.Equal(Chain.Zcash, any.Chain);
        Assert.Throws<ArgumentException>(() => BitcoinAddress.FromWitness(
            0, Enumerable.Repeat((byte)0x33, 20).ToArray(), BitcoinNetwork.Zcash));
    }

    [Fact]
    public void A_txid_carries_its_chain_and_is_stored_with_it_beside_bitcoins()
    {
        var bitcoin = Txid.Parse(Fixtures.Repeat("f1", 32));

        Assert.NotEqual(bitcoin, Payment);
        Assert.Equal(bitcoin.Value, bitcoin.Stored);
        Assert.Equal("zcash:" + Payment.Value, Payment.Stored);
        Assert.True(Txid.TryParseStored(Payment.Stored, out var read));
        Assert.Equal(Payment, read);
        Assert.False(Txid.TryParseStored("ethereum:" + Payment.Value, out _));
        Assert.Throws<ArgumentOutOfRangeException>(() => Payment.On(Chain.Ethereum));
    }

    [Fact]
    public async Task Money_into_the_pool_is_read_from_zcashds_value_balance_and_sealed_under_zcash()
    {
        using var harness = new SourceHarness();
        AnswerShielding(harness, saplingBalance: -1_090_000);

        var lookup = await harness.Zcash.GetTransactionAsync(harness.Case, Shielding);

        Assert.Equal(LookupStatus.Fetched, lookup.Status);
        var transaction = lookup.Value!;
        Assert.Equal(Chain.Zcash, transaction.Txid.Chain);
        Assert.Equal(new Satoshi(1_090_000), transaction.Shielded!.IntoPool);
        Assert.Equal(Satoshi.Zero, transaction.Shielded.OutOfPool);
        Assert.Equal([ShieldedPool.Sapling], transaction.Shielded.Pools);
        Assert.Equal(new Satoshi(10_000), transaction.Fee);
        Assert.Equal(Payment, transaction.Inputs[0].PreviousTxid);
        Assert.Equal(0, transaction.Inputs[0].PreviousIndex);
        Assert.Equal(Payee, transaction.Inputs[0].Address);
        Assert.False(transaction.BytesVerified);
        Assert.Equal(3_000_005, transaction.Block!.Height);
        Assert.All(harness.RawRows(), row => Assert.Equal("zcash-mainnet", row.Chain));
        Assert.True(harness.Verify().IsIntact);
    }

    [Fact]
    public async Task Value_balances_that_do_not_add_up_are_refused()
    {
        using var harness = new SourceHarness();
        AnswerShielding(harness, saplingBalance: -2_000_000);

        var lookup = await harness.Zcash.GetTransactionAsync(harness.Case, Shielding);

        Assert.Null(lookup.Value);
    }

    [Fact]
    public async Task Who_spent_an_output_is_read_from_its_addresss_history()
    {
        using var harness = new SourceHarness();
        AnswerPayment(harness);
        AnswerTip(harness, 3_000_100);
        Answer(harness, Window(Payee, 3_000_000, 1_152), Page(Payee, 1, -1, ShieldingJson(), PaymentJson()));
        Answer(harness, Window(Payer, 3_000_000, 1_152), Page(Payer, 1, -1, PaymentJson()));

        var spends = await harness.Zcash.GetOutputSpendsAsync(harness.Case, Payment);

        Assert.NotNull(spends.Value);
        Assert.Equal(Shielding, spends.Value[0].SpentBy!.Txid);
        Assert.Null(spends.Value[1].SpentBy);
        var fromCase = await harness.Zcash.GetOutputSpendsAsync(harness.Case, Payment, LookupMode.CaseOnly);
        Assert.Equal(Shielding, fromCase.Value![0].SpentBy!.Txid);
    }

    [Fact]
    public async Task An_addresss_page_and_summary_are_read()
    {
        using var harness = new SourceHarness();
        Answer(harness, $"api/v2/address/{Payee.Value}?details=txs&pageSize=5", Page(
            Payee, 1, 1, ShieldingJson(), PaymentJson()));
        Answer(harness, $"api/v2/address/{Payee.Value}?details=basic", $$"""
            {"page":1,"totalPages":1,"itemsOnPage":1000,"address":"{{Payee.Value}}","balance":"400000",
             "totalReceived":"1900000","totalSent":"1500000","unconfirmedBalance":"0","unconfirmedTxs":0,"txs":2}
            """);

        var page = await harness.Zcash.GetAddressTransactionsAsync(harness.Case, Payee);
        var summary = await harness.Zcash.GetAddressSummaryAsync(harness.Case, Payee);

        Assert.Equal([Shielding, Payment], page.Value!.Transactions.Select(transaction => transaction.Txid));
        Assert.Null(page.Value.NextAfter);
        Assert.Equal(2, summary.Value!.Confirmed.TransactionCount);
        Assert.Equal(new Satoshi(1_900_000), summary.Value.Confirmed.Funded);
        Assert.Equal(new Satoshi(1_500_000), summary.Value.Confirmed.Spent);
    }

    [Fact]
    public async Task A_bitcoin_address_is_never_sent_to_zcashs_source()
    {
        using var harness = new SourceHarness();
        var bitcoin = BitcoinAddress.FromHash(
            BitcoinScriptType.PayToPublicKeyHash, Enumerable.Repeat((byte)0x11, 20).ToArray(), BitcoinNetwork.Mainnet);

        var page = await harness.Zcash.GetAddressTransactionsAsync(harness.Case, bitcoin);

        Assert.Null(page.Value);
        Assert.Empty(harness.Http.Sent);
    }

    [Fact]
    public async Task A_coinbase_that_pays_into_orchard_has_no_fee_and_its_flow_into_the_pool()
    {
        using var harness = new SourceHarness();
        var coinbase = Txid.Parse(Chain.Zcash, Fixtures.Repeat("c1", 32));
        Answer(harness, $"api/v2/tx/{coinbase.Value}", Tx(coinbase, [], [(Payer, 40_000)], 3_000_001, "b3"));
        Answer(harness, $"api/v2/tx-specific/{coinbase.Value}", Spec(
            coinbase, [], [40_000], 3_000_001, "b3", orchard: -5_000, orchardActions: 2));

        var transaction = (await harness.Zcash.GetTransactionAsync(harness.Case, coinbase)).Value!;

        Assert.True(transaction.IsCoinbase);
        Assert.Equal(Satoshi.Zero, transaction.Fee);
        Assert.Equal(new Satoshi(5_000), transaction.Shielded!.IntoPool);
        Assert.Equal([ShieldedPool.Orchard], transaction.Shielded.Pools);
    }

    [Fact]
    public async Task Money_out_of_sapling_has_no_transparent_input_and_its_fee_counted_from_the_balance()
    {
        using var harness = new SourceHarness();
        var unshield = Txid.Parse(Chain.Zcash, Fixtures.Repeat("d1", 32));
        Answer(harness, $"api/v2/tx/{unshield.Value}", Tx(unshield, [], [(Payee, 1_990_000)], 3_000_010, "b4"));
        Answer(harness, $"api/v2/tx-specific/{unshield.Value}", Spec(
            unshield, [], [1_990_000], 3_000_010, "b4", sapling: 2_000_000));

        var transaction = (await harness.Zcash.GetTransactionAsync(harness.Case, unshield)).Value!;

        Assert.Empty(transaction.Inputs);
        Assert.Equal(new Satoshi(2_000_000), transaction.Shielded!.OutOfPool);
        Assert.Equal(new Satoshi(10_000), transaction.Fee);
    }

    [Fact]
    public async Task Money_into_sprout_is_read_from_the_joinsplits()
    {
        using var harness = new SourceHarness();
        var sprout = Txid.Parse(Chain.Zcash, Fixtures.Repeat("d2", 32));
        var funding = Txid.Parse(Chain.Zcash, Fixtures.Repeat("e2", 32));
        Answer(harness, $"api/v2/tx/{sprout.Value}", Tx(sprout, [(funding, 1, Payer, 600_000)], [], 400_000, "b5"));
        Answer(harness, $"api/v2/tx-specific/{sprout.Value}", Spec(
            sprout, [(funding, 1)], [], 400_000, "b5", joinsplits: [(590_000, 0)]));

        var transaction = (await harness.Zcash.GetTransactionAsync(harness.Case, sprout)).Value!;

        Assert.Equal(new Satoshi(590_000), transaction.Shielded!.IntoPool);
        Assert.Equal([ShieldedPool.Sprout], transaction.Shielded.Pools);
        Assert.Equal(new Satoshi(10_000), transaction.Fee);
    }

    [Fact]
    public async Task A_shielded_part_that_names_another_block_or_other_inputs_is_refused()
    {
        using var harness = new SourceHarness();
        Answer(harness, $"api/v2/tx/{Shielding.Value}", ShieldingJson());
        Answer(harness, $"api/v2/tx-specific/{Shielding.Value}", Spec(
            Shielding, [(Payment, 1)], [400_000], 3_000_005, "b2", sapling: -1_090_000));

        Assert.Null((await harness.Zcash.GetTransactionAsync(harness.Case, Shielding)).Value);

        using var other = new SourceHarness();
        Answer(other, $"api/v2/tx/{Shielding.Value}", ShieldingJson());
        Answer(other, $"api/v2/tx-specific/{Shielding.Value}", Spec(
            Shielding, [(Payment, 0)], [400_000], 3_000_006, "b9", sapling: -1_090_000));

        Assert.Null((await other.Zcash.GetTransactionAsync(other.Case, Shielding)).Value);
    }

    [Fact]
    public async Task A_window_that_fills_a_page_is_read_narrower_and_the_spender_found_there()
    {
        using var harness = new SourceHarness();
        AnswerPayment(harness);
        AnswerTip(harness, 3_001_000);
        Answer(harness, Window(Payee, 3_000_000, 1_152), Page(Payee, 1, -1, Busy()));
        Answer(harness, Window(Payee, 3_000_000, 288), Page(Payee, 1, -1, ShieldingJson(), PaymentJson()));
        Answer(harness, Window(Payer, 3_000_000, 1_152), Page(Payer, 1, -1, PaymentJson()));

        var spends = (await harness.Zcash.GetOutputSpendsAsync(harness.Case, Payment)).Value!;

        Assert.Equal(Shielding, spends[0].SpentBy!.Txid);
        Assert.False(spends[1].IsUnknown);
        Assert.Null(spends[1].SpentBy);
    }

    [Fact]
    public async Task An_unspent_output_is_read_window_by_window_up_to_the_tip()
    {
        using var harness = new SourceHarness();
        AnswerPayment(harness);
        AnswerTip(harness, 3_010_000);
        Answer(harness, Window(Payee, 3_000_000, 1_152), Page(Payee, 1, -1, ShieldingJson(), PaymentJson()));
        Answer(harness, Window(Payer, 3_000_000, 1_152), Page(Payer, 1, -1, PaymentJson()));
        Answer(harness, Window(Payer, 3_001_152, 4_608), Page(Payer, 1, -1));
        Answer(harness, Window(Payer, 3_005_760, 18_432), Page(Payer, 1, -1));

        var lookup = await harness.Zcash.GetOutputSpendsAsync(harness.Case, Payment);

        Assert.Null(lookup.Value![1].SpentBy);
        Assert.False(lookup.Value[1].IsUnknown);
        Assert.Null(lookup.Problem);
    }

    [Fact]
    public async Task An_output_whose_spender_lies_beyond_the_windows_read_is_unknown_and_the_others_are_kept()
    {
        using var harness = new SourceHarness();
        AnswerPayment(harness);
        AnswerTip(harness, 3_001_000);
        foreach (var span in new long[] { 1_152, 288, 72, 18, 4, 1 })
        {
            Answer(harness, Window(Payee, 3_000_000, span), Page(Payee, 1, -1, Busy()));
        }

        Answer(harness, Window(Payer, 3_000_000, 1_152), Page(Payer, 1, -1, PaymentJson()));

        var lookup = await harness.Zcash.GetOutputSpendsAsync(harness.Case, Payment);

        Assert.True(lookup.Value![0].IsUnknown);
        Assert.Null(lookup.Value[0].SpentBy);
        Assert.False(lookup.Value[1].IsUnknown);
        Assert.Contains("kunde inte läsas", lookup.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_filtered_page_without_a_count_has_more_when_it_is_full()
    {
        var full = BlockbookParser.ParseAddressPage(Encoding.UTF8.GetBytes(Page(Payee, 1, -1, Busy())), Payee);
        var part = BlockbookParser.ParseAddressPage(Encoding.UTF8.GetBytes(Page(Payee, 1, -1, PaymentJson())), Payee);
        var counted = BlockbookParser.ParseAddressPage(Encoding.UTF8.GetBytes(Page(Payee, 1, 2, PaymentJson())), Payee);

        Assert.True(full.More);
        Assert.False(part.More);
        Assert.True(counted.More);
        Assert.Throws<EsploraFormatException>(() => BlockbookParser.ParseAddressPage(
            Encoding.UTF8.GetBytes(Page(Payee, 1, -2, PaymentJson())), Payee));
    }

    [Fact]
    public void A_page_with_more_after_it_never_ends_inside_a_block()
    {
        var at = new[] { 10L, 9, 9 };
        var transactions = at.Select((height, index) => Confirmed(index, height)).ToList();

        var page = BlockbookZcashSource.EndAtWholeBlock(Payee, null, transactions, more: true);
        var whole = BlockbookZcashSource.EndAtWholeBlock(Payee, null, [Confirmed(0, 9), Confirmed(1, 9)], more: true);
        var last = BlockbookZcashSource.EndAtWholeBlock(Payee, null, transactions, more: false);

        Assert.Equal([transactions[0].Txid], page.Transactions.Select(transaction => transaction.Txid));
        Assert.Equal(transactions[0].Txid, page.NextAfter);
        Assert.Equal(2, whole.Transactions.Count);
        Assert.Null(last.NextAfter);
    }

    [Fact]
    public async Task The_page_after_a_transaction_is_asked_below_its_block()
    {
        using var harness = new SourceHarness();
        Answer(harness, $"api/v2/tx/{Payment.Value}", PaymentJson());
        Answer(harness, $"api/v2/address/{Payee.Value}?details=txs&pageSize=5&to=2999999", Page(Payee, 1, -1));

        var page = await harness.Zcash.GetAddressTransactionsAsync(harness.Case, Payee, Payment);

        Assert.NotNull(page.Value);
        Assert.Empty(page.Value.Transactions);
        Assert.Null(page.Value.NextAfter);
    }

    [Fact]
    public async Task A_full_page_below_a_block_without_a_count_has_a_next_page()
    {
        using var harness = new SourceHarness();
        Answer(harness, $"api/v2/tx/{Payment.Value}", PaymentJson());
        Answer(harness, $"api/v2/address/{Payee.Value}?details=txs&pageSize=5&to=2999999", Page(Payee, 1, -1, Busy()));

        var page = await harness.Zcash.GetAddressTransactionsAsync(harness.Case, Payee, Payment);

        Assert.Equal(5, page.Value!.Transactions.Count);
        Assert.NotNull(page.Value.NextAfter);
    }

    /// <summary>Answers the shielding's two answers, with Sapling's value balance as given.</summary>
    private static void AnswerShielding(SourceHarness harness, long saplingBalance)
    {
        Answer(harness, $"api/v2/tx/{Shielding.Value}", ShieldingJson());
        Answer(harness, $"api/v2/tx-specific/{Shielding.Value}", Spec(
            Shielding, [(Payment, 0)], [400_000], 3_000_005, "b2", sapling: saplingBalance));
        Answer(harness, "api/", """{"blockbook":{"coin":"Zcash","bestHeight":3000100},"backend":{} }""");
    }

    /// <summary>Answers the payment's two answers.</summary>
    private static void AnswerPayment(SourceHarness harness)
    {
        Answer(harness, $"api/v2/tx/{Payment.Value}", PaymentJson());
        Answer(harness, $"api/v2/tx-specific/{Payment.Value}", Spec(
            Payment, [(Txid.Parse(Chain.Zcash, Fixtures.Repeat("e0", 32)), 2)], [1_500_000, 490_000], 3_000_000, "b1"));
    }

    /// <summary>The payment, as Blockbook's <c>/api/v2/tx</c> writes it.</summary>
    private static string PaymentJson() => Tx(
        Payment,
        [(Txid.Parse(Chain.Zcash, Fixtures.Repeat("e0", 32)), 2, Payer, 2_000_000)],
        [(Payee, 1_500_000), (Payer, 490_000)],
        3_000_000,
        "b1");

    /// <summary>The shielding, with the input's index of 0 left out as Blockbook leaves it out.</summary>
    private static string ShieldingJson() => Tx(Shielding, [(Payment, 0, Payee, 1_500_000)], [(Payee, 400_000)], 3_000_005, "b2");

    /// <summary>A transaction that pays an address and spends nothing of the payment's.</summary>
    private static string Unrelated(string fill, BitcoinAddress address) => Tx(
        Txid.Parse(Chain.Zcash, Fixtures.Repeat(fill, 32)),
        [(Txid.Parse(Chain.Zcash, Fixtures.Repeat("ef", 32)), 1, Payer, 70_000)],
        [(address, 60_000)],
        3_000_050,
        "b6");

    /// <summary>A confirmed transaction at a height, for the paging rule.</summary>
    private static BitcoinTransaction Confirmed(int index, long height) => new(
        Txid.Parse(Chain.Zcash, Fixtures.Repeat(index.ToString("x2", System.Globalization.CultureInfo.InvariantCulture), 32)),
        new BlockReference(height, BlockHash.Parse(Fixtures.Repeat("b7", 32)), DateTimeOffset.UnixEpoch.AddDays(height)),
        [],
        [],
        Satoshi.Zero,
        0,
        0,
        5,
        0);

    /// <summary>Writes an input's output index as Blockbook does: left out when it is 0.</summary>
    private static string VoutPart(int vout) => vout == 0 ? string.Empty : "\"vout\":" + vout + ",";

    /// <summary>
    /// A transaction as Blockbook's <c>/api/v2/tx</c> writes it: an input's index of 0 left out, amounts as strings,
    /// and a coinbase input when there are no inputs and no outputs from the pool (an empty input list otherwise).
    /// </summary>
    private static string Tx(
        Txid txid, (Txid Previous, int Vout, BitcoinAddress Address, long Value)[] inputs,
        (BitcoinAddress Address, long Value)[] outputs, long height, string block)
    {
        var vin = string.Join(",", inputs.Select((input, n) =>
            $$"""{"txid":"{{input.Previous.Value}}",{{VoutPart(input.Vout)}}"sequence":4294967295,"n":{{n}},"addresses":["{{input.Address.Value}}"],"isAddress":true,"value":"{{input.Value}}"}"""));
        if (inputs.Length == 0 && txid.Value.StartsWith("c1", StringComparison.Ordinal))
        {
            vin = """{"n":0,"isAddress":false,"value":"0"}""";
        }

        var vout = string.Join(",", outputs.Select((output, n) =>
            $$"""{"value":"{{output.Value}}","n":{{n}},"hex":"{{Script(output.Address)}}","addresses":["{{output.Address.Value}}"],"isAddress":true}"""));
        return $$"""
            {"txid":"{{txid.Value}}","version":5,"vin":[{{vin}}],"vout":[{{vout}}],
             "blockHash":"{{Fixtures.Repeat(block, 32)}}","blockHeight":{{height}},"confirmations":10,
             "blockTime":{{1_790_000_000 + ((height - 3_000_000) * 75)}},"size":300,"value":"0","valueIn":"0","fees":"0"}
            """;
    }

    /// <summary>zcashd's own answer: the inputs, the outputs' amounts, the block and the pools' balances.</summary>
    private static string Spec(
        Txid txid, (Txid Previous, int Vout)[] inputs, long[] outputs, long height, string block, long sapling = 0,
        long orchard = 0, int orchardActions = 0, (long Old, long New)[]? joinsplits = null)
    {
        var vin = inputs.Length == 0 && txid.Value.StartsWith("c1", StringComparison.Ordinal)
            ? """{"coinbase":"03"}"""
            : string.Join(",", inputs.Select(input => $$"""{"txid":"{{input.Previous.Value}}","vout":{{input.Vout}} }"""));
        var vout = string.Join(",", outputs.Select((value, index) => $$"""{"valueZat":{{value}},"n":{{index}} }"""));
        var sprout = string.Join(",", (joinsplits ?? []).Select(js => $$"""{"vpub_oldZat":{{js.Old}},"vpub_newZat":{{js.New}} }"""));
        var spends = sapling > 0 ? "[{}]" : "[]";
        var outs = sapling < 0 ? "[{}]" : "[]";
        var actions = string.Join(",", Enumerable.Repeat("{}", orchardActions));
        return $$"""
            {"txid":"{{txid.Value}}","locktime":0,"vin":[{{vin}}],"vout":[{{vout}}],"vjoinsplit":[{{sprout}}],
             "vShieldedSpend":{{spends}},"vShieldedOutput":{{outs}},"valueBalanceZat":{{sapling}},
             "orchard":{"actions":[{{actions}}],"valueBalanceZat":{{orchard}} },
             "blockhash":"{{Fixtures.Repeat(block, 32)}}","height":{{height}} }
            """;
    }

    /// <summary>A page of an address's transactions.</summary>
    private static string Page(BitcoinAddress address, int page, int pages, params string[] transactions) => $$"""
        {"page":{{page}},"totalPages":{{pages}},"itemsOnPage":5,"address":"{{address.Value}}","balance":"0",
         "totalReceived":"0","totalSent":"0","unconfirmedBalance":"0","unconfirmedTxs":0,
         "txs":{{transactions.Length}},"transactions":[{{string.Join(",", transactions)}}]}
        """;

    /// <summary>The path of an address's history in a window of heights.</summary>
    private static string Window(BitcoinAddress address, long from, long span) =>
        $"api/v2/address/{address.Value}?details=txs&pageSize=5&from={from}&to={from + span - 1}";

    /// <summary>Answers the chain's height.</summary>
    private static void AnswerTip(SourceHarness harness, long height) => Answer(
        harness, "api/", $$"""{"blockbook":{"coin":"Zcash","bestHeight":{{height}}},"backend":{} }""");

    /// <summary>A full page (five) of transactions that pay the payee and spend nothing of the payment's.</summary>
    private static string[] Busy() => [.. new[] { "a1", "a2", "a3", "a4", "a5" }.Select(fill => Unrelated(fill, Payee))];

    /// <summary>Answers a path of the synthetic Blockbook with a body.</summary>
    private static void Answer(SourceHarness harness, string path, string body) =>
        harness.Http.AnswerBytes(SourceHarness.ZcashBase + path, Encoding.UTF8.GetBytes(body), 200);

    /// <summary>Gives a synthetic transparent P2PKH address of a repeated byte.</summary>
    private static BitcoinAddress Pkh(byte fill) => BitcoinAddress.FromHash(
        BitcoinScriptType.PayToPublicKeyHash, Enumerable.Repeat(fill, 20).ToArray(), BitcoinNetwork.Zcash);

    /// <summary>Writes the P2PKH script of an address's hash.</summary>
    private static string Script(BitcoinAddress address)
    {
        Assert.True(Base58Check.TryDecode(address.Value, out var data));
        return "76a914" + Convert.ToHexStringLower(data.AsSpan(2)) + "88ac";
    }
}
