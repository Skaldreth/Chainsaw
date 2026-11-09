using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Reads Blockbook's JSON for Zcash (Trezor's indexer, API v2) into Chainsaw's model of the chain, and checks that it
/// adds up: every amount is within Zcash's supply, every script's address is what the script says, a confirmed
/// transaction names its block, and the shielded part (zcashd's own answer, <c>tx-specific</c>) agrees with the
/// transparent part. A response that does not add up is refused, never patched.
/// </summary>
/// <remarks>
/// Blockbook writes amounts as whole zatoshi in strings, leaves out an input's <c>vout</c> when it is 0, and gives an
/// input's address and value from the output it spends. Blockbook's own <c>fees</c> leave out the shielded pools,
/// so the fee is counted here from the pools' value balances.
/// </remarks>
internal static class BlockbookParser
{
    /// <summary>The most zatoshi there can ever be: 21 million ZEC.</summary>
    private const long MaximumUnits = 21_000_000 * Satoshi.PerBitcoin;

    /// <summary>
    /// The weight written for a Zcash transaction or block: Zcash has no witness and no weight, so none is made up; 0
    /// says that there is none.
    /// </summary>
    private const int NoWeight = 0;

    /// <summary>The network the addresses belong to.</summary>
    private const BitcoinNetwork Network = BitcoinNetwork.Zcash;

    /// <summary>Reads one transaction's transparent part, as from <c>/api/v2/tx/:txid</c>.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <param name="expected">The txid asked for; the response must be that transaction.</param>
    /// <returns>The transaction, without its shielded part.</returns>
    /// <exception cref="EsploraFormatException">The response cannot be read or does not add up.</exception>
    public static BitcoinTransaction ParseTransaction(ReadOnlySpan<byte> body, Txid expected)
    {
        using var document = Open(body);
        var transaction = ReadTransaction(document.RootElement);
        return transaction.Txid == expected.On(Chain.Zcash)
            ? transaction
            : throw new EsploraFormatException("The response is another transaction than the one asked for.");
    }

    /// <summary>Reads what a transaction moves between the transparent part and the shielded pools.</summary>
    /// <param name="body">The response's bytes, zcashd's own answer as from <c>/api/v2/tx-specific/:txid</c>.</param>
    /// <param name="transparent">The transaction's transparent part, which the answer must agree with.</param>
    /// <returns>The transaction with its fee and its shielded flow, when it touches a pool.</returns>
    /// <exception cref="EsploraFormatException">The answer cannot be read or does not agree.</exception>
    public static BitcoinTransaction WithShielded(ReadOnlySpan<byte> body, BitcoinTransaction transparent)
    {
        using var document = Open(body);
        var root = Object(document.RootElement);
        if (!Txid.TryParse(Chain.Zcash, String(root, "txid"), out var txid) || txid != transparent.Txid)
        {
            throw new EsploraFormatException("The shielded part is another transaction's.");
        }

        CheckOutputsAgree(root, transparent);
        CheckInputsAgree(root, transparent);
        CheckBlockAgrees(root, transparent);
        var pools = new List<ShieldedPool>();
        long net = 0;
        if (root.TryGetProperty("vjoinsplit", out var joinsplits) && joinsplits.ValueKind == JsonValueKind.Array
            && joinsplits.GetArrayLength() > 0)
        {
            pools.Add(ShieldedPool.Sprout);
            foreach (var joinsplit in joinsplits.EnumerateArray())
            {
                net = checked(net + Amount(joinsplit, "vpub_oldZat") - Amount(joinsplit, "vpub_newZat"));
            }
        }

        if (Count(root, "vShieldedSpend") + Count(root, "vShieldedOutput") > 0)
        {
            pools.Add(ShieldedPool.Sapling);
        }

        net = checked(net - OptionalBalance(root));
        if (root.TryGetProperty("orchard", out var orchard) && orchard.ValueKind == JsonValueKind.Object)
        {
            if (Count(orchard, "actions") > 0)
            {
                pools.Add(ShieldedPool.Orchard);
            }

            net = checked(net - OptionalBalance(orchard));
        }

        var fee = FeeOf(transparent, net, pools.Count > 0);
        return transparent with
        {
            Fee = new Satoshi(fee),
            Shielded = pools.Count == 0 && net == 0 ? null : ShieldedFlow.FromNet(net, pools),
            LockTime = UInt32(root, "locktime"),
        };
    }

    /// <summary>
    /// Counts a transaction's fee: what its transparent inputs pay beyond its transparent outputs and what goes into the
    /// pools. A coinbase pays no fee and has no inputs to count: its new coins go to transparent outputs and, since
    /// Heartwood, may go straight into a pool, so money into a pool is allowed there and never out of one.
    /// </summary>
    /// <param name="transparent">The transaction's transparent part.</param>
    /// <param name="net">The net amount into the pools, negative out of them.</param>
    /// <param name="touchesPool">Whether the transaction has any shielded part.</param>
    /// <returns>The fee in zatoshi; 0 for a coinbase.</returns>
    /// <exception cref="EsploraFormatException">The amounts do not add up.</exception>
    private static long FeeOf(BitcoinTransaction transparent, long net, bool touchesPool)
    {
        if (transparent.IsCoinbase)
        {
            return net < 0 || (net > 0 && !touchesPool)
                ? throw new EsploraFormatException("A coinbase takes nothing out of a pool.")
                : 0;
        }

        var transparentIn = transparent.Inputs.Aggregate(
            0L, (sum, input) => checked(sum + (input.Amount ?? throw MissingAmount()).Value));
        var fee = checked(transparentIn - transparent.TotalOut.Value - net);
        return fee >= 0
            ? fee
            : throw new EsploraFormatException("The transaction's amounts and its pools' value balances do not add up.");
    }

    /// <summary>Says that an input lacks the amount of the output it spends.</summary>
    /// <returns>The exception.</returns>
    private static EsploraFormatException MissingAmount() => new("An input lacks the amount it spends.");

    /// <summary>
    /// Checks that zcashd's inputs are Blockbook's: the same outputs spent in the same order, or a coinbase in both.
    /// </summary>
    /// <param name="root">zcashd's answer.</param>
    /// <param name="transparent">Blockbook's transaction.</param>
    private static void CheckInputsAgree(JsonElement root, BitcoinTransaction transparent)
    {
        var inputs = Array(root, "vin").ToList();
        for (var index = 0; index < inputs.Count; index++)
        {
            if (!InputAgrees(Object(inputs[index]), transparent.Inputs[index]))
            {
                throw new EsploraFormatException("The shielded part's inputs spend other outputs than the transaction's.");
            }
        }
    }

    /// <summary>Says whether one of zcashd's inputs is Blockbook's.</summary>
    /// <param name="theirs">zcashd's input.</param>
    /// <param name="mine">Blockbook's input.</param>
    /// <returns><see langword="true"/> when both spend the same output, or both are the coinbase.</returns>
    private static bool InputAgrees(JsonElement theirs, BitcoinInput mine)
    {
        if (theirs.TryGetProperty("coinbase", out _))
        {
            return mine.IsCoinbase;
        }

        return !mine.IsCoinbase
            && Txid.TryParse(Chain.Zcash, String(theirs, "txid"), out var previous)
            && previous == mine.PreviousTxid && Integer(theirs, "vout") == mine.PreviousIndex;
    }

    /// <summary>
    /// Checks that zcashd names the same block as Blockbook, or none while the transaction is unconfirmed, so that the
    /// two sealed answers are of the same transaction in the same place.
    /// </summary>
    /// <param name="root">zcashd's answer.</param>
    /// <param name="transparent">Blockbook's transaction.</param>
    private static void CheckBlockAgrees(JsonElement root, BitcoinTransaction transparent)
    {
        var named = root.TryGetProperty("blockhash", out var hash) && hash.ValueKind == JsonValueKind.String;
        var agrees = transparent.Block is { } block
            ? named && hash.GetString() == block.Hash.Value && Integer(root, "height") == block.Height
            : !named;
        if (!agrees)
        {
            throw new EsploraFormatException("The shielded part names another block than the transaction's.");
        }
    }

    /// <summary>Reads an address's page of transactions, as from <c>/api/v2/address/:address?details=txs</c>.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <param name="address">The address asked for.</param>
    /// <returns>The transactions, newest first, the page's number and how many pages there are.</returns>
    /// <exception cref="EsploraFormatException">The response cannot be read or is another address's.</exception>
    public static (List<BitcoinTransaction> Transactions, int Page, int Pages) ParseAddressPage(
        ReadOnlySpan<byte> body, BitcoinAddress address)
    {
        using var document = Open(body);
        var root = Object(document.RootElement);
        if (String(root, "address") != address.Value)
        {
            throw new EsploraFormatException("The page is another address's.");
        }

        var transactions = root.TryGetProperty("transactions", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray().Select(ReadTransaction).ToList()
            : [];
        if (transactions.DistinctBy(transaction => transaction.Txid).Count() != transactions.Count)
        {
            throw new EsploraFormatException("The page names a transaction twice.");
        }

        var page = Integer(root, "page");
        var pages = Integer(root, "totalPages");
        if (page < 1 || pages < 0 || page > Math.Max(pages, 1) || pages > int.MaxValue)
        {
            throw new EsploraFormatException("The page's number is not within its pages.");
        }

        return (transactions, (int)page, (int)pages);
    }

    /// <summary>Reads an address's summary, as from <c>/api/v2/address/:address?details=basic</c>.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <param name="address">The address asked for.</param>
    /// <returns>
    /// The summary. Blockbook does not count outputs, so the counts of funded and spent outputs are 0; only the
    /// amounts and the number of transactions are read.
    /// </returns>
    /// <exception cref="EsploraFormatException">The response cannot be read, does not add up or is another's.</exception>
    public static BitcoinAddressSummary ParseAddressSummary(ReadOnlySpan<byte> body, BitcoinAddress address)
    {
        using var document = Open(body);
        var root = Object(document.RootElement);
        if (String(root, "address") != address.Value)
        {
            throw new EsploraFormatException("The summary is another address's.");
        }

        var received = Amount(root, "totalReceived");
        var sent = Amount(root, "totalSent");
        var unconfirmed = Integer(root, "unconfirmedTxs");
        var transactions = Integer(root, "txs");
        if (sent > received || unconfirmed < 0 || transactions < 0 || unconfirmed > transactions)
        {
            throw new EsploraFormatException("The address's amounts do not add up.");
        }

        return new BitcoinAddressSummary(
            address,
            new BitcoinAddressStats(transactions - unconfirmed, 0, new Satoshi(received), 0, new Satoshi(sent)),
            new BitcoinAddressStats(unconfirmed, 0, Satoshi.Zero, 0, Satoshi.Zero));
    }

    /// <summary>Reads a block, as from <c>/api/v2/block/:hash</c>.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <param name="expected">The block asked for.</param>
    /// <returns>The block.</returns>
    /// <exception cref="EsploraFormatException">The response cannot be read or is another block.</exception>
    public static BitcoinBlock ParseBlock(ReadOnlySpan<byte> body, BlockHash expected)
    {
        using var document = Open(body);
        var root = Object(document.RootElement);
        var hash = Hash(root, "hash");
        if (hash != expected)
        {
            throw new EsploraFormatException("The response is another block than the one asked for.");
        }

        var size = Integer(root, "size");
        return new BitcoinBlock(
            hash,
            Positive(root, "height"),
            UnixTime(root, "time"),
            null,
            root.TryGetProperty("previousBlockHash", out _) ? Hash(root, "previousBlockHash") : null,
            String(root, "merkleRoot"),
            checked((int)Integer(root, "txCount")),
            size,
            NoWeight,
            Integer(root, "version"));
    }

    /// <summary>Reads the chain's height, as from <c>/api/</c>.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <returns>The height of the newest block Blockbook has indexed.</returns>
    /// <exception cref="EsploraFormatException">The response cannot be read.</exception>
    public static BitcoinTip ParseTip(ReadOnlySpan<byte> body)
    {
        using var document = Open(body);
        var blockbook = Property(Object(document.RootElement), "blockbook");
        return new BitcoinTip(Positive(blockbook, "bestHeight"));
    }

    /// <summary>Reads the hash of the block at a height, as from <c>/api/v2/block-index/:height</c>.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <returns>The hash.</returns>
    /// <exception cref="EsploraFormatException">The response cannot be read.</exception>
    public static BlockHash ParseBlockIndex(ReadOnlySpan<byte> body)
    {
        using var document = Open(body);
        return Hash(Object(document.RootElement), "blockHash");
    }

    /// <summary>Reads one transaction's transparent part.</summary>
    /// <param name="element">The transaction's object.</param>
    /// <returns>The transaction, with its transparent amounts' difference as its fee.</returns>
    private static BitcoinTransaction ReadTransaction(JsonElement element)
    {
        var root = Object(element);
        if (!Txid.TryParse(Chain.Zcash, String(root, "txid"), out var txid))
        {
            throw new EsploraFormatException("The property txid is not a txid.");
        }

        var inputs = Array(root, "vin").Select((input, index) => ReadInput(input, index)).ToList();
        var outputs = Array(root, "vout").Select((output, index) => ReadOutput(output, index)).ToList();
        var transparentIn = inputs.Aggregate(0L, (sum, input) => checked(sum + (input.Amount?.Value ?? 0)));
        var transparentOut = outputs.Aggregate(0L, (sum, output) => checked(sum + output.Amount.Value));
        if (transparentIn > MaximumUnits || transparentOut > MaximumUnits)
        {
            throw new EsploraFormatException("The amounts are beyond Zcash's supply.");
        }

        var size = root.TryGetProperty("size", out _) ? checked((int)Integer(root, "size")) : 0;
        var transaction = new BitcoinTransaction(
            txid,
            ReadBlock(root),
            inputs,
            outputs,
            new Satoshi(Math.Max(0, transparentIn - transparentOut)),
            size,
            NoWeight,
            Integer(root, "version"),
            0);
        if (transaction.IsCoinbase != inputs.Any(input => input.IsCoinbase))
        {
            throw new EsploraFormatException("A coinbase input is not the transaction's only input.");
        }

        return transaction;
    }

    /// <summary>Reads an input: the output it spends, with that output's address and value, or a coinbase.</summary>
    /// <param name="element">The input's object.</param>
    /// <param name="index">Its index.</param>
    /// <returns>The input.</returns>
    private static BitcoinInput ReadInput(JsonElement element, int index)
    {
        var input = Object(element);
        if (!input.TryGetProperty("txid", out _))
        {
            return new BitcoinInput(index, true, null, null, null, null, null, uint.MaxValue);
        }

        if (!Txid.TryParse(Chain.Zcash, String(input, "txid"), out var previous))
        {
            throw new EsploraFormatException("An input's txid is not a txid.");
        }

        // Blockbook leaves out an index of 0.
        var previousIndex = input.TryGetProperty("vout", out _) ? checked((int)Integer(input, "vout")) : 0;
        var address = OptionalAddress(input);
        var sequence = input.TryGetProperty("sequence", out _) ? UInt32(input, "sequence") : uint.MaxValue;
        return new BitcoinInput(
            index, false, previous, previousIndex, address, address?.Type, new Satoshi(Amount(input, "value")),
            sequence);
    }

    /// <summary>Reads an output: its script, the address the script pays and its value.</summary>
    /// <param name="element">The output's object.</param>
    /// <param name="index">Its place in the list, which must be its index.</param>
    /// <returns>The output, without who spends it (that is asked apart).</returns>
    private static BitcoinOutput ReadOutput(JsonElement element, int index)
    {
        var output = Object(element);
        if (Integer(output, "n") != index)
        {
            throw new EsploraFormatException("The outputs are not in order.");
        }

        var script = output.TryGetProperty("hex", out _) ? String(output, "hex") : string.Empty;
        var (type, address) = script.Length == 0
            ? (BitcoinScriptType.NonStandard, OptionalAddress(output))
            : Classify(script);
        if (script.Length > 0 && address?.Value != OptionalAddress(output)?.Value)
        {
            throw new EsploraFormatException("An output's address is not what its script pays.");
        }

        return new BitcoinOutput(index, type, address, new Satoshi(Amount(output, "value")), script, null);
    }

    /// <summary>Classifies a script given in hex.</summary>
    /// <param name="hex">The script.</param>
    /// <returns>The kind and its address.</returns>
    private static (BitcoinScriptType Type, BitcoinAddress? Address) Classify(string hex)
    {
        try
        {
            return ScriptClassifier.Classify(Convert.FromHexString(hex), Network);
        }
        catch (FormatException exception)
        {
            throw new EsploraFormatException("A script is not hex.", exception);
        }
    }

    /// <summary>Reads the transparent address Blockbook names, when it names one.</summary>
    /// <param name="holder">The input's or output's object.</param>
    /// <returns>The address, or <see langword="null"/> when it names none (a script without one).</returns>
    private static BitcoinAddress? OptionalAddress(JsonElement holder)
    {
        if (holder.TryGetProperty("isAddress", out var isAddress) && isAddress.ValueKind == JsonValueKind.False)
        {
            return null;
        }

        if (!holder.TryGetProperty("addresses", out var addresses) || addresses.ValueKind != JsonValueKind.Array
            || addresses.GetArrayLength() != 1)
        {
            return null;
        }

        return BitcoinAddress.TryParse(Chain.Zcash, addresses[0].GetString(), out var address)
            ? address
            : throw new EsploraFormatException("An address is not a transparent Zcash address.");
    }

    /// <summary>Reads the block a transaction is in, when it is in one.</summary>
    /// <param name="root">The transaction's object.</param>
    /// <returns>The block, or <see langword="null"/> while unconfirmed.</returns>
    private static BlockReference? ReadBlock(JsonElement root)
    {
        if (!root.TryGetProperty("blockHash", out _) || Integer(root, "blockHeight") <= 0)
        {
            return null;
        }

        return new BlockReference(Positive(root, "blockHeight"), Hash(root, "blockHash"), UnixTime(root, "blockTime"));
    }

    /// <summary>Checks that zcashd's outputs are Blockbook's: as many, with the same amounts.</summary>
    /// <param name="root">zcashd's answer.</param>
    /// <param name="transparent">Blockbook's transaction.</param>
    private static void CheckOutputsAgree(JsonElement root, BitcoinTransaction transparent)
    {
        var outputs = Array(root, "vout").ToList();
        if (outputs.Count != transparent.Outputs.Count
            || outputs.Where((output, index) => Amount(output, "valueZat") != transparent.Outputs[index].Amount.Value)
                .Any())
        {
            throw new EsploraFormatException("The shielded part's outputs are not the transaction's.");
        }

        var inputs = Array(root, "vin").ToList();
        if (inputs.Count != transparent.Inputs.Count)
        {
            throw new EsploraFormatException("The shielded part's inputs are not the transaction's.");
        }
    }

    /// <summary>Reads a value balance in zatoshi, which an older transaction may lack.</summary>
    /// <param name="holder">The object.</param>
    /// <returns>The balance; 0 when there is none.</returns>
    private static long OptionalBalance(JsonElement holder) =>
        holder.TryGetProperty("valueBalanceZat", out var balance) && balance.TryGetInt64(out var value)
            ? value
            : 0;

    /// <summary>Counts the entries of an array property, which may be missing.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The count; 0 when it is missing.</returns>
    private static int Count(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : 0;

    /// <summary>Opens a JSON response.</summary>
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

    /// <summary>Checks that an element is an object.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The same element.</returns>
    private static JsonElement Object(JsonElement element) => element.ValueKind == JsonValueKind.Object
        ? element
        : throw new EsploraFormatException("An object was expected.");

    /// <summary>Reads a property that must be there.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>Its value.</returns>
    private static JsonElement Property(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out var value)
            ? value
            : throw new EsploraFormatException($"The property {name} is missing.");

    /// <summary>Reads an array property that must be there.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>Its entries.</returns>
    private static IEnumerable<JsonElement> Array(JsonElement holder, string name)
    {
        var value = Property(holder, name);
        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().ToList()
            : throw new EsploraFormatException($"The property {name} is not a list.");
    }

    /// <summary>Reads a text property.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The text.</returns>
    private static string String(JsonElement holder, string name)
    {
        var value = Property(holder, name);
        return value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new EsploraFormatException($"The property {name} is not text.");
    }

    /// <summary>Reads a whole number property.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number.</returns>
    private static long Integer(JsonElement holder, string name) =>
        Property(holder, name).TryGetInt64(out var value)
            ? value
            : throw new EsploraFormatException($"The property {name} is not a whole number.");

    /// <summary>Reads a whole number property above zero.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number.</returns>
    private static long Positive(JsonElement holder, string name)
    {
        var value = Integer(holder, name);
        return value > 0 ? value : throw new EsploraFormatException($"The property {name} is not above zero.");
    }

    /// <summary>Reads a 32-bit unsigned property.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number.</returns>
    private static uint UInt32(JsonElement holder, string name) =>
        Property(holder, name).TryGetUInt32(out var value)
            ? value
            : throw new EsploraFormatException($"The property {name} is not a 32-bit number.");

    /// <summary>
    /// Reads an amount in zatoshi, which Blockbook writes as a string of digits and zcashd as a number, within
    /// Zcash's supply.
    /// </summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The amount.</returns>
    private static long Amount(JsonElement holder, string name)
    {
        var value = Property(holder, name);
        var read = value.ValueKind == JsonValueKind.String
            ? long.TryParse(value.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var text)
                ? text
                : -1
            : value.TryGetInt64(out var number) ? number : -1;
        return read is >= 0 and <= MaximumUnits
            ? read
            : throw new EsploraFormatException($"The amount {name} is not zatoshi within Zcash's supply.");
    }

    /// <summary>Reads a time in seconds since 1970.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The time.</returns>
    private static DateTimeOffset UnixTime(JsonElement holder, string name)
    {
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(Positive(holder, name));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new EsploraFormatException($"The time {name} is out of range.", exception);
        }
    }

    /// <summary>Reads a block hash property.</summary>
    /// <param name="holder">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The hash.</returns>
    private static BlockHash Hash(JsonElement holder, string name) =>
        BlockHash.TryParse(String(holder, name), out var hash)
            ? hash
            : throw new EsploraFormatException($"The property {name} is not a block hash.");
}
