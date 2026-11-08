using System.Globalization;
using System.Text;
using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Reads Esplora's JSON into Chainsaw's model of the chain, and checks that it adds up: the fee is the inputs minus
/// the outputs, every amount is within Bitcoin's supply, every script's address is what the script says, and a
/// confirmed transaction names its block. A response that does not add up is refused, never patched.
/// </summary>
/// <param name="network">The network the addresses belong to.</param>
internal sealed class EsploraParser(BitcoinNetwork network)
{
    /// <summary>The most satoshi there can ever be: 21 million bitcoin.</summary>
    private const long MaximumSatoshi = 21_000_000 * Satoshi.PerBitcoin;

    /// <summary>The weight units per byte outside the witness.</summary>
    private const int WitnessScaleFactor = 4;

    /// <summary>The coinbase's previous output index, which points at nothing.</summary>
    private const uint CoinbaseIndex = uint.MaxValue;

    /// <summary>Reads one transaction, as from <c>/tx/:txid</c>.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <param name="expected">The txid asked for; the response must be that transaction.</param>
    /// <returns>The transaction.</returns>
    /// <exception cref="EsploraFormatException">The response cannot be read or does not add up.</exception>
    public BitcoinTransaction ParseTransaction(ReadOnlySpan<byte> body, Txid expected)
    {
        using var document = Open(body);
        var transaction = ReadTransaction(document.RootElement);
        return transaction.Txid == expected
            ? transaction
            : throw new EsploraFormatException("The response is another transaction than the one asked for.");
    }

    /// <summary>Reads a list of transactions, as from <c>/address/:address/txs</c>.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <param name="address">The address asked for; every transaction must pay to it or spend from it.</param>
    /// <returns>The transactions, in the order of the response.</returns>
    /// <exception cref="EsploraFormatException">The response cannot be read or does not add up.</exception>
    public List<BitcoinTransaction> ParseAddressTransactions(ReadOnlySpan<byte> body, BitcoinAddress address)
    {
        var transactions = ParseTransactionList(body);
        if (transactions.Any(transaction => !Involves(transaction, address)))
        {
            throw new EsploraFormatException("A transaction in the response does not involve the address.");
        }

        if (transactions.DistinctBy(transaction => transaction.Txid).Count() != transactions.Count)
        {
            throw new EsploraFormatException("The response lists a transaction twice.");
        }

        return transactions;
    }

    /// <summary>
    /// Reads a list of transactions without asking whom they involve, for a response that was checked when it was
    /// fetched and is now read back from the case.
    /// </summary>
    /// <param name="body">The response's bytes.</param>
    /// <returns>The transactions, in the order of the response.</returns>
    /// <exception cref="EsploraFormatException">The response cannot be read or does not add up.</exception>
    public List<BitcoinTransaction> ParseTransactionList(ReadOnlySpan<byte> body)
    {
        using var document = Open(body);
        return [.. Array(document.RootElement).Select(ReadTransaction)];
    }

    /// <summary>
    /// Reads one transaction out of a list read back from the case, without reading the others: a graph reads each of
    /// its transactions from the address page it came on, and reading the whole page for each made a graph of a few
    /// hundred transactions take seconds to show.
    /// </summary>
    /// <param name="body">The response's bytes.</param>
    /// <param name="txid">The transaction.</param>
    /// <returns>The transaction, or <see langword="null"/> when the list does not have it.</returns>
    /// <exception cref="EsploraFormatException">The response cannot be read, or the transaction does not add up.
    /// </exception>
    public BitcoinTransaction? ParseTransactionInList(ReadOnlySpan<byte> body, Txid txid)
    {
        ArgumentNullException.ThrowIfNull(txid);
        using var document = Open(body);
        var element = Array(document.RootElement).FirstOrDefault(candidate =>
            candidate.ValueKind == JsonValueKind.Object
            && candidate.TryGetProperty("txid", out var id)
            && id.ValueKind == JsonValueKind.String
            && string.Equals(id.GetString(), txid.Value, StringComparison.OrdinalIgnoreCase));
        return element.ValueKind == JsonValueKind.Undefined ? null : ReadTransaction(element);
    }

    /// <summary>Reads which inputs spend a transaction's outputs, as from <c>/tx/:txid/outspends</c>.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <returns>One entry per output, in order.</returns>
    /// <exception cref="EsploraFormatException">The response cannot be read or does not add up.</exception>
    public static IReadOnlyList<BitcoinOutputSpend> ParseOutputSpends(ReadOnlySpan<byte> body)
    {
        using var document = Open(body);
        return
        [
            .. Array(document.RootElement)
                .Select((element, index) => new BitcoinOutputSpend(index, ReadSpend(element))),
        ];
    }

    /// <summary>Reads what the source says about an address as a whole, as from <c>/address/:address</c>.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <param name="expected">The address asked for; the response must be about it.</param>
    /// <returns>The summary.</returns>
    /// <exception cref="EsploraFormatException">The response cannot be read or does not add up.</exception>
    public static BitcoinAddressSummary ParseAddressSummary(ReadOnlySpan<byte> body, BitcoinAddress expected)
    {
        using var document = Open(body);
        var root = Object(document.RootElement);
        if (!string.Equals(String(root, "address"), expected.Value, StringComparison.OrdinalIgnoreCase))
        {
            throw new EsploraFormatException("The response is about another address than the one asked for.");
        }

        var confirmed = ReadStats(Property(root, "chain_stats"));
        if (confirmed.SpentOutputs > confirmed.FundedOutputs || confirmed.Spent.Value > confirmed.Funded.Value)
        {
            throw new EsploraFormatException("An address cannot have spent more in blocks than it received there.");
        }

        return new BitcoinAddressSummary(expected, confirmed, ReadStats(Property(root, "mempool_stats")));
    }

    /// <summary>
    /// Reads an address's counts and sums. The sums are not held to Bitcoin's supply, since money that passes an
    /// address many times can add up to more than exists.
    /// </summary>
    /// <param name="element">The stats object.</param>
    /// <returns>The stats.</returns>
    private static BitcoinAddressStats ReadStats(JsonElement element)
    {
        var stats = Object(element);
        return new BitcoinAddressStats(
            NonNegative(stats, "tx_count"),
            NonNegative(stats, "funded_txo_count"),
            new Satoshi(NonNegative(stats, "funded_txo_sum")),
            NonNegative(stats, "spent_txo_count"),
            new Satoshi(NonNegative(stats, "spent_txo_sum")));
    }

    /// <summary>Reads a block, as from <c>/block/:hash</c>.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <param name="expected">The hash asked for; the response must be that block.</param>
    /// <returns>The block.</returns>
    /// <exception cref="EsploraFormatException">The response cannot be read or does not add up.</exception>
    public static BitcoinBlock ParseBlock(ReadOnlySpan<byte> body, BlockHash expected)
    {
        using var document = Open(body);
        var root = Object(document.RootElement);
        var hash = Hash(root, "id");
        if (hash != expected)
        {
            throw new EsploraFormatException("The response is another block than the one asked for.");
        }

        var hasPrevious = root.TryGetProperty("previousblockhash", out var element)
            && element.ValueKind != JsonValueKind.Null;
        var previous = hasPrevious ? Hash(root, "previousblockhash") : null;
        var height = NonNegative(root, "height");
        if ((previous is null) != (height == 0))
        {
            throw new EsploraFormatException("Only the first block lacks a previous block.");
        }

        var merkleRoot = String(root, "merkle_root");
        if (!BlockHash.TryParse(merkleRoot, out var root32))
        {
            throw new EsploraFormatException("The merkle root is not 32 bytes of hex.");
        }

        return new BitcoinBlock(
            hash,
            height,
            UnixTime(root, "timestamp"),
            root.TryGetProperty("mediantime", out _) ? UnixTime(root, "mediantime") : null,
            previous,
            root32.Value,
            PositiveInt32(root, "tx_count"),
            Positive(root, "size"),
            Positive(root, "weight"),
            Integer(root, "version"));
    }

    /// <summary>Reads the chain's height, as from <c>/blocks/tip/height</c>, which is plain text.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <returns>The height.</returns>
    /// <exception cref="EsploraFormatException">The response is not a non-negative whole number.</exception>
    public static BitcoinTip ParseTipHeight(ReadOnlySpan<byte> body)
    {
        var text = Encoding.ASCII.GetString(body).Trim();
        return long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var height)
            ? new BitcoinTip(height)
            : throw new EsploraFormatException("The height is not a whole number.");
    }

    /// <summary>Reads the newest block's hash, as from <c>/blocks/tip/hash</c>, which is plain text.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <returns>The hash.</returns>
    /// <exception cref="EsploraFormatException">The response is not 64 hex characters.</exception>
    public static BlockHash ParseTipHash(ReadOnlySpan<byte> body) =>
        BlockHash.TryParse(Encoding.ASCII.GetString(body).Trim(), out var hash)
            ? hash
            : throw new EsploraFormatException("The hash is not 64 hex characters.");

    /// <summary>Reads a transaction object and checks that it adds up.</summary>
    /// <param name="element">The object.</param>
    /// <returns>The transaction.</returns>
    private BitcoinTransaction ReadTransaction(JsonElement element)
    {
        var root = Object(element);
        var txid = Txid(root, "txid");
        var inputs = Array(Property(root, "vin")).Select(ReadInput).ToList();
        var outputs = Array(Property(root, "vout")).Select(ReadOutput).ToList();
        if (inputs.Count == 0 || outputs.Count == 0)
        {
            throw new EsploraFormatException("A transaction has at least one input and one output.");
        }

        var isCoinbase = inputs[0].IsCoinbase;
        if (inputs.Skip(1).Any(input => input.IsCoinbase) || (isCoinbase && inputs.Count != 1))
        {
            throw new EsploraFormatException("A coinbase input must be the only input.");
        }

        var fee = new Satoshi(Amount(root, "fee"));
        CheckFee(inputs, outputs, fee, isCoinbase);
        var size = PositiveInt32(root, "size");
        var weight = PositiveInt32(root, "weight");
        if (weight < size || weight > (long)size * WitnessScaleFactor)
        {
            throw new EsploraFormatException("The weight does not fit the size.");
        }

        return new BitcoinTransaction(
            txid,
            ReadStatus(Property(root, "status")),
            inputs,
            outputs,
            fee,
            size,
            weight,
            Integer(root, "version"),
            UInt32(root, "locktime"));
    }

    /// <summary>Checks that the inputs are the outputs plus the fee; a coinbase has no fee of its own.</summary>
    /// <param name="inputs">The inputs.</param>
    /// <param name="outputs">The outputs.</param>
    /// <param name="fee">The fee the source reported.</param>
    /// <param name="isCoinbase">Whether the transaction is a coinbase.</param>
    private static void CheckFee(List<BitcoinInput> inputs, List<BitcoinOutput> outputs, Satoshi fee, bool isCoinbase)
    {
        var totalOut = Total(outputs.Select(output => output.Amount));
        if (totalOut.Value > MaximumSatoshi)
        {
            throw new EsploraFormatException("The outputs exceed Bitcoin's supply.");
        }

        if (isCoinbase)
        {
            if (fee != Satoshi.Zero)
            {
                throw new EsploraFormatException("A coinbase pays no fee.");
            }

            return;
        }

        var totalIn = Total(inputs.Select(input => input.Amount!.Value));
        if (totalIn.Value > MaximumSatoshi || totalIn - totalOut != fee)
        {
            throw new EsploraFormatException("The fee is not the inputs minus the outputs.");
        }
    }

    /// <summary>Adds amounts, refusing a sum that cannot be on the chain instead of overflowing.</summary>
    /// <param name="amounts">The amounts, each within Bitcoin's supply.</param>
    /// <returns>The sum.</returns>
    private static Satoshi Total(IEnumerable<Satoshi> amounts)
    {
        var total = Satoshi.Zero;
        foreach (var amount in amounts)
        {
            total += amount;
            if (total.Value > MaximumSatoshi)
            {
                throw new EsploraFormatException("The amounts exceed Bitcoin's supply.");
            }
        }

        return total;
    }

    /// <summary>Reads an input; a non-coinbase input must name the output it spends.</summary>
    /// <param name="element">The input object.</param>
    /// <param name="index">The input's place.</param>
    /// <returns>The input.</returns>
    private BitcoinInput ReadInput(JsonElement element, int index)
    {
        var input = Object(element);
        var sequence = UInt32(input, "sequence");
        if (Boolean(input, "is_coinbase"))
        {
            var hasPrevout = input.TryGetProperty("prevout", out var prevout)
                && prevout.ValueKind != JsonValueKind.Null;
            if (hasPrevout || UInt32(input, "vout") != CoinbaseIndex)
            {
                throw new EsploraFormatException("A coinbase input spends nothing.");
            }

            return new BitcoinInput(index, true, null, null, null, null, null, sequence);
        }

        var spent = Object(Property(input, "prevout"));
        var (type, address) = ReadScript(spent);
        var previousIndex = Integer(input, "vout");
        if (previousIndex is < 0 or > int.MaxValue)
        {
            throw new EsploraFormatException("The spent output's index is out of range.");
        }

        return new BitcoinInput(
            index, false, Txid(input, "txid"), (int)previousIndex, address, type, new Satoshi(Amount(spent, "value")),
            sequence);
    }

    /// <summary>Reads an output.</summary>
    /// <param name="element">The output object.</param>
    /// <param name="index">The output's place.</param>
    /// <returns>The output, without who spends it.</returns>
    private BitcoinOutput ReadOutput(JsonElement element, int index)
    {
        var output = Object(element);
        var (type, address) = ReadScript(output);
        var script = String(output, "scriptpubkey").ToLowerInvariant();
        return new BitcoinOutput(index, type, address, new Satoshi(Amount(output, "value")), script, null);
    }

    /// <summary>
    /// Classifies a script from its bytes and checks the source's address against the script's own.
    /// </summary>
    /// <param name="holder">The object with <c>scriptpubkey</c> and maybe <c>scriptpubkey_address</c>.</param>
    /// <returns>The kind and address.</returns>
    private (BitcoinScriptType Type, BitcoinAddress? Address) ReadScript(JsonElement holder)
    {
        byte[] script;
        try
        {
            script = Convert.FromHexString(String(holder, "scriptpubkey"));
        }
        catch (FormatException exception)
        {
            throw new EsploraFormatException("A script is not hex.", exception);
        }

        var (type, address) = ScriptClassifier.Classify(script, network);
        if (holder.TryGetProperty("scriptpubkey_address", out var reported) && reported.ValueKind != JsonValueKind.Null)
        {
            var text = reported.ValueKind == JsonValueKind.String ? reported.GetString() : null;
            if (address is null || !string.Equals(text, address.Value, StringComparison.OrdinalIgnoreCase))
            {
                throw new EsploraFormatException("The source's address does not match the script.");
            }
        }

        return (type, address);
    }

    /// <summary>Reads a transaction's status: its block when it is confirmed.</summary>
    /// <param name="element">The status object.</param>
    /// <returns>The block, or <see langword="null"/> when unconfirmed.</returns>
    private static BlockReference? ReadStatus(JsonElement element)
    {
        var status = Object(element);
        return Boolean(status, "confirmed")
            ? new BlockReference(
                NonNegative(status, "block_height"), Hash(status, "block_hash"), UnixTime(status, "block_time"))
            : null;
    }

    /// <summary>Reads one entry of <c>/outspends</c>.</summary>
    /// <param name="element">The entry.</param>
    /// <returns>The spending input, or <see langword="null"/> when the output is unspent.</returns>
    private static BitcoinSpend? ReadSpend(JsonElement element)
    {
        var spend = Object(element);
        if (!Boolean(spend, "spent"))
        {
            return null;
        }

        var inputIndex = Integer(spend, "vin");
        if (inputIndex is < 0 or > int.MaxValue)
        {
            throw new EsploraFormatException("The spending input's index is out of range.");
        }

        return new BitcoinSpend(Txid(spend, "txid"), (int)inputIndex, ReadStatus(Property(spend, "status")));
    }

    /// <summary>Checks whether a transaction pays to an address or spends from it.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool Involves(BitcoinTransaction transaction, BitcoinAddress address) =>
        transaction.Outputs.Any(output => output.Address == address)
        || transaction.Inputs.Any(input => input.Address == address);

    /// <summary>Parses a response as JSON.</summary>
    /// <param name="body">The bytes.</param>
    /// <returns>The document.</returns>
    private static JsonDocument Open(ReadOnlySpan<byte> body)
    {
        try
        {
            return JsonDocument.Parse(body.ToArray());
        }
        catch (JsonException exception)
        {
            throw new EsploraFormatException("The response is not JSON.", exception);
        }
    }

    /// <summary>Gives a property that must be there.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>Its value.</returns>
    private static JsonElement Property(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out var value)
            ? value
            : throw new EsploraFormatException($"The property {name} is missing.");

    /// <summary>Checks that an element is an object.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The same element.</returns>
    private static JsonElement Object(JsonElement element) =>
        element.ValueKind == JsonValueKind.Object ? element : throw new EsploraFormatException("Expected an object.");

    /// <summary>Gives the items of an array.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The items.</returns>
    private static IEnumerable<JsonElement> Array(JsonElement element) =>
        element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().ToList()
            : throw new EsploraFormatException("Expected an array.");

    /// <summary>Reads a string property.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The string.</returns>
    private static string String(JsonElement holder, string name)
    {
        var value = Property(holder, name);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new EsploraFormatException($"The property {name} is not a string.");
    }

    /// <summary>Reads a boolean property.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The value.</returns>
    private static bool Boolean(JsonElement holder, string name) => Property(holder, name).ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw new EsploraFormatException($"The property {name} is not true or false."),
    };

    /// <summary>Reads a whole-number property.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number.</returns>
    private static long Integer(JsonElement holder, string name)
    {
        var value = Property(holder, name);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : throw new EsploraFormatException($"The property {name} is not a whole number.");
    }

    /// <summary>Reads a whole number that is zero or more.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number.</returns>
    private static long NonNegative(JsonElement holder, string name)
    {
        var number = Integer(holder, name);
        return number >= 0 ? number : throw new EsploraFormatException($"The property {name} is negative.");
    }

    /// <summary>Reads a whole number that is one or more.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number.</returns>
    private static long Positive(JsonElement holder, string name)
    {
        var number = Integer(holder, name);
        return number > 0 ? number : throw new EsploraFormatException($"The property {name} is not positive.");
    }

    /// <summary>Reads a whole number that fits an unsigned 32-bit field, such as a sequence or a lock time.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number.</returns>
    private static uint UInt32(JsonElement holder, string name)
    {
        var number = Integer(holder, name);
        return number is >= 0 and <= uint.MaxValue
            ? (uint)number
            : throw new EsploraFormatException($"The property {name} is out of range.");
    }

    /// <summary>Reads a whole number from one up to the largest 32-bit integer, such as a size.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number.</returns>
    private static int PositiveInt32(JsonElement holder, string name)
    {
        var number = Positive(holder, name);
        return number <= int.MaxValue
            ? (int)number
            : throw new EsploraFormatException($"The property {name} is out of range.");
    }

    /// <summary>Reads an amount in satoshi, within Bitcoin's supply. Esplora writes amounts as whole satoshi.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The amount.</returns>
    private static long Amount(JsonElement holder, string name)
    {
        var amount = NonNegative(holder, name);
        return amount <= MaximumSatoshi
            ? amount
            : throw new EsploraFormatException($"The amount {name} exceeds Bitcoin's supply.");
    }

    /// <summary>Reads a Unix time in seconds.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The time in UTC.</returns>
    private static DateTimeOffset UnixTime(JsonElement holder, string name)
    {
        var seconds = NonNegative(holder, name);
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new EsploraFormatException($"The time {name} is out of range.", exception);
        }
    }

    /// <summary>Reads a txid property.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The txid.</returns>
    private static Txid Txid(JsonElement holder, string name) =>
        Contracts.Txid.TryParse(String(holder, name), out var txid)
            ? txid
            : throw new EsploraFormatException($"The property {name} is not a txid.");

    /// <summary>Reads a block hash property.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The hash.</returns>
    private static BlockHash Hash(JsonElement holder, string name) =>
        BlockHash.TryParse(String(holder, name), out var hash)
            ? hash
            : throw new EsploraFormatException($"The property {name} is not a block hash.");
}
