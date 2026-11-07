using System.Text;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Tests;

public sealed class ParserTests
{
    private readonly EsploraParser _parser = new(BitcoinNetwork.Mainnet);

    [Fact]
    public void A_confirmed_transaction_is_read_as_on_the_chain()
    {
        var transaction = _parser.ParseTransaction(Fixtures.Bytes("tx-confirmed.json"), Fixtures.Confirmed);

        Assert.Equal(Fixtures.Confirmed, transaction.Txid);
        Assert.True(transaction.IsConfirmed);
        Assert.False(transaction.IsCoinbase);
        Assert.Equal(800_000, transaction.Block!.Height);
        Assert.Equal(Fixtures.Block, transaction.Block.Hash);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_700_000_000), transaction.Block.Time);
        Assert.Equal(2, transaction.Version);
        Assert.Equal(799_999u, transaction.LockTime);
        Assert.Equal(400, transaction.Size);
        Assert.Equal(1000, transaction.Weight);
        Assert.Equal(250, transaction.VirtualSize);

        Assert.Equal(2, transaction.Inputs.Count);
        var first = transaction.Inputs[0];
        Assert.Equal(Txid.Parse(Fixtures.Repeat("b1", 32)), first.PreviousTxid);
        Assert.Equal(0, first.PreviousIndex);
        Assert.Equal(Fixtures.KeyHash, first.Address);
        Assert.Equal(BitcoinScriptType.PayToPublicKeyHash, first.ScriptType);
        Assert.Equal(new Satoshi(60_000_000), first.Amount);
        Assert.Equal(4_294_967_293u, first.Sequence);
        Assert.Equal(Fixtures.Older, transaction.Inputs[1].PreviousTxid);
        Assert.Equal(1, transaction.Inputs[1].PreviousIndex);
        Assert.Equal(Fixtures.WitnessKeyHash, transaction.Inputs[1].Address);

        Assert.Equal(
            [BitcoinScriptType.PayToWitnessScriptHash, BitcoinScriptType.PayToTaproot, BitcoinScriptType.OpReturn],
            transaction.Outputs.Select(output => output.ScriptType));
        Assert.Equal([0, 1, 2], transaction.Outputs.Select(output => output.Index));
        Assert.Equal(Fixtures.WitnessScriptHash, transaction.Outputs[0].Address);
        Assert.Equal(Fixtures.Taproot, transaction.Outputs[1].Address);
        Assert.Null(transaction.Outputs[2].Address);
        Assert.Equal(Satoshi.Zero, transaction.Outputs[2].Amount);
        Assert.Equal(Satoshi.FromBitcoin(0.7m), transaction.Outputs[0].Amount);
        Assert.Equal("6a0b68656c6c6f20776f726c64", transaction.Outputs[2].Script);
        Assert.All(transaction.Outputs, output => Assert.Null(output.SpentBy));
    }

    [Theory]
    [InlineData("tx-confirmed.json")]
    [InlineData("tx-unconfirmed.json")]
    [InlineData("tx-older.json")]
    public void The_inputs_are_the_outputs_plus_the_fee(string fixture)
    {
        var transaction = _parser.ParseTransactionList(Encoding.UTF8.GetBytes($"[{Fixtures.Text(fixture)}]"))[0];

        var totalIn = transaction.Inputs.Aggregate(Satoshi.Zero, (sum, input) => sum + input.Amount!.Value);

        Assert.Equal(totalIn, transaction.TotalOut + transaction.Fee);
        Assert.True(transaction.Fee.Value > 0);
    }

    [Fact]
    public void The_confirmed_transactions_fee_is_exact()
    {
        var transaction = _parser.ParseTransaction(Fixtures.Bytes("tx-confirmed.json"), Fixtures.Confirmed);

        Assert.Equal(new Satoshi(10_000), transaction.Fee);
        Assert.Equal("0.99990000", transaction.TotalOut.ToString());
    }

    [Fact]
    public void An_unconfirmed_transaction_has_no_block()
    {
        var transaction = _parser.ParseTransaction(Fixtures.Bytes("tx-unconfirmed.json"), Fixtures.Unconfirmed);

        Assert.False(transaction.IsConfirmed);
        Assert.Null(transaction.Block);
        Assert.Equal(new Satoshi(5_000), transaction.Fee);
        Assert.Equal(Fixtures.Confirmed, transaction.Inputs[0].PreviousTxid);
        Assert.Equal(BitcoinScriptType.PayToTaproot, transaction.Inputs[0].ScriptType);
    }

    [Fact]
    public void A_coinbase_spends_nothing_and_pays_no_fee()
    {
        var transaction = _parser.ParseTransaction(Fixtures.Bytes("tx-coinbase.json"), Fixtures.Coinbase);

        Assert.True(transaction.IsCoinbase);
        var input = Assert.Single(transaction.Inputs);
        Assert.True(input.IsCoinbase);
        Assert.Null(input.PreviousTxid);
        Assert.Null(input.PreviousIndex);
        Assert.Null(input.Address);
        Assert.Null(input.Amount);
        Assert.Equal(Satoshi.Zero, transaction.Fee);
        Assert.Equal(new Satoshi(626_234_567), transaction.TotalOut);
        Assert.Equal(BitcoinScriptType.OpReturn, transaction.Outputs[1].ScriptType);
    }

    [Theory]
    [InlineData("tx-bad-fee.json")]
    [InlineData("tx-bad-address.json")]
    [InlineData("tx-bad-amount.json")]
    public void A_transaction_that_does_not_add_up_is_refused(string fixture)
    {
        Assert.Throws<EsploraFormatException>(
            () => _parser.ParseTransaction(Fixtures.Bytes(fixture), Fixtures.Confirmed));
    }

    [Fact]
    public void Another_transaction_than_the_one_asked_for_is_refused()
    {
        Assert.Throws<EsploraFormatException>(() =>
            _parser.ParseTransaction(Fixtures.Bytes("tx-confirmed.json"), Fixtures.Unconfirmed));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("""{"txid": 5}""")]
    public void Unreadable_responses_are_refused(string body)
    {
        Assert.Throws<EsploraFormatException>(() =>
            _parser.ParseTransaction(Encoding.UTF8.GetBytes(body), Fixtures.Confirmed));
    }

    [Fact]
    public void A_negative_amount_is_refused()
    {
        var body = Fixtures.Text("tx-unconfirmed.json")
            .Replace("\"value\": 20000000", "\"value\": -20000000", StringComparison.Ordinal);

        Assert.Throws<EsploraFormatException>(() =>
            _parser.ParseTransaction(Encoding.UTF8.GetBytes(body), Fixtures.Unconfirmed));
    }

    [Fact]
    public void A_fractional_amount_is_refused()
    {
        var body = Fixtures.Text("tx-unconfirmed.json")
            .Replace("\"value\": 20000000", "\"value\": 0.2", StringComparison.Ordinal);

        Assert.Throws<EsploraFormatException>(() =>
            _parser.ParseTransaction(Encoding.UTF8.GetBytes(body), Fixtures.Unconfirmed));
    }

    [Fact]
    public void A_confirmed_status_without_its_block_is_refused()
    {
        var body = Fixtures.Text("tx-confirmed.json").Replace("\"block_hash\"", "\"other\"", StringComparison.Ordinal);

        Assert.Throws<EsploraFormatException>(() =>
            _parser.ParseTransaction(Encoding.UTF8.GetBytes(body), Fixtures.Confirmed));
    }

    [Fact]
    public void An_address_page_is_read_in_order()
    {
        var transactions = _parser.ParseAddressTransactions(
            Fixtures.Bytes("address-page-first.json"), Fixtures.WitnessKeyHash);

        Assert.Equal([Fixtures.Unconfirmed, Fixtures.Coinbase, Fixtures.Confirmed], transactions.Select(tx => tx.Txid));
    }

    [Fact]
    public void An_address_page_with_a_transaction_that_does_not_involve_the_address_is_refused()
    {
        Assert.Throws<EsploraFormatException>(() =>
            _parser.ParseAddressTransactions(Fixtures.Bytes("address-page-first.json"), Fixtures.ScriptHash));
    }

    [Fact]
    public void Output_spends_are_read()
    {
        var spends = EsploraParser.ParseOutputSpends(Fixtures.Bytes("outspends-open.json"));

        Assert.Equal(3, spends.Count);
        Assert.Null(spends[0].SpentBy);
        Assert.Equal(Fixtures.Unconfirmed, spends[1].SpentBy!.Txid);
        Assert.Equal(0, spends[1].SpentBy!.InputIndex);
        Assert.Null(spends[1].SpentBy!.Block);
        var settled = EsploraParser.ParseOutputSpends(Fixtures.Bytes("outspends-settled.json"));
        Assert.Equal(800_003, settled[1].SpentBy!.Block!.Height);
    }

    [Fact]
    public void A_block_is_read()
    {
        var block = EsploraParser.ParseBlock(Fixtures.Bytes("block.json"), Fixtures.Block);

        Assert.Equal(Fixtures.Block, block.Hash);
        Assert.Equal(800_000, block.Height);
        Assert.Equal(3000, block.TransactionCount);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_699_998_000), block.MedianTime);
        Assert.NotNull(block.PreviousHash);
        Assert.Equal(Fixtures.Repeat("5e", 32), block.MerkleRoot);
        Assert.Equal(536_870_912, block.Version);
    }

    [Fact]
    public void Another_block_than_the_one_asked_for_is_refused()
    {
        var other = BlockHash.Parse(Fixtures.Repeat("ff", 32));

        Assert.Throws<EsploraFormatException>(() => EsploraParser.ParseBlock(Fixtures.Bytes("block.json"), other));
    }

    [Fact]
    public void Only_the_first_block_lacks_a_previous_block()
    {
        var body = Fixtures.Text("block.json")
            .Replace("\"previousblockhash\"", "\"ignored\"", StringComparison.Ordinal);

        Assert.Throws<EsploraFormatException>(() =>
            EsploraParser.ParseBlock(Encoding.UTF8.GetBytes(body), Fixtures.Block));
    }

    [Theory]
    [InlineData("800010", 800_010)]
    [InlineData("800010\n", 800_010)]
    public void The_tip_height_is_read_from_text(string body, long height)
    {
        Assert.Equal(height, EsploraParser.ParseTipHeight(Encoding.ASCII.GetBytes(body)).Height);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("8e5")]
    [InlineData("")]
    public void A_tip_height_that_is_not_a_whole_number_is_refused(string body)
    {
        Assert.Throws<EsploraFormatException>(() => EsploraParser.ParseTipHeight(Encoding.ASCII.GetBytes(body)));
    }
}
