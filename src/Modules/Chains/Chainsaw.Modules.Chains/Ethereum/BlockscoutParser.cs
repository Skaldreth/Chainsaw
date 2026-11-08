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
/// <param name="Receiver">
/// Who receives its ether: <paramref name="To"/>, or the contract it created; <see langword="null"/> when the source
/// gave neither.
/// </param>
/// <param name="Value">The ether it sent, in wei.</param>
/// <param name="Fee">The gas used times the price per gas, in wei.</param>
/// <param name="Outcome">How it ended.</param>
/// <param name="Tokens">The ERC-20 <c>Transfer</c> events in its logs that move something, in their order.</param>
/// <param name="Unread">
/// How many logs with the <c>Transfer</c> topic could not be read as an ERC-20 transfer (another shape, such as an old
/// token's, or an address that is not one); the transaction is then incomplete.
/// </param>
/// <param name="LogsCut">
/// Whether the answer did not have all of a successful transaction's logs: the source pages them
/// (<c>next_page_params</c>) or gave none. The transaction is then incomplete and asked for again.
/// </param>
internal sealed record TransactionInfo(
    EthereumTxHash Hash, long? Height, DateTimeOffset? Time, EthereumAddress From, EthereumAddress? To,
    EthereumAddress? Receiver, BigInteger Value, BigInteger Fee, EthereumOutcome Outcome,
    IReadOnlyList<TokenLog> Tokens, int Unread, bool LogsCut);

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
/// <param name="Asset">
/// The token, by its contract, with the source's symbol and decimals; <see langword="null"/> when it has no decimals
/// Chainsaw can count in (anyone can make a contract say 255).
/// </param>
internal sealed record TokenInfo(Asset? Asset);

/// <summary>An address's ether as the source gave it.</summary>
/// <param name="Wei">The ether, in wei.</param>
internal sealed record EtherBalance(BigInteger Wei);

/// <summary>An address's ERC-20 tokens as the source listed them.</summary>
/// <param name="Tokens">The tokens with a balance above zero, USDT and USDC first and then by symbol.</param>
/// <param name="Skipped">How many entries are not counted: an NFT, or a token without decimals to count in.</param>
/// <param name="IsIncomplete">Whether the source said it has not read all of the address's tokens yet (status 2).</param>
internal sealed record TokenBalances(IReadOnlyList<AssetAmount> Tokens, int Skipped, bool IsIncomplete);

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
    public static EthereumBlockReference ParseBlock(byte[] body, long? height) =>
        Read(body, root => BlockFrom(root, height));

    /// <summary>
    /// Reads the newest block the source has read from <c>v2/blocks?type=block</c>, a list of blocks newest first:
    /// its height, hash and time from one entry, so that the height and the hash always belong together.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <returns>The newest block.</returns>
    /// <exception cref="FormatException">The list has no block, or its first does not add up.</exception>
    public static ChainTip ParseNewestBlock(byte[] body) => Read(body, root =>
    {
        if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array
            || items.GetArrayLength() == 0)
        {
            throw new FormatException("The list has no block.");
        }

        var block = BlockFrom(items[0], null);
        return block.Height > 0
            ? new ChainTip(block.Height, block.Hash!, block.Time)
            : throw new FormatException("The newest block is not above zero.");
    });

    /// <summary>
    /// Reads an address's ether from <c>module=account&amp;action=balance</c>: status 1 and the wei as decimal text.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <returns>The ether.</returns>
    /// <exception cref="FormatException">The source did not answer with a whole number that is not negative.</exception>
    public static EtherBalance ParseBalance(byte[] body) => Read(body, root =>
    {
        if (OptionalString(root, "status") == "1"
            && BigInteger.TryParse(OptionalString(root, "result"), NumberStyles.None, CultureInfo.InvariantCulture,
                out var wei))
        {
            return new EtherBalance(wei);
        }

        ThrowIfBusy(root);
        throw new FormatException("The source did not answer: " + OptionalString(root, "message"));
    });

    /// <summary>
    /// Reads an address's tokens from <c>module=account&amp;action=tokenlist</c>: only ERC-20 tokens with decimals
    /// Chainsaw can count in and a balance above zero are given; NFTs and tokens without decimals are counted as
    /// skipped. USDT and USDC are recognised by their contract, never by their symbol.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <returns>The tokens.</returns>
    /// <exception cref="FormatException">An entry does not add up, or the source did not answer.</exception>
    public static TokenBalances ParseTokenBalances(byte[] body) => Read(body, root =>
    {
        var (items, incomplete) = List(root);
        var tokens = new List<AssetAmount>();
        var skipped = 0;
        foreach (var item in items)
        {
            var asset = OptionalString(item, "type") == "ERC-20"
                ? Token(Address(String(item, "contractAddress")), OptionalString(item, "symbol"),
                    OptionalString(item, "decimals"))
                : null;
            if (asset is null)
            {
                skipped++;
                continue;
            }

            var units = Whole(item, "balance");
            if (!units.IsZero)
            {
                tokens.Add(new AssetAmount(asset, units));
            }
        }

        if (tokens.DistinctBy(token => token.Asset).Count() != tokens.Count)
        {
            throw new FormatException("A token is listed twice.");
        }

        return new TokenBalances(
            [
                .. tokens.OrderBy(token => KnownTokens.IssuerOf(token.Asset) is null)
                    .ThenBy(token => token.Asset.Symbol, StringComparer.Ordinal)
                    .ThenBy(token => token.Asset.Contract, StringComparer.Ordinal),
            ],
            skipped,
            incomplete);
    });

    /// <summary>Reads a block object as Blockscout's v2 API gives it: its height, hash and time.</summary>
    /// <param name="block">The block.</param>
    /// <param name="height">The height asked for, or <see langword="null"/> when any will do.</param>
    /// <returns>The block.</returns>
    /// <exception cref="FormatException">It is not that block, or does not add up.</exception>
    private static EthereumBlockReference BlockFrom(JsonElement block, long? height)
    {
        var hash = String(block, "hash");
        var number = block.TryGetProperty("height", out var h) && h.TryGetInt64(out var n)
            ? n
            : throw new FormatException("The block has no height.");
        if (!EthereumTxHash.IsHash(hash) || (height is { } asked && asked != number))
        {
            throw new FormatException("The block is not the one asked for.");
        }

        var time = DateTimeOffset.Parse(String(block, "timestamp"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        return new EthereumBlockReference(number, hash.ToLowerInvariant(), time);
    }

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
        var outcome = OutcomeOf(result, height);
        var tokens = new List<TokenLog>();
        var unread = 0;
        var hasLogs = result.TryGetProperty("logs", out var logs) && logs.ValueKind == JsonValueKind.Array;
        var morePages = result.TryGetProperty("next_page_params", out var next) && next.ValueKind != JsonValueKind.Null;
        var logsCut = outcome == EthereumOutcome.Succeeded && (!hasLogs || morePages);
        if (outcome == EthereumOutcome.Succeeded && hasLogs)
        {
            foreach (var log in logs.EnumerateArray())
            {
                var (transfer, unreadable) = TransferOf(log);
                unread += unreadable ? 1 : 0;
                if (transfer is not null)
                {
                    tokens.Add(transfer);
                }
            }
        }

        var to = OptionalAddress(result, "to");
        return new TransactionInfo(
            hash,
            height,
            height is null ? null : Time(result),
            Address(String(result, "from")),
            to,
            to ?? OptionalAddress(result, "contractAddress") ?? OptionalAddress(result, "created_contract"),
            Whole(result, "value"),
            outcome == EthereumOutcome.Pending ? BigInteger.Zero : Whole(result, "gasUsed") * Whole(result, "gasPrice"),
            outcome,
            tokens,
            unread,
            logsCut);
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

            if (!MovesEther(item))
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

        return new TokenInfo(Token(contract, OptionalString(result, "symbol"), OptionalString(result, "decimals")));
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
            var skipped = 0;
            var unread = 0;
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

                if (history == EthereumHistory.Internal && !MovesEther(item))
                {
                    // A failed or empty call moves nothing, and a failed creation has no receiver at all.
                    continue;
                }

                var from = Address(String(item, "from"));
                var to = OptionalReceiver(item);
                if (to is null && history == EthereumHistory.Transactions && from == address
                    && (!Succeeded(item) || Whole(item, "value").IsZero))
                {
                    // A failed creation can lack a receiver; it moved nothing, but its fee was paid.
                    AddFee(item, hash, from, block, fees);
                    continue;
                }

                if (to is null)
                {
                    throw new FormatException("The entry has no receiver.");
                }
                if (from != address && to != address)
                {
                    throw new FormatException("The page has an entry that does not touch the address.");
                }

                var ordinal = ordinals[hash.Value] = ordinals.GetValueOrDefault(hash.Value, -1) + 1;
                if (history == EthereumHistory.Tokens && !HasDecimals(item))
                {
                    // The source may not have read the token yet: not counted now, and the page is asked for again.
                    unread++;
                    continue;
                }

                skipped += Read(item, history, hash, from, to, block, ordinal, address, transfers, fees) ? 0 : 1;
            }

            long? next = null;
            var cutShort = false;
            if (items.Count >= pageSize)
            {
                // A full page in one block, or of unconfirmed entries only, cannot be paged past by block.
                cutShort = heights.Count == 0 || heights.Min() == heights.Max();
                next = cutShort ? null : heights.Min();
            }

            return new EthereumAddressPage(address, history, atOrBelow, transfers, fees, next, cutShort, incomplete)
            {
                Skipped = skipped,
                Unread = unread,
            };
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
    /// <returns>
    /// <see langword="false"/> when the entry is not counted: an NFT, a token without decimals Chainsaw can count in,
    /// or a token transfer of zero.
    /// </returns>
    private static bool Read(
        JsonElement item, EthereumHistory history, EthereumTxHash hash, EthereumAddress from, EthereumAddress to,
        EthereumBlockReference? block, int ordinal, EthereumAddress address, List<EthereumTransfer> transfers,
        List<EthereumFee> fees)
    {
        switch (history)
        {
            case EthereumHistory.Transactions:
                var succeeded = Succeeded(item);
                var value = Whole(item, "value");
                if (succeeded && !value.IsZero)
                {
                    transfers.Add(new EthereumTransfer(hash, EthereumTransferKind.Native, from, to,
                        new AssetAmount(Asset.Ether, value), 0, block));
                }

                if (from == address)
                {
                    AddFee(item, hash, from, block, fees);
                }

                return true;
            case EthereumHistory.Internal:
                if (MovesEther(item))
                {
                    transfers.Add(new EthereumTransfer(hash, EthereumTransferKind.Internal, from, to,
                        new AssetAmount(Asset.Ether, Whole(item, "value")), PositionOf(item, ordinal), block));
                }

                return true;
            default:
                // An NFT, a token whose decimals Chainsaw cannot count in (anyone can make a contract say 255) and a
                // transfer of zero (address poisoning) are not counted, and never make the page unreadable.
                var token = Token(Address(String(item, "contractAddress")), OptionalString(item, "tokenSymbol"),
                    OptionalString(item, "tokenDecimal"));
                if (token is null || !BigInteger.TryParse(OptionalString(item, "value"), NumberStyles.None,
                    CultureInfo.InvariantCulture, out var units) || units.IsZero)
                {
                    return false;
                }

                transfers.Add(new EthereumTransfer(hash, EthereumTransferKind.Token, from, to,
                    new AssetAmount(token, units), LogPosition(item, ordinal), block));
                return true;
        }
    }

    /// <summary>
    /// Reads an ERC-20 <c>Transfer</c> from a log. Any other event is not one; an ERC-721 <c>Transfer</c> (four topics:
    /// the fourth is a token id, not an amount) and a transfer of zero (anyone can make a token contract write one from
    /// any address, address poisoning) are not counted. A log with the topic in any other shape (an old token's, or a
    /// topic that is not an address) is unread, so that one log a launderer wrote never makes the transaction
    /// unreadable.
    /// </summary>
    /// <param name="log">The log.</param>
    /// <returns>The transfer or <see langword="null"/>, and whether it is a Transfer that could not be read.</returns>
    private static (TokenLog? Transfer, bool Unread) TransferOf(JsonElement log)
    {
        if (!log.TryGetProperty("topics", out var topicsElement) || topicsElement.ValueKind != JsonValueKind.Array)
        {
            return (null, false);
        }

        var topics = topicsElement.EnumerateArray()
            .Where(topic => topic.ValueKind == JsonValueKind.String).Select(topic => topic.GetString()!).ToList();
        if (topics.Count == 0 || !string.Equals(topics[0], TransferTopic, StringComparison.OrdinalIgnoreCase)
            || topics.Count == 4)
        {
            return (null, false);
        }

        var data = OptionalString(log, "data") ?? string.Empty;
        var contract = OptionalAddress(log, "address");
        if (topics.Count != 3 || data.Length != 66 || contract is null
            || !long.TryParse(OptionalString(log, "index"), NumberStyles.None, CultureInfo.InvariantCulture,
                out var index)
            || Word(topics[1]) is not { } from || Word(topics[2]) is not { } to
            || !TryQuantity(data, out var units))
        {
            return (null, true);
        }

        return units.IsZero ? (null, false) : (new TokenLog(contract, from, to, units, index), false);
    }

    /// <summary>Reads an address from a 32-byte topic, which must be zero above its 20 bytes.</summary>
    /// <param name="topic">The topic, <c>0x</c> and 64 hex digits.</param>
    /// <returns>The address, or <see langword="null"/> when it is not one padded with zeros.</returns>
    private static EthereumAddress? Word(string topic) =>
        EthereumTxHash.IsHash(topic) && topic.AsSpan(2, 24).IndexOfAnyExcept('0') < 0
            && EthereumAddress.TryParse("0x" + topic[26..], out var address)
            ? address
            : null;

    /// <summary>Makes a token of a contract, a symbol and decimals, recognising USDT and USDC by contract.</summary>
    /// <param name="contract">The contract.</param>
    /// <param name="symbol">The symbol the source gave, if any.</param>
    /// <param name="decimals">The decimals as text, if any.</param>
    /// <returns>The token, or <see langword="null"/> when the decimals are missing or ones Chainsaw cannot count in.
    /// </returns>
    private static Asset? Token(EthereumAddress contract, string? symbol, string? decimals)
    {
        if (!int.TryParse(decimals, NumberStyles.None, CultureInfo.InvariantCulture, out var places)
            || places > Asset.MaximumDecimals)
        {
            return null;
        }

        var shown = string.IsNullOrWhiteSpace(symbol) ? "token" : symbol.Trim();
        return KnownTokens.Canonical(Asset.Token(Chain.Ethereum, contract.Value, shown, places));
    }

    /// <summary>
    /// Reads a body as JSON and turns every failure into a <see cref="FormatException"/>; a body that says the source
    /// is asked too fast throws <see cref="SourceBusyException"/>, so that it is waited for and not counted as broken.
    /// </summary>
    /// <typeparam name="T">What is read.</typeparam>
    /// <param name="body">The body.</param>
    /// <param name="read">Reads the root.</param>
    /// <returns>What was read.</returns>
    private static T Read<T>(byte[] body, Func<JsonElement, T> read)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                ThrowIfBusy(document.RootElement);
            }

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
    /// <exception cref="SourceNotFoundException">The source said it does not know what was asked.</exception>
    private static JsonElement Result(JsonElement root)
    {
        var message = OptionalString(root, "message") ?? string.Empty;
        if (OptionalString(root, "status") == "1" && root.TryGetProperty("result", out var result)
            && result.ValueKind == JsonValueKind.Object)
        {
            return result;
        }

        ThrowIfBusy(root);

        // Etherscan-style APIs answer an unknown transaction or token with 200, status 0 and no result.
        var empty = !root.TryGetProperty("result", out var missing) || missing.ValueKind == JsonValueKind.Null;
        throw empty && OptionalString(root, "status") == "0"
            && message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            ? new SourceNotFoundException()
            : new FormatException("The source did not answer: " + message);
    }

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
            ThrowIfBusy(root);
            throw new FormatException("The source did not answer: " + OptionalString(root, "message"));
        }

        return ([.. result.EnumerateArray()], status == "2");
    }

    /// <summary>
    /// Throws when an Etherscan-style answer says that the source is asked too fast: status 0, and a message or a
    /// result that says <em>rate limit</em> (for example <c>Max rate limit reached</c>) or <em>too many requests</em>
    /// (Blockscout's open instance: <c>Too many requests. Increase limits now …</c>).
    /// </summary>
    /// <param name="root">The answer.</param>
    /// <exception cref="SourceBusyException">It says so.</exception>
    private static void ThrowIfBusy(JsonElement root)
    {
        var said = (OptionalString(root, "message") ?? string.Empty) + " "
            + (root.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String
                ? result.GetString()
                : string.Empty);
        if (OptionalString(root, "status") == "0"
            && (said.Contains("rate limit", StringComparison.OrdinalIgnoreCase)
                || said.Contains("too many requests", StringComparison.OrdinalIgnoreCase)))
        {
            throw new SourceBusyException();
        }
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
        OptionalReceiver(item) ?? throw new FormatException("The entry has no receiver.");

    /// <summary>Gives an entry's receiver, or <see langword="null"/> when it has none.</summary>
    /// <param name="item">The entry.</param>
    /// <returns>The receiver, or <see langword="null"/>.</returns>
    private static EthereumAddress? OptionalReceiver(JsonElement item) =>
        OptionalAddress(item, "to") ?? OptionalAddress(item, "contractAddress");

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

    /// <summary>
    /// Adds the fee an entry of the address's own transactions paid; a pending one has paid nothing yet, and its fee
    /// is known once it is in a block.
    /// </summary>
    /// <param name="item">The entry.</param>
    /// <param name="hash">Its transaction.</param>
    /// <param name="payer">The address, which signed it.</param>
    /// <param name="block">Its block, or <see langword="null"/>.</param>
    /// <param name="fees">Where the fee is added.</param>
    private static void AddFee(
        JsonElement item, EthereumTxHash hash, EthereumAddress payer, EthereumBlockReference? block,
        List<EthereumFee> fees)
    {
        if (block is null)
        {
            return;
        }

        var fee = Whole(item, "gasUsed") * Whole(item, "gasPrice");
        fees.Add(new EthereumFee(hash, payer, new AssetAmount(Asset.Ether, fee), Succeeded(item), block));
    }

    /// <summary>Says whether an entry of an address's transactions succeeded: no error and no failed receipt.</summary>
    /// <param name="item">The entry.</param>
    /// <returns><see langword="true"/> when it did.</returns>
    private static bool Succeeded(JsonElement item) =>
        !IsError(item) && OptionalString(item, "txreceipt_status") != "0";

    /// <summary>Says whether a token entry has decimals; without them the source has not read the token.</summary>
    /// <param name="item">The entry.</param>
    /// <returns><see langword="true"/> when it has.</returns>
    private static bool HasDecimals(JsonElement item) => !string.IsNullOrEmpty(OptionalString(item, "tokenDecimal"));

    /// <summary>Says how a transaction ended: pending without a block, otherwise by its <c>success</c>.</summary>
    /// <param name="result">The transaction.</param>
    /// <param name="height">Its block, or <see langword="null"/>.</param>
    /// <returns>The outcome.</returns>
    private static EthereumOutcome OutcomeOf(JsonElement result, long? height)
    {
        if (height is null)
        {
            return EthereumOutcome.Pending;
        }

        if (!result.TryGetProperty("success", out var success)
            || success.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new FormatException("A transaction in a block says whether it succeeded.");
        }

        return success.ValueKind == JsonValueKind.True ? EthereumOutcome.Succeeded : EthereumOutcome.Failed;
    }

    /// <summary>
    /// Says whether an internal transaction moved ether: it did not fail, carried value, and is a call, a creation or a
    /// self-destruct. A <c>delegatecall</c> or <c>callcode</c> runs another contract's code with the caller's value
    /// without moving it, and a <c>staticcall</c> cannot move any, so they are not counted.
    /// </summary>
    /// <param name="item">The entry.</param>
    /// <returns><see langword="true"/> when it moved ether.</returns>
    private static bool MovesEther(JsonElement item)
    {
        var callType = OptionalString(item, "callType");
        var type = OptionalString(item, "type");
        return !IsError(item) && !Whole(item, "value").IsZero
            && (string.IsNullOrEmpty(callType) || callType == "call")
            && type is null or "" or "call" or "create" or "create2" or "selfdestruct" or "suicide";
    }

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

    /// <summary>Reads a data word, <c>0x</c> and hex, as a whole number, or says it is not one.</summary>
    /// <param name="hex">The text.</param>
    /// <param name="value">The number.</param>
    /// <returns><see langword="true"/> when it is one.</returns>
    private static bool TryQuantity(string hex, out BigInteger value)
    {
        value = BigInteger.Zero;
        return hex.StartsWith("0x", StringComparison.Ordinal) && hex.Length > 2
            && BigInteger.TryParse("0" + hex[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                out value);
    }

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
