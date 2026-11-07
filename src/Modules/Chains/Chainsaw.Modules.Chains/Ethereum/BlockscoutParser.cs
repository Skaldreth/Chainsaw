using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>A token transfer read from a transaction's logs, before its decimals are known.</summary>
/// <param name="Contract">The token's contract: the address that emitted the event.</param>
/// <param name="From">The address the tokens left.</param>
/// <param name="To">The address that received them.</param>
/// <param name="Units">The amount in the token's smallest unit.</param>
/// <param name="Position">The log's index in the block.</param>
internal sealed record TokenLog(EthereumAddress Contract, EthereumAddress From, EthereumAddress To, BigInteger Units,
    long Position);

/// <summary>A transaction as Blockscout's <c>gettxinfo</c> gives it, before its block's hash and tokens are
/// read.</summary>
/// <param name="Hash">The transaction.</param>
/// <param name="Height">The block's number, or <see langword="null"/> while unconfirmed.</param>
/// <param name="Time">The block's time, or <see langword="null"/>.</param>
/// <param name="From">The signer.</param>
/// <param name="To">Where it was sent, or <see langword="null"/> when it created a contract.</param>
/// <param name="Value">The ether it sent, in wei.</param>
/// <param name="Fee">The gas used times the price per gas, in wei.</param>
/// <param name="Succeeded">Whether it succeeded.</param>
/// <param name="Tokens">The ERC-20 <c>Transfer</c> events in its logs, in their order.</param>
internal sealed record TransactionInfo(
    EthereumTxHash Hash, long? Height, DateTimeOffset? Time, EthereumAddress From, EthereumAddress? To,
    BigInteger Value, BigInteger Fee, bool Succeeded, IReadOnlyList<TokenLog> Tokens);

/// <summary>An internal transaction as the source lists it.</summary>
/// <param name="From">The contract that sent the ether.</param>
/// <param name="To">The address that received it.</param>
/// <param name="Value">The ether, in wei.</param>
/// <param name="Position">Its index in the transaction's trace.</param>
internal sealed record InternalCall(EthereumAddress From, EthereumAddress To, BigInteger Value, long Position);

/// <summary>A transaction's internal transactions, and whether the source has read all of them.</summary>
/// <param name="Calls">The calls that moved ether and did not fail.</param>
/// <param name="IsIncomplete">Whether the source said it has not processed them all (status 2).</param>
internal sealed record InternalCalls(IReadOnlyList<InternalCall> Calls, bool IsIncomplete);

/// <summary>A token as the source describes it.</summary>
/// <param name="Asset">The token, by its contract, with the source's symbol and decimals.</param>
internal sealed record TokenInfo(Asset Asset);

/// <summary>
/// Reads Blockscout's answers and checks that they add up: every value is a whole number that is not negative, every
/// address and hash is well formed, the answer is about what was asked, and a page has only entries that touch its
/// address. Anything else throws <see cref="FormatException"/>, and the answer is sealed but never used.
/// </summary>
internal static class BlockscoutParser
{
    /// <summary>The Keccak-256 of <c>Transfer(address,address,uint256)</c>, the first topic of an ERC-20
    /// transfer.</summary>
    public static readonly string TransferTopic =
        "0x" + Convert.ToHexStringLower(Keccak256.Hash("Transfer(address,address,uint256)"));

    /// <summary>Reads the chain's height from a JSON-RPC answer, <c>{"result":"0x…"}</c>.</summary>
    /// <param name="body">The body.</param>
    /// <returns>The height.</returns>
    /// <exception cref="FormatException">It is not a height above zero.</exception>
    public static EthereumTip ParseHeight(byte[] body) => Read(body, root =>
    {
        var height = Quantity(String(root, "result"));
        return height > 0 && height <= long.MaxValue
            ? new EthereumTip((long)height)
            : throw new FormatException("The height is not a whole number above zero.");
    });

    /// <summary>Reads a block from <c>v2/blocks/:height</c>.</summary>
    /// <param name="body">The body.</param>
    /// <param name="height">The height asked for, or <see langword="null"/> when any will do (a probe).</param>
    /// <returns>The block's height, hash and time.</returns>
    /// <exception cref="FormatException">It is not that block.</exception>
    public static EthereumBlockReference ParseBlock(byte[] body, long? height) => Read(body, root =>
    {
        var hash = String(root, "hash");
        var number = root.TryGetProperty("height", out var h) && h.TryGetInt64(out var n)
            ? n
            : throw new FormatException("The block has no height.");
        if (!EthereumTxHash.IsHash(hash) || (height is { } asked && asked != number))
        {
            throw new FormatException("The block is not the one asked for.");
        }

        var time = DateTimeOffset.Parse(String(root, "timestamp"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return new EthereumBlockReference(number, hash.ToLowerInvariant(), time);
    });

    /// <summary>Reads a transaction from <c>module=transaction&amp;action=gettxinfo</c>.</summary>
    /// <param name="body">The body.</param>
    /// <param name="hash">The transaction asked for.</param>
    /// <returns>The transaction.</returns>
    /// <exception cref="FormatException">It does not add up, or it is another transaction.</exception>
    public static TransactionInfo ParseTransaction(byte[] body, EthereumTxHash hash) => Read(body, root =>
    {
        var result = Result(root);
        if (!string.Equals(String(result, "hash"), hash.Value, StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("The answer is about another transaction.");
        }

        var height = OptionalHeight(result, "blockNumber");
        var succeeded = result.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True;
        var tokens = new List<TokenLog>();
        if (succeeded && result.TryGetProperty("logs", out var logs) && logs.ValueKind == JsonValueKind.Array)
        {
            tokens.AddRange(logs.EnumerateArray().Select(TransferOf).OfType<TokenLog>());
        }

        return new TransactionInfo(
            hash,
            height,
            height is null ? null : Time(result),
            Address(String(result, "from")),
            OptionalAddress(result, "to"),
            Whole(result, "value"),
            Whole(result, "gasUsed") * Whole(result, "gasPrice"),
            succeeded,
            tokens);
    });

    /// <summary>Reads a transaction's internal transactions from <c>txlistinternal&amp;txhash=</c>.</summary>
    /// <param name="body">The body.</param>
    /// <param name="hash">The transaction asked for.</param>
    /// <returns>The calls that moved ether.</returns>
    /// <exception cref="FormatException">It does not add up, or lists another transaction.</exception>
    public static InternalCalls ParseInternal(byte[] body, EthereumTxHash hash) => Read(body, root =>
    {
        var (items, incomplete) = List(root);
        var calls = new List<InternalCall>();
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (TransactionOf(item) != hash.Value)
            {
                throw new FormatException("The list has another transaction's internal transactions.");
            }

            if (IsError(item) || Whole(item, "value").IsZero)
            {
                continue;
            }

            calls.Add(new InternalCall(Address(String(item, "from")), Receiver(item), Whole(item, "value"),
                PositionOf(item, i)));
        }

        return new InternalCalls(calls, incomplete);
    });

    /// <summary>Reads a token from <c>module=token&amp;action=getToken</c>.</summary>
    /// <param name="body">The body.</param>
    /// <param name="contract">The contract asked for.</param>
    /// <returns>The token with its decimals.</returns>
    /// <exception cref="FormatException">It is another contract, or has no decimals.</exception>
    public static TokenInfo ParseToken(byte[] body, EthereumAddress contract) => Read(body, root =>
    {
        var result = Result(root);
        if (Address(String(result, "contractAddress")) != contract)
        {
            throw new FormatException("The answer is about another token.");
        }

        return new TokenInfo(Token(contract, OptionalString(result, "symbol"), String(result, "decimals")));
    });

    /// <summary>Reads a page of an address's history, newest first.</summary>
    /// <param name="body">The body.</param>
    /// <param name="address">The address asked about.</param>
    /// <param name="history">Which history.</param>
    /// <param name="atOrBelow">The block the page goes back from, or <see langword="null"/>.</param>
    /// <param name="pageSize">How many entries were asked for; a full page has a next one.</param>
    /// <returns>The page.</returns>
    /// <exception cref="FormatException">An entry does not add up or does not touch the address.</exception>
    public static EthereumAddressPage ParseHistory(
        byte[] body, EthereumAddress address, EthereumHistory history, long? atOrBelow, int pageSize) =>
        Read(body, root =>
        {
            var (items, incomplete) = List(root);
            var transfers = new List<EthereumTransfer>();
            var fees = new List<EthereumFee>();
            var ordinals = new Dictionary<string, int>(StringComparer.Ordinal);
            var heights = new List<long>();
            foreach (var item in items)
            {
                var hash = EthereumTxHash.Parse(TransactionOf(item));
                var block = BlockOf(item);
                if (block is not null)
                {
                    heights.Add(block.Height);
                    if (atOrBelow is { } limit && block.Height > limit)
                    {
                        throw new FormatException("The page has an entry above the block it goes back from.");
                    }
                }

                var from = Address(String(item, "from"));
                var to = Receiver(item);
                if (from != address && to != address)
                {
                    throw new FormatException("The page has an entry that does not touch the address.");
                }

                var ordinal = ordinals[hash.Value] = ordinals.GetValueOrDefault(hash.Value, -1) + 1;
                incomplete |= !Read(item, history, hash, from, to, block, ordinal, address, transfers, fees);
            }

            long? next = null;
            var cutShort = false;
            if (items.Count >= pageSize && heights.Count > 0)
            {
                cutShort = heights.Min() == heights.Max();
                next = cutShort ? null : heights.Min();
            }

            return new EthereumAddressPage(address, history, atOrBelow, transfers, fees, next, cutShort, incomplete);
        });

    /// <summary>Reads one entry of a history into its transfer and fee.</summary>
    /// <param name="item">The entry.</param>
    /// <param name="history">Which history.</param>
    /// <param name="hash">Its transaction.</param>
    /// <param name="from">The address the value left.</param>
    /// <param name="to">The address that received it.</param>
    /// <param name="block">Its block.</param>
    /// <param name="ordinal">Its place among the page's entries of the same transaction.</param>
    /// <param name="address">The page's address.</param>
    /// <param name="transfers">Where a transfer is added.</param>
    /// <param name="fees">Where a fee is added.</param>
    /// <returns><see langword="false"/> when the entry could not be read whole (a token without decimals).</returns>
    private static bool Read(
        JsonElement item, EthereumHistory history, EthereumTxHash hash, EthereumAddress from, EthereumAddress to,
        EthereumBlockReference? block, int ordinal, EthereumAddress address, List<EthereumTransfer> transfers,
        List<EthereumFee> fees)
    {
        switch (history)
        {
            case EthereumHistory.Transactions:
                var succeeded = !IsError(item) && OptionalString(item, "txreceipt_status") != "0";
                var value = Whole(item, "value");
                if (succeeded && !value.IsZero)
                {
                    transfers.Add(new EthereumTransfer(hash, EthereumTransferKind.Native, from, to,
                        new AssetAmount(Asset.Ether, value), 0, block));
                }

                if (from == address)
                {
                    var fee = Whole(item, "gasUsed") * Whole(item, "gasPrice");
                    fees.Add(new EthereumFee(hash, from, new AssetAmount(Asset.Ether, fee), succeeded, block));
                }

                return true;
            case EthereumHistory.Internal:
                if (!IsError(item) && !Whole(item, "value").IsZero)
                {
                    transfers.Add(new EthereumTransfer(hash, EthereumTransferKind.Internal, from, to,
                        new AssetAmount(Asset.Ether, Whole(item, "value")), PositionOf(item, ordinal), block));
                }

                return true;
            default:
                var decimals = OptionalString(item, "tokenDecimal");
                if (string.IsNullOrEmpty(decimals))
                {
                    // Not an ERC-20 with decimals (an NFT, or a token the source has not read): nothing to count in.
                    return false;
                }

                var token = Token(Address(String(item, "contractAddress")), OptionalString(item, "tokenSymbol"),
                    decimals);
                transfers.Add(new EthereumTransfer(hash, EthereumTransferKind.Token, from, to,
                    new AssetAmount(token, Whole(item, "value")), LogPosition(item, ordinal), block));
                return true;
        }
    }

    /// <summary>Reads an ERC-20 <c>Transfer</c> from a log, or gives <see langword="null"/> for any other
    /// event.</summary>
    /// <param name="log">The log.</param>
    /// <returns>The transfer, or <see langword="null"/>.</returns>
    private static TokenLog? TransferOf(JsonElement log)
    {
        if (!log.TryGetProperty("topics", out var topicsElement) || topicsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var topics = topicsElement.EnumerateArray()
            .Where(topic => topic.ValueKind == JsonValueKind.String).Select(topic => topic.GetString()!).ToList();

        // ERC-20's Transfer has three topics and the amount as one word of data; ERC-721's has the token id as a
        // fourth topic, which is not an amount, so it is not read.
        var data = OptionalString(log, "data") ?? string.Empty;
        if (topics.Count != 3 || !string.Equals(topics[0], TransferTopic, StringComparison.OrdinalIgnoreCase)
            || data.Length != 66)
        {
            return null;
        }

        return new TokenLog(
            Address(String(log, "address")), Word(topics[1]), Word(topics[2]), Quantity(data),
            long.Parse(String(log, "index"), NumberStyles.None, CultureInfo.InvariantCulture));
    }

    /// <summary>Reads an address from a 32-byte topic, which must be zero above its 20 bytes.</summary>
    /// <param name="topic">The topic, <c>0x</c> and 64 hex digits.</param>
    /// <returns>The address.</returns>
    /// <exception cref="FormatException">It is not an address padded with zeros.</exception>
    private static EthereumAddress Word(string topic) =>
        EthereumTxHash.IsHash(topic) && topic.AsSpan(2, 24).IndexOfAnyExcept('0') < 0
            ? Address("0x" + topic[26..])
            : throw new FormatException("A topic is not an address.");

    /// <summary>Makes a token of a contract, a symbol and decimals, recognising USDT and USDC by contract.</summary>
    /// <param name="contract">The contract.</param>
    /// <param name="symbol">The symbol the source gave, if any.</param>
    /// <param name="decimals">The decimals as text.</param>
    /// <returns>The token.</returns>
    /// <exception cref="FormatException">The decimals are not a whole number Chainsaw can count in.</exception>
    private static Asset Token(EthereumAddress contract, string? symbol, string decimals)
    {
        if (!int.TryParse(decimals, NumberStyles.None, CultureInfo.InvariantCulture, out var places)
            || places > Asset.MaximumDecimals)
        {
            throw new FormatException("The token's decimals are not a whole number Chainsaw can count in.");
        }

        var shown = string.IsNullOrWhiteSpace(symbol) ? "token" : symbol.Trim();
        return KnownTokens.Canonical(Asset.Token(Chain.Ethereum, contract.Value, shown, places));
    }

    /// <summary>Reads a body as JSON and turns every failure into a <see cref="FormatException"/>.</summary>
    /// <typeparam name="T">What is read.</typeparam>
    /// <param name="body">The body.</param>
    /// <param name="read">Reads the root.</param>
    /// <returns>What was read.</returns>
    private static T Read<T>(byte[] body, Func<JsonElement, T> read)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            return read(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or ArgumentException or OverflowException)
        {
            throw new FormatException("The answer is not what Blockscout gives.", exception);
        }
    }

    /// <summary>Gives the result of an Etherscan-style answer whose status is 1.</summary>
    /// <param name="root">The answer.</param>
    /// <returns>The result.</returns>
    /// <exception cref="FormatException">The source said it failed.</exception>
    private static JsonElement Result(JsonElement root) =>
        OptionalString(root, "status") == "1" && root.TryGetProperty("result", out var result)
            && result.ValueKind == JsonValueKind.Object
            ? result
            : throw new FormatException("The source did not answer: " + OptionalString(root, "message"));

    /// <summary>
    /// Gives the entries of an Etherscan-style list: status 1 with entries, 2 with entries the source has not finished
    /// (internal transactions), or 0 with an empty list (nothing found). Any other answer is the source's failure,
    /// such as a rate limit.
    /// </summary>
    /// <param name="root">The answer.</param>
    /// <returns>The entries and whether they are incomplete.</returns>
    /// <exception cref="FormatException">The source said it failed.</exception>
    private static (IReadOnlyList<JsonElement> Items, bool Incomplete) List(JsonElement root)
    {
        var status = OptionalString(root, "status");
        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array
            || status is not ("0" or "1" or "2") || (status == "0" && result.GetArrayLength() > 0))
        {
            throw new FormatException("The source did not answer: " + OptionalString(root, "message"));
        }

        return ([.. result.EnumerateArray()], status == "2");
    }

    /// <summary>Gives an entry's transaction, which Blockscout and Etherscan name differently.</summary>
    /// <param name="item">The entry.</param>
    /// <returns>The hash in lower case.</returns>
    private static string TransactionOf(JsonElement item) =>
        (OptionalString(item, "hash") ?? String(item, "transactionHash")).ToLowerInvariant();

    /// <summary>Gives an entry's receiver: <c>to</c>, or the contract it created.</summary>
    /// <param name="item">The entry.</param>
    /// <returns>The receiver.</returns>
    /// <exception cref="FormatException">There is none.</exception>
    private static EthereumAddress Receiver(JsonElement item) =>
        OptionalAddress(item, "to") ?? OptionalAddress(item, "contractAddress")
        ?? throw new FormatException("The entry has no receiver.");

    /// <summary>Gives an entry's block, with its hash when the entry has it.</summary>
    /// <param name="item">The entry.</param>
    /// <returns>The block, or <see langword="null"/> while unconfirmed.</returns>
    private static EthereumBlockReference? BlockOf(JsonElement item)
    {
        if (OptionalHeight(item, "blockNumber") is not { } height)
        {
            return null;
        }

        var hash = OptionalString(item, "blockHash");
        return new EthereumBlockReference(height, EthereumTxHash.IsHash(hash) ? hash!.ToLowerInvariant() : null,
            Time(item));
    }

    /// <summary>Gives an internal transaction's place in its trace, or its order on the page.</summary>
    /// <param name="item">The entry.</param>
    /// <param name="ordinal">Its order among the transaction's entries.</param>
    /// <returns>The position.</returns>
    private static long PositionOf(JsonElement item, int ordinal) =>
        long.TryParse(OptionalString(item, "index"), NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            ? index
            : ordinal;

    /// <summary>Gives a token transfer's log index, or its order on the page when the source does not give
    /// it.</summary>
    /// <param name="item">The entry.</param>
    /// <param name="ordinal">Its order among the transaction's entries.</param>
    /// <returns>The position.</returns>
    private static long LogPosition(JsonElement item, int ordinal) =>
        long.TryParse(OptionalString(item, "logIndex"), NumberStyles.None, CultureInfo.InvariantCulture, out var log)
            ? log
            : ordinal;

    /// <summary>Says whether the source marked an entry as failed.</summary>
    /// <param name="item">The entry.</param>
    /// <returns><see langword="true"/> when it failed.</returns>
    private static bool IsError(JsonElement item) => OptionalString(item, "isError") == "1";

    /// <summary>Reads a block's time, in seconds since 1970 as text or number.</summary>
    /// <param name="item">The entry.</param>
    /// <returns>The time in UTC.</returns>
    private static DateTimeOffset Time(JsonElement item)
    {
        var element = item.GetProperty("timeStamp");
        var seconds = element.ValueKind == JsonValueKind.Number
            ? element.GetInt64()
            : long.Parse(element.GetString()!, NumberStyles.None, CultureInfo.InvariantCulture);
        return DateTimeOffset.FromUnixTimeSeconds(seconds);
    }

    /// <summary>Reads a block number that may be missing or empty (an unconfirmed transaction).</summary>
    /// <param name="item">The entry.</param>
    /// <param name="name">The property.</param>
    /// <returns>The height, or <see langword="null"/>.</returns>
    private static long? OptionalHeight(JsonElement item, string name)
    {
        var text = OptionalString(item, name);
        return string.IsNullOrEmpty(text)
            ? null
            : long.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
    }

    /// <summary>Reads a whole number that is not negative, in decimal text.</summary>
    /// <param name="item">The entry.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number.</returns>
    private static BigInteger Whole(JsonElement item, string name) =>
        BigInteger.TryParse(String(item, name), NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new FormatException($"{name} is not a whole number.");

    /// <summary>Reads a JSON-RPC quantity or a data word, <c>0x</c> and hex, as a whole number.</summary>
    /// <param name="hex">The text.</param>
    /// <returns>The number.</returns>
    private static BigInteger Quantity(string hex) =>
        hex.StartsWith("0x", StringComparison.Ordinal) && hex.Length > 2
            && BigInteger.TryParse("0" + hex[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                out var value)
            ? value
            : throw new FormatException("A quantity is 0x and hex.");

    /// <summary>Reads an address property.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The address.</returns>
    private static EthereumAddress Address(string text) => EthereumAddress.Parse(text);

    /// <summary>Reads an address property that may be missing, null or empty.</summary>
    /// <param name="item">The entry.</param>
    /// <param name="name">The property.</param>
    /// <returns>The address, or <see langword="null"/>.</returns>
    private static EthereumAddress? OptionalAddress(JsonElement item, string name)
    {
        var text = OptionalString(item, name);
        return string.IsNullOrEmpty(text) ? null : Address(text);
    }

    /// <summary>Reads a string property that must be there.</summary>
    /// <param name="item">The entry.</param>
    /// <param name="name">The property.</param>
    /// <returns>The text.</returns>
    private static string String(JsonElement item, string name) =>
        OptionalString(item, name) ?? throw new FormatException($"{name} is missing.");

    /// <summary>Reads a string property that may be missing or null.</summary>
    /// <param name="item">The entry.</param>
    /// <param name="name">The property.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    private static string? OptionalString(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
