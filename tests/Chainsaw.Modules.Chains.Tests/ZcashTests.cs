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
        Answer(harness, $"api/v2/tx/{Payment.Value}", PaymentJson());
        Answer(harness, $"api/v2/tx-specific/{Payment.Value}", Specific(Payment, [1_500_000, 490_000], 0));
        Answer(harness, History(Payee, 3_000_000), Page(Payee, ShieldingJson(), PaymentJson()));
        Answer(harness, History(Payer, 3_000_000), Page(Payer, PaymentJson()));

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
        Answer(harness, $"api/v2/address/{Payee.Value}?details=txs&pageSize=5", Page(Payee, ShieldingJson(), PaymentJson()));
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

    /// <summary>Answers the shielding's two answers, with Sapling's value balance as given.</summary>
    private static void AnswerShielding(SourceHarness harness, long saplingBalance)
    {
        Answer(harness, $"api/v2/tx/{Shielding.Value}", ShieldingJson());
        Answer(harness, $"api/v2/tx-specific/{Shielding.Value}", Specific(Shielding, [400_000], saplingBalance));
        Answer(harness, "api/", """{"blockbook":{"coin":"Zcash","bestHeight":3000100},"backend":{} }""");
    }

    /// <summary>The payment, as Blockbook's <c>/api/v2/tx</c> writes it.</summary>
    private static string PaymentJson() => $$"""
        {"txid":"{{Payment.Value}}","version":4,
         "vin":[{"txid":"{{Fixtures.Repeat("e0", 32)}}","vout":2,"sequence":4294967295,"n":0,
                 "addresses":["{{Payer.Value}}"],"isAddress":true,"value":"2000000"}],
         "vout":[{"value":"1500000","n":0,"spent":true,"hex":"{{Script(Payee)}}","addresses":["{{Payee.Value}}"],
                  "isAddress":true},
                 {"value":"490000","n":1,"hex":"{{Script(Payer)}}","addresses":["{{Payer.Value}}"],"isAddress":true}],
         "blockHash":"{{Fixtures.Repeat("b1", 32)}}","blockHeight":3000000,"confirmations":101,
         "blockTime":1790000000,"size":225,"value":"1990000","valueIn":"2000000","fees":"10000"}
        """;

    /// <summary>The shielding, with the input's index of 0 left out as Blockbook leaves it out.</summary>
    private static string ShieldingJson() => $$"""
        {"txid":"{{Shielding.Value}}","version":5,
         "vin":[{"txid":"{{Payment.Value}}","sequence":4294967295,"n":0,"addresses":["{{Payee.Value}}"],
                 "isAddress":true,"value":"1500000"}],
         "vout":[{"value":"400000","n":0,"hex":"{{Script(Payee)}}","addresses":["{{Payee.Value}}"],"isAddress":true}],
         "blockHash":"{{Fixtures.Repeat("b2", 32)}}","blockHeight":3000005,"confirmations":96,
         "blockTime":1790000375,"size":2000,"value":"400000","valueIn":"1500000","fees":"1100000"}
        """;

    /// <summary>zcashd's own answer: the outputs' amounts and Sapling's value balance.</summary>
    private static string Specific(Txid txid, long[] outputs, long saplingBalance)
    {
        var vout = string.Join(",", outputs.Select((value, index) => $$"""{"valueZat":{{value}},"n":{{index}}}"""));
        var spends = saplingBalance == 0 ? "[]" : "[{}]";
        return $$"""
            {"txid":"{{txid.Value}}","locktime":0,"vin":[{}],"vout":[{{vout}}],"vjoinsplit":[],
             "vShieldedSpend":[],"vShieldedOutput":{{spends}},"valueBalanceZat":{{saplingBalance}},
             "orchard":{"actions":[],"valueBalanceZat":0} }
            """;
    }

    /// <summary>An address's page of transactions, in one page.</summary>
    private static string Page(BitcoinAddress address, params string[] transactions) => $$"""
        {"page":1,"totalPages":1,"itemsOnPage":5,"address":"{{address.Value}}","balance":"0",
         "totalReceived":"0","totalSent":"0","unconfirmedBalance":"0","unconfirmedTxs":0,
         "txs":{{transactions.Length}},"transactions":[{{string.Join(",", transactions)}}]}
        """;

    /// <summary>The path of an address's history from a height.</summary>
    private static string History(BitcoinAddress address, long height) =>
        $"api/v2/address/{address.Value}?details=txs&pageSize=5&from={height}";

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
