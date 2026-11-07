#if DEBUG
using System.Text.Json;

namespace Chainsaw.App.TestData;

/// <summary>Writes the made-up chain in Esplora's JSON, as mempool.space and Blockstream answer.</summary>
public static class EsploraJson
{
    /// <summary>A coinbase input's txid: all zeros.</summary>
    private static readonly string NoTxid = new('0', 64);

    /// <summary>Writes a list of transactions (an address page).</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="chain">The chain, for the blocks.</param>
    /// <param name="transactions">The transactions.</param>
    public static void List(Utf8JsonWriter writer, TestChain chain, IEnumerable<TestTransaction> transactions)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStartArray();
        foreach (var transaction in transactions)
        {
            Transaction(writer, chain, transaction);
        }

        writer.WriteEndArray();
    }

    /// <summary>Writes one transaction.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="chain">The chain, for the block.</param>
    /// <param name="transaction">The transaction.</param>
    public static void Transaction(Utf8JsonWriter writer, TestChain chain, TestTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(transaction);
        writer.WriteStartObject();
        writer.WriteString("txid", transaction.Txid);
        writer.WriteNumber("version", TestTransaction.Version);
        writer.WriteNumber("locktime", 0);
        writer.WriteStartArray("vin");
        foreach (var input in transaction.Inputs)
        {
            Input(writer, transaction, input);
        }

        writer.WriteEndArray();
        writer.WriteStartArray("vout");
        foreach (var output in transaction.Outputs)
        {
            Output(writer, output);
        }

        writer.WriteEndArray();
        writer.WriteNumber("size", transaction.Size);
        writer.WriteNumber("weight", transaction.Weight);
        writer.WriteNumber("fee", transaction.Fee);
        writer.WritePropertyName("status");
        Status(writer, chain, transaction.Height);
        writer.WriteEndObject();
    }

    /// <summary>Writes which input spends each of a transaction's outputs.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="transaction">The transaction.</param>
    public static void Spends(Utf8JsonWriter writer, TestChain chain, TestTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(transaction);
        writer.WriteStartArray();
        for (var index = 0; index < transaction.Outputs.Count; index++)
        {
            writer.WriteStartObject();
            if (chain.SpentBy(transaction.Txid, index) is { } spend)
            {
                writer.WriteBoolean("spent", true);
                writer.WriteString("txid", spend.By.Txid);
                writer.WriteNumber("vin", spend.Input);
                writer.WritePropertyName("status");
                Status(writer, chain, spend.By.Height);
            }
            else
            {
                writer.WriteBoolean("spent", false);
            }

            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    /// <summary>Writes what an address has received and spent in all.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="address">The address.</param>
    /// <param name="script">The address's script, or <see langword="null"/> when the chain has never paid it.</param>
    /// <param name="history">The address's transactions.</param>
    public static void Summary(
        Utf8JsonWriter writer, TestChain chain, string address, string? script,
        IReadOnlyList<TestTransaction> history)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(chain);
        ArgumentNullException.ThrowIfNull(history);
        var funded = history.SelectMany(transaction => transaction.Outputs.Select((output, index) =>
            (Transaction: transaction, Output: output, Index: index)))
            .Where(item => item.Output.To.Script == script)
            .ToList();
        var spent = funded.Where(item => chain.SpentBy(item.Transaction.Txid, item.Index) is not null).ToList();
        writer.WriteStartObject();
        writer.WriteString("address", address);
        writer.WriteStartObject("chain_stats");
        writer.WriteNumber("funded_txo_count", funded.Count);
        writer.WriteNumber("funded_txo_sum", funded.Sum(item => item.Output.Value));
        writer.WriteNumber("spent_txo_count", spent.Count);
        writer.WriteNumber("spent_txo_sum", spent.Sum(item => item.Output.Value));
        writer.WriteNumber("tx_count", history.Count);
        writer.WriteEndObject();
        writer.WriteStartObject("mempool_stats");
        foreach (var name in (string[])["funded_txo_count", "funded_txo_sum", "spent_txo_count", "spent_txo_sum",
            "tx_count"])
        {
            writer.WriteNumber(name, 0);
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>Writes a block.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="block">The block.</param>
    public static void Block(Utf8JsonWriter writer, TestBlock block)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(block);
        writer.WriteStartObject();
        writer.WriteString("id", block.Hash);
        writer.WriteNumber("height", block.Height);
        writer.WriteNumber("version", System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(block.Header));
        writer.WriteNumber("timestamp", block.Time.ToUnixTimeSeconds());
        writer.WriteNumber("tx_count", block.Transactions);
        writer.WriteNumber("size", 1_000 * block.Transactions);
        writer.WriteNumber("weight", 4_000 * block.Transactions);
        writer.WriteString("merkle_root", block.MerkleRoot);
        writer.WriteString("previousblockhash", block.Previous);
        writer.WriteEndObject();
    }

    /// <summary>Writes an input, with the coin it spends as Esplora's <c>prevout</c>.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="transaction">The input's transaction, for a coinbase's script.</param>
    /// <param name="input">The input.</param>
    private static void Input(Utf8JsonWriter writer, TestTransaction transaction, TestInput input)
    {
        writer.WriteStartObject();
        if (input.IsCoinbase)
        {
            writer.WriteString("txid", NoTxid);
            writer.WriteNumber("vout", TestTransaction.Final);
            writer.WriteNull("prevout");
            writer.WriteString("scriptsig", Convert.ToHexStringLower(transaction.CoinbaseScript()));
            writer.WriteBoolean("is_coinbase", true);
            writer.WriteNumber("sequence", TestTransaction.Final);
        }
        else
        {
            writer.WriteString("txid", input.PreviousTxid);
            writer.WriteNumber("vout", input.PreviousIndex);
            writer.WritePropertyName("prevout");
            Output(writer, input.Spent!);
            writer.WriteString("scriptsig", string.Empty);
            writer.WriteBoolean("is_coinbase", false);
            writer.WriteNumber("sequence", TestTransaction.Sequence);
        }

        writer.WriteEndObject();
    }

    /// <summary>Writes an output: its script, its address when it has one, and its value.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="output">The output.</param>
    private static void Output(Utf8JsonWriter writer, TestOutput output)
    {
        writer.WriteStartObject();
        writer.WriteString("scriptpubkey", output.To.Script);
        if (output.To.Address is { } address)
        {
            writer.WriteString("scriptpubkey_address", address.Value);
        }

        writer.WriteNumber("value", output.Value);
        writer.WriteEndObject();
    }

    /// <summary>Writes a confirmed status in a block.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="height">The block's height.</param>
    private static void Status(Utf8JsonWriter writer, TestChain chain, long height)
    {
        var block = chain.BlockAt(height)
            ?? throw new InvalidOperationException("The made-up chain is not sealed up to the transaction.");
        writer.WriteStartObject();
        writer.WriteBoolean("confirmed", true);
        writer.WriteNumber("block_height", block.Height);
        writer.WriteString("block_hash", block.Hash);
        writer.WriteNumber("block_time", block.Time.ToUnixTimeSeconds());
        writer.WriteEndObject();
    }
}
#endif
