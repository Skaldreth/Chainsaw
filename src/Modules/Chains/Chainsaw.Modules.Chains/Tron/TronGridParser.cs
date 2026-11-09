using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// A TRON transaction as <c>wallet/gettransactionbyid</c> gives it: who signed it, what it sent of its own and how it
/// ended, before its block, fee and events are read (<see cref="TronTransactionInfo"/>).
/// </summary>
/// <param name="Hash">The transaction.</param>
/// <param name="Owner">The account that signed it and pays the fee.</param>
/// <param name="To">
/// Where it sent its TRX or which contract it called (<c>to_address</c> or <c>contract_address</c>), or
/// <see langword="null"/> for a transaction that does neither (a vote, a freeze, a delegation of resources).
/// </param>
/// <param name="Value">The TRX it sent of its own, in sun: the amount of a transfer or the value of a call.</param>
/// <param name="Result">
/// How the chain says it ended (<c>contractRet</c>, for example <c>SUCCESS</c> or <c>REVERT</c>), or <see langword="null"/>
/// when the answer does not say (a transaction that has not run in a block).
/// </param>
/// <param name="IsTrc10">Whether it moves a TRC-10 token, which Chainsaw does not count.</param>
internal sealed record TronTransactionRaw(
    EthereumTxHash Hash, EthereumAddress Owner, EthereumAddress? To, BigInteger Value, string? Result, bool IsTrc10)
{
    /// <summary>Gets whether the chain says it succeeded.</summary>
    public bool Succeeded => Result == "SUCCESS";
}

/// <summary>
/// What a TRON transaction did once it ran in a block, as <c>wallet/gettransactioninfobyid</c> gives it: its block, the
/// fee it burned, the TRC-20 <c>Transfer</c> events in its log and the TRX its contracts sent (internal
/// transactions).
/// </summary>
/// <param name="Hash">The transaction.</param>
/// <param name="Height">The block's number.</param>
/// <param name="Time">The block's time in UTC.</param>
/// <param name="Fee">The TRX it burned for bandwidth and energy, in sun.</param>
/// <param name="Failed">Whether its contract call failed (<c>receipt.result</c> is set and not <c>SUCCESS</c>).</param>
/// <param name="Tokens">The TRC-20 <c>Transfer</c> events that move something, with the log's index as position.</param>
/// <param name="Calls">The internal transactions that moved TRX and were not rejected.</param>
/// <param name="Unread">How many logs with the <c>Transfer</c> topic could not be read as a TRC-20 transfer.</param>
internal sealed record TronTransactionInfo(
    EthereumTxHash Hash, long Height, DateTimeOffset Time, BigInteger Fee, bool Failed, IReadOnlyList<TokenLog> Tokens,
    IReadOnlyList<InternalCall> Calls, int Unread);

/// <summary>A page of an address's transactions from <c>v1/accounts/:address/transactions</c>.</summary>
/// <param name="Page">The page as the account model reads it; its blocks' hashes are not in it.</param>
/// <param name="ServerTime">The source's own clock when it answered (<c>meta.at</c>), or <see langword="null"/>.</param>
internal sealed record TronHistoryPage(EthereumAddressPage Page, DateTimeOffset? ServerTime);

/// <summary>A TRC-20 transfer as an address's token history lists it, before its block and place are known.</summary>
/// <param name="Transaction">The transaction.</param>
/// <param name="Asset">The token, with the decimals the source gave.</param>
/// <param name="From">The address the tokens left.</param>
/// <param name="To">The address that received them.</param>
/// <param name="Units">The amount in the token's smallest unit.</param>
/// <param name="Time">The block's time in UTC.</param>
internal sealed record TronTokenEntry(
    EthereumTxHash Transaction, Asset Asset, EthereumAddress From, EthereumAddress To, BigInteger Units,
    DateTimeOffset Time);

/// <summary>A page of an address's TRC-20 transfers from <c>v1/accounts/:address/transactions/trc20</c>.</summary>
/// <param name="Entries">The transfers that touch the address and can be counted, newest first.</param>
/// <param name="NextAtOrBelow">
/// Where the next page starts (a block's time in milliseconds), or <see langword="null"/>.
/// </param>
/// <param name="IsCutShort">Whether a full page lay in one block, so that the rest cannot be read by paging.</param>
/// <param name="Skipped">How many entries are not counted (a transfer of zero, too many decimals).</param>
/// <param name="Unread">How many entries came without decimals; they keep the page from being final.</param>
/// <param name="ServerTime">The source's own clock when it answered (<c>meta.at</c>), or <see langword="null"/>.</param>
internal sealed record TronTokenPage(
    IReadOnlyList<TronTokenEntry> Entries, long? NextAtOrBelow, bool IsCutShort, int Skipped, int Unread,
    DateTimeOffset? ServerTime);

/// <summary>What a TRON address holds, as <c>v1/accounts/:address</c> gives it.</summary>
/// <param name="Sun">Its TRX, in sun.</param>
/// <param name="Tokens">The TRC-20 tokens Chainsaw knows (<see cref="KnownTokens"/>) with a balance above zero.</param>
/// <param name="Skipped">How many TRC-20 balances are of tokens whose decimals Chainsaw does not know.</param>
internal sealed record TronAccount(BigInteger Sun, IReadOnlyList<AssetAmount> Tokens, int Skipped);

/// <summary>
/// Reads TronGrid's answers (and a TRON node's <c>wallet/…</c> answers) and checks that they add up: every amount is a
/// whole number that is not negative, every address and id is well formed, the answer is about what was asked and a
/// page has only entries that touch its address. Anything else throws <see cref="FormatException"/>, and the answer
/// is sealed but never used. TRON's API answers an unknown transaction or block with <c>{}</c>, which throws
/// <see cref="SourceNotFoundException"/>.
/// </summary>
internal static class TronGridParser
{
    /// <summary>The first topic of a TRC-20 (and ERC-20) transfer: the Keccak-256 of its signature, without 0x.</summary>
    private static readonly string TransferTopic = BlockscoutParser.TransferTopic[2..];

    /// <summary>The digits of a block's id that hold its number: the first eight bytes.</summary>
    private const int HeightDigits = 16;

    /// <summary>
    /// Reads a block from <c>wallet/getblock?id_or_num=:height&amp;detail=false</c> (or the newest, without a number).
    /// The block's id begins with its number in eight bytes, which is checked against the number it gives.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <param name="height">The height asked for, or <see langword="null"/> when any will do (the newest).</param>
    /// <returns>The block's height, id and time.</returns>
    /// <exception cref="FormatException">It is not that block.</exception>
    public static EthereumBlockReference ParseBlock(byte[] body, long? height) => Read(body, root =>
    {
        ThrowIfEmpty(root);
        var id = String(root, "blockID");
        var header = root.GetProperty("block_header").GetProperty("raw_data");

        // A field that is zero is left out of TRON's answers, as the genesis block's number and time are.
        var number = OptionalLong(header, "number") ?? 0;
        var time = DateTimeOffset.FromUnixTimeMilliseconds(OptionalLong(header, "timestamp") ?? 0);
        if (!EthereumTxHash.TryParse(Chain.Tron, id, out var parsed)
            || long.Parse(parsed.Value[..HeightDigits], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)
                != number)
        {
            throw new FormatException("The block's id does not begin with its number.");
        }

        return height is { } asked && asked != number
            ? throw new FormatException("The block is not the one asked for.")
            : new EthereumBlockReference(number, parsed.Value, time);
    });

    /// <summary>Reads the newest block from <c>wallet/getblock?detail=false</c>.</summary>
    /// <param name="body">The body.</param>
    /// <returns>The newest block's height, id and time.</returns>
    /// <exception cref="FormatException">It is not a block above zero.</exception>
    public static ChainTip ParseTip(byte[] body)
    {
        var block = ParseBlock(body, null);
        return block.Height > 0
            ? new ChainTip(block.Height, block.Hash!, block.Time)
            : throw new FormatException("The newest block is not above zero.");
    }

    /// <summary>Reads the chain's height from the newest block.</summary>
    /// <param name="body">The body.</param>
    /// <returns>The height.</returns>
    /// <exception cref="FormatException">It is not a block above zero.</exception>
    public static EthereumTip ParseHeight(byte[] body) => new(ParseTip(body).Height);

    /// <summary>Reads a transaction from <c>wallet/gettransactionbyid?value=:id</c>.</summary>
    /// <param name="body">The body.</param>
    /// <param name="hash">The transaction asked about.</param>
    /// <returns>The transaction.</returns>
    /// <exception cref="SourceNotFoundException">The source does not know it (<c>{}</c>).</exception>
    /// <exception cref="FormatException">It is not that transaction, or does not add up.</exception>
    public static TronTransactionRaw ParseTransaction(byte[] body, EthereumTxHash hash) => Read(body, root =>
    {
        ThrowIfEmpty(root);
        if (!EthereumTxHash.TryParse(Chain.Tron, String(root, "txID"), out var id) || id != hash)
        {
            throw new FormatException("The transaction is not the one asked for.");
        }

        var contract = SingleContract(root.GetProperty("raw_data"));
        var type = String(contract, "type");
        var value = contract.GetProperty("parameter").GetProperty("value");
        var owner = Address(String(value, "owner_address"));
        var to = OptionalAddress(value, "to_address") ?? OptionalAddress(value, "contract_address");
        return new TronTransactionRaw(
            id, owner, to, MovedValue(type, value), OptionalResult(root), type == "TransferAssetContract");
    });

    /// <summary>Reads what a transaction did in its block from <c>wallet/gettransactioninfobyid?value=:id</c>.</summary>
    /// <param name="body">The body.</param>
    /// <param name="hash">The transaction asked about.</param>
    /// <returns>Its block, fee, events and internal transactions.</returns>
    /// <exception cref="SourceNotFoundException">
    /// The source does not know it in a block (<c>{}</c>): unknown, or not in a block yet.
    /// </exception>
    /// <exception cref="FormatException">It is not that transaction, or does not add up.</exception>
    public static TronTransactionInfo ParseTransactionInfo(byte[] body, EthereumTxHash hash) => Read(body, root =>
    {
        ThrowIfEmpty(root);
        if (!EthereumTxHash.TryParse(Chain.Tron, String(root, "id"), out var id) || id != hash)
        {
            throw new FormatException("The transaction is not the one asked for.");
        }

        var height = OptionalLong(root, "blockNumber") ?? throw new FormatException("blockNumber is missing.");
        var time = DateTimeOffset.FromUnixTimeMilliseconds(
            OptionalLong(root, "blockTimeStamp") ?? throw new FormatException("blockTimeStamp is missing."));
        var result = root.TryGetProperty("receipt", out var receipt) ? OptionalString(receipt, "result") : null;
        var failed = result is not null && result != "SUCCESS";
        var (tokens, unread) = Logs(root);
        return new TronTransactionInfo(
            id, height, time, OptionalWhole(root, "fee"), failed, failed ? [] : tokens,
            failed ? [] : InfoCalls(root), unread);
    });

    /// <summary>
    /// Reads a page of an address's transactions from <c>v1/accounts/:address/transactions</c>: the TRX each sent, the
    /// TRX its contracts sent (internal transactions, which the source lists with the transaction), and the fee the
    /// address paid when it signed. A TRC-10 transfer is skipped; the TRC-20 transfers are in the token history.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <param name="address">The address asked about.</param>
    /// <param name="atOrBelow">The block time the page goes back from, in milliseconds, or <see langword="null"/>.</param>
    /// <param name="pageSize">How many entries were asked for.</param>
    /// <returns>The page.</returns>
    /// <exception cref="FormatException">The answer does not add up.</exception>
    public static TronHistoryPage ParseHistory(byte[] body, EthereumAddress address, long? atOrBelow, int pageSize) =>
        Read(body, root =>
        {
            var items = Data(root);
            var transfers = new List<EthereumTransfer>();
            var fees = new List<EthereumFee>();
            var times = new List<long>();
            var skipped = 0;
            foreach (var item in items)
            {
                if (OptionalLong(item, "block_timestamp") is { } time)
                {
                    times.Add(time);
                }

                skipped += ReadHistoryEntry(item, address, transfers, fees);
            }

            var (next, cut) = NextPage(times, items.Count, atOrBelow, pageSize);
            var page = new EthereumAddressPage(
                address, EthereumHistory.Transactions, atOrBelow, transfers, fees, next, cut, false)
            {
                Skipped = skipped,
            };
            return new TronHistoryPage(page, ServerTime(root));
        });

    /// <summary>Reads a page of an address's TRC-20 transfers from <c>v1/accounts/:address/transactions/trc20</c>.</summary>
    /// <param name="body">The body.</param>
    /// <param name="address">The address asked about.</param>
    /// <param name="atOrBelow">The block time the page goes back from, in milliseconds, or <see langword="null"/>.</param>
    /// <param name="pageSize">How many entries were asked for.</param>
    /// <returns>The page.</returns>
    /// <exception cref="FormatException">The answer does not add up, or an entry does not touch the address.</exception>
    public static TronTokenPage ParseTokenHistory(
        byte[] body, EthereumAddress address, long? atOrBelow, int pageSize) => Read(body, root =>
    {
        var items = Data(root);
        var entries = new List<TronTokenEntry>();
        var times = new List<long>();
        var (skipped, unread) = (0, 0);
        foreach (var item in items)
        {
            var time = Long(item, "block_timestamp");
            times.Add(time);
            var from = Address(String(item, "from"));
            var to = Address(String(item, "to"));
            if (from != address && to != address)
            {
                throw new FormatException("A token transfer on the page does not touch its address.");
            }

            var units = Whole(item, "value");
            var token = item.GetProperty("token_info");
            var decimals = OptionalLong(token, "decimals");
            if (OptionalString(item, "type") is { } type && type != "Transfer")
            {
                continue;
            }

            if (decimals is null)
            {
                unread++;
            }
            else if (units.IsZero || decimals is < 0 or > Asset.MaximumDecimals)
            {
                skipped++;
            }
            else
            {
                var contract = Address(String(token, "address"));
                var symbol = OptionalString(token, "symbol");
                var asset = KnownTokens.Canonical(Asset.Token(Chain.Tron, contract.Value,
                    string.IsNullOrWhiteSpace(symbol) ? "token" : symbol.Trim(), (int)decimals.Value));
                entries.Add(new TronTokenEntry(EthereumTxHash.Parse(Chain.Tron, String(item, "transaction_id")),
                    asset, from, to, units, DateTimeOffset.FromUnixTimeMilliseconds(time)));
            }
        }

        var (next, cut) = NextPage(times, items.Count, atOrBelow, pageSize);
        return new TronTokenPage(entries, next, cut, skipped, unread, ServerTime(root));

    });

    /// <summary>
    /// Reads what an address holds from <c>v1/accounts/:address</c>: its TRX, and the TRC-20 tokens Chainsaw knows. An
    /// address that has never been used is not in the answer (<c>data</c> is empty) and holds nothing.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <param name="address">The address asked about.</param>
    /// <returns>What it holds.</returns>
    /// <exception cref="FormatException">The answer does not add up.</exception>
    public static TronAccount ParseAccount(byte[] body, EthereumAddress address) => Read(body, root =>
    {
        var items = Data(root);
        if (items.Count == 0)
        {
            return new TronAccount(BigInteger.Zero, [], 0);
        }

        var account = items[0];
        if (OptionalString(account, "address") is { } own && Address(own) != address)
        {
            throw new FormatException("The account is not the one asked for.");
        }

        var tokens = new List<AssetAmount>();
        var skipped = 0;
        if (account.TryGetProperty("trc20", out var balances) && balances.ValueKind == JsonValueKind.Array)
        {
            foreach (var property in balances.EnumerateArray().SelectMany(entry => entry.EnumerateObject()))
            {
                var units = WholeText(property.Value.GetString());
                var known = KnownTokens.Canonical(Asset.Token(Chain.Tron, Address(property.Name).Value, "token", 0));
                if (KnownTokens.IssuerOf(known) is null)
                {
                    skipped++;
                }
                else if (!units.IsZero)
                {
                    tokens.Add(new AssetAmount(known, units));
                }
            }
        }

        return new TronAccount(OptionalWhole(account, "balance"), tokens, skipped);
    });

    /// <summary>
    /// Reads one entry of an address's transactions into its movements and fee, and says whether it was skipped (a
    /// TRC-10 transfer that touches the address).
    /// </summary>
    /// <param name="item">The entry.</param>
    /// <param name="address">The page's address.</param>
    /// <param name="transfers">The movements, added to.</param>
    /// <param name="fees">The fees, added to.</param>
    /// <returns>1 when a TRC-10 transfer was skipped, else 0.</returns>
    private static int ReadHistoryEntry(
        JsonElement item, EthereumAddress address, List<EthereumTransfer> transfers, List<EthereumFee> fees)
    {
        var hash = EthereumTxHash.Parse(Chain.Tron, String(item, "txID"));
        // An entry that has not run in a block yet has no block number; it is pending, as on Ethereum.
        var block = OptionalLong(item, "blockNumber") is { } height
            ? new EthereumBlockReference(
                height, null, DateTimeOffset.FromUnixTimeMilliseconds(Long(item, "block_timestamp")))
            : null;
        var contract = SingleContract(item.GetProperty("raw_data"));
        var type = String(contract, "type");
        var value = contract.GetProperty("parameter").GetProperty("value");
        var owner = Address(String(value, "owner_address"));
        var to = OptionalAddress(value, "to_address") ?? OptionalAddress(value, "contract_address");
        if (block is null)
        {
            // Not in a block yet: it has moved nothing and paid nothing; the next page read sees it in its block.
            return 0;
        }

        var succeeded = ResultOf(item) == "SUCCESS";
        if (owner == address)
        {
            var paid = item.GetProperty("ret")[0];
            fees.Add(new EthereumFee(hash, owner, Trx(OptionalWhole(paid, "fee")), succeeded, block));
        }

        if (!succeeded)
        {
            return 0;
        }

        var moved = MovedValue(type, value);
        if (!moved.IsZero && to is not null && (owner == address || to == address))
        {
            transfers.Add(new EthereumTransfer(hash, EthereumTransferKind.Native, owner, to, Trx(moved), 0, block));
        }

        transfers.AddRange(HistoryCalls(item).Where(call => call.From == address || call.To == address)
            .Select(call => new EthereumTransfer(
                hash, EthereumTransferKind.Internal, call.From, call.To, Trx(call.Value), call.Position, block)));
        return type == "TransferAssetContract" && (owner == address || to == address) ? 1 : 0;
    }

    /// <summary>
    /// Reads the internal transactions an address's history lists with a transaction: those that were not rejected and
    /// sent TRX (<c>call_value._</c>; another key is a TRC-10 token, not counted), with their order as position.
    /// </summary>
    /// <param name="item">The entry.</param>
    /// <returns>The calls.</returns>
    private static List<InternalCall> HistoryCalls(JsonElement item)
    {
        var calls = new List<InternalCall>();
        if (!item.TryGetProperty("internal_transactions", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return calls;
        }

        var position = 0;
        foreach (var call in list.EnumerateArray())
        {
            position++;
            var data = call.GetProperty("data");
            var rejected = data.TryGetProperty("rejected", out var flag) && flag.ValueKind == JsonValueKind.True;
            var sun = data.TryGetProperty("call_value", out var values) ? OptionalWhole(values, "_") : BigInteger.Zero;
            if (!rejected && !sun.IsZero)
            {
                calls.Add(new InternalCall(
                    Address(String(call, "from_address")), Address(String(call, "to_address")), sun, position));
            }
        }

        return calls;
    }

    /// <summary>
    /// Reads the internal transactions of a transaction's info: those that were not rejected and sent TRX
    /// (<c>callValueInfo</c> with a <c>callValue</c> and no <c>tokenId</c>), with their order as position, as the
    /// history numbers them.
    /// </summary>
    /// <param name="root">The info.</param>
    /// <returns>The calls.</returns>
    private static List<InternalCall> InfoCalls(JsonElement root)
    {
        var calls = new List<InternalCall>();
        if (!root.TryGetProperty("internal_transactions", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return calls;
        }

        var position = 0;
        foreach (var call in list.EnumerateArray())
        {
            position++;
            var rejected = call.TryGetProperty("rejected", out var flag) && flag.ValueKind == JsonValueKind.True;
            var sun = BigInteger.Zero;
            if (call.TryGetProperty("callValueInfo", out var infos) && infos.ValueKind == JsonValueKind.Array)
            {
                sun = infos.EnumerateArray().Where(info => !info.TryGetProperty("tokenId", out _))
                    .Aggregate(BigInteger.Zero, (sum, info) => sum + OptionalWhole(info, "callValue"));
            }

            if (!rejected && !sun.IsZero)
            {
                calls.Add(new InternalCall(Address(String(call, "caller_address")),
                    Address(String(call, "transferTo_address")), sun, position));
            }
        }

        return calls;
    }

    /// <summary>
    /// Reads the TRC-20 <c>Transfer</c> events in a transaction's log: three topics (the signature, the sender and the
    /// receiver) and the amount in the data, from the token's contract. An event of zero is left out (anyone can make a
    /// contract write one); one with the topic that cannot be read as a transfer is counted as unread.
    /// </summary>
    /// <param name="root">The info.</param>
    /// <returns>The transfers with the log's index as position, and how many could not be read.</returns>
    private static (List<TokenLog> Tokens, int Unread) Logs(JsonElement root)
    {
        var tokens = new List<TokenLog>();
        var unread = 0;
        if (!root.TryGetProperty("log", out var logs) || logs.ValueKind != JsonValueKind.Array)
        {
            return (tokens, unread);
        }

        var index = -1;
        foreach (var log in logs.EnumerateArray())
        {
            index++;
            var topics = log.TryGetProperty("topics", out var list) && list.ValueKind == JsonValueKind.Array
                ? [.. list.EnumerateArray().Select(topic => topic.GetString() ?? string.Empty)]
                : Array.Empty<string>();
            if (topics.Length == 0 || !string.Equals(topics[0], TransferTopic, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = OptionalString(log, "data") ?? string.Empty;
            if (topics.Length != 3 || data.Length != 64
                || !EthereumAddress.TryFromTronWord(OptionalString(log, "address"), out var contract)
                || !EthereumAddress.TryFromTronWord(topics[1], out var from)
                || !EthereumAddress.TryFromTronWord(topics[2], out var to)
                || !BigInteger.TryParse("0" + data, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture,
                    out var units))
            {
                unread++;
                continue;
            }

            if (!units.IsZero)
            {
                tokens.Add(new TokenLog(contract, from, to, units, index));
            }
        }

        return (tokens, unread);
    }

    /// <summary>
    /// Says where the next page starts: at the oldest block time on a full page (the source pages by time, and a page
    /// that goes back from a time starts <em>at</em> it, so its entries come again), or nowhere when the page was not
    /// full; a full page whose entries all lie at one time cannot be paged past.
    /// </summary>
    /// <param name="times">The block times of the entries that have one, in milliseconds.</param>
    /// <param name="count">How many entries the page holds, also those without a block time.</param>
    /// <param name="atOrBelow">Where the page started.</param>
    /// <param name="pageSize">How many entries were asked for.</param>
    /// <returns>The next start, and whether the page was cut short.</returns>
    private static (long? Next, bool Cut) NextPage(List<long> times, int count, long? atOrBelow, int pageSize)
    {
        if (times.Any(time => time > atOrBelow))
        {
            throw new FormatException("An entry on the page is newer than where the page starts.");
        }

        // A full page is one with as many entries as were asked for, whether or not each is in a block yet; a full page
        // without any block time cannot say where the next starts, and is cut short.
        if (count < pageSize)
        {
            return (null, false);
        }

        if (times.Count == 0)
        {
            return (null, true);
        }

        var oldest = times.Min();
        return oldest == times.Max() ? (null, true) : (oldest, false);
    }

    /// <summary>Gives the TRX a transaction sent of its own: a transfer's amount, or a call's value.</summary>
    /// <param name="type">The contract's type.</param>
    /// <param name="value">Its parameters.</param>
    /// <returns>The TRX, in sun; zero for any other type.</returns>
    private static BigInteger MovedValue(string type, JsonElement value) => type switch
    {
        "TransferContract" => OptionalWhole(value, "amount"),
        "TriggerSmartContract" => OptionalWhole(value, "call_value"),
        _ => BigInteger.Zero,
    };

    /// <summary>Gives a transaction's only contract; TRON allows one per transaction.</summary>
    /// <param name="rawData">The transaction's <c>raw_data</c>.</param>
    /// <returns>The contract.</returns>
    /// <exception cref="FormatException">There is not exactly one.</exception>
    private static JsonElement SingleContract(JsonElement rawData)
    {
        var contracts = rawData.GetProperty("contract");
        return contracts.ValueKind == JsonValueKind.Array && contracts.GetArrayLength() == 1
            ? contracts[0]
            : throw new FormatException("A TRON transaction has exactly one contract.");
    }

    /// <summary>Reads how a transaction ended, <c>ret[0].contractRet</c>.</summary>
    /// <param name="item">The transaction.</param>
    /// <returns>The result, for example <c>SUCCESS</c> or <c>REVERT</c>.</returns>
    private static string ResultOf(JsonElement item) =>
        OptionalResult(item) ?? throw new FormatException("The transaction does not say how it ended.");

    /// <summary>Reads how a transaction ended, <c>ret[0].contractRet</c>, when the answer says.</summary>
    /// <param name="item">The transaction.</param>
    /// <returns>The result, or <see langword="null"/>.</returns>
    private static string? OptionalResult(JsonElement item) =>
        item.TryGetProperty("ret", out var ret) && ret.ValueKind == JsonValueKind.Array && ret.GetArrayLength() > 0
            ? OptionalString(ret[0], "contractRet")
            : null;

    /// <summary>Gives the entries of a TronGrid list, <c>{"success":true,"data":[…]}</c>.</summary>
    /// <param name="root">The answer.</param>
    /// <returns>The entries.</returns>
    /// <exception cref="FormatException">The source said it failed.</exception>
    private static List<JsonElement> Data(JsonElement root) =>
        root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.True
            && root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array
            ? [.. data.EnumerateArray()]
            : throw new FormatException("The source did not answer: " + OptionalString(root, "error"));

    /// <summary>Reads the source's own clock from a TronGrid list's <c>meta.at</c>.</summary>
    /// <param name="root">The answer.</param>
    /// <returns>The time, or <see langword="null"/> when it is not given.</returns>
    private static DateTimeOffset? ServerTime(JsonElement root) =>
        root.TryGetProperty("meta", out var meta) && OptionalLong(meta, "at") is { } at
            ? DateTimeOffset.FromUnixTimeMilliseconds(at)
            : null;

    /// <summary>Throws <see cref="SourceNotFoundException"/> for TRON's answer to something unknown, <c>{}</c>.</summary>
    /// <param name="root">The answer.</param>
    private static void ThrowIfEmpty(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object && !root.EnumerateObject().Any())
        {
            throw new SourceNotFoundException();
        }
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
            ThrowIfBusy(document.RootElement);
            return read(document.RootElement);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException
            or KeyNotFoundException or ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            throw new FormatException("The answer is not what TronGrid gives.", exception);
        }
    }

    /// <summary>
    /// Throws when an answer says the source is asked too fast: TronGrid answers <c>{"Error": "…"}</c> or
    /// <c>{"success": false, "error": "…"}</c> with a text about its limit (<em>request rate exceeded</em>).
    /// </summary>
    /// <param name="root">The answer.</param>
    private static void ThrowIfBusy(JsonElement root)
    {
        var text = OptionalString(root, "Error") ?? OptionalString(root, "error") ?? string.Empty;
        if (text.Contains("limit", StringComparison.OrdinalIgnoreCase)
            || text.Contains("too many", StringComparison.OrdinalIgnoreCase)
            || text.Contains("rate exceeded", StringComparison.OrdinalIgnoreCase))
        {
            throw new SourceBusyException();
        }
    }

    /// <summary>Makes an amount of TRX.</summary>
    /// <param name="sun">The amount in sun.</param>
    /// <returns>The amount.</returns>
    private static AssetAmount Trx(BigInteger sun) => new(Asset.Tronix, sun);

    /// <summary>Reads a TRON address in hex or Base58Check.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The address.</returns>
    /// <exception cref="FormatException">It is not one.</exception>
    private static EthereumAddress Address(string text) =>
        EthereumAddress.TryParseTron(text, out var address, out _)
            ? address
            : throw new FormatException("Not a TRON address.");

    /// <summary>Reads an address property that may be missing.</summary>
    /// <param name="item">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The address, or <see langword="null"/>.</returns>
    private static EthereumAddress? OptionalAddress(JsonElement item, string name) =>
        OptionalString(item, name) is { Length: > 0 } text ? Address(text) : null;

    /// <summary>Reads a whole number that is not negative, as a JSON number or decimal text.</summary>
    /// <param name="item">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number.</returns>
    private static BigInteger Whole(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) ? WholeOf(value) : throw new FormatException($"{name} is missing.");

    /// <summary>Reads a whole number that is not negative and may be missing (TRON leaves zero out).</summary>
    /// <param name="item">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number, or zero when it is missing.</returns>
    private static BigInteger OptionalWhole(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value)
            ? WholeOf(value)
            : BigInteger.Zero;

    /// <summary>Reads a JSON number or decimal text as a whole number that is not negative.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The number.</returns>
    private static BigInteger WholeOf(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => WholeText(value.GetRawText()),
        JsonValueKind.String => WholeText(value.GetString()),
        _ => throw new FormatException("A number is missing."),
    };

    /// <summary>Reads decimal text as a whole number that is not negative.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The number.</returns>
    private static BigInteger WholeText(string? text) =>
        BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new FormatException("Not a whole number.");

    /// <summary>Reads a whole number property that must be there and fit in a long.</summary>
    /// <param name="item">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number.</returns>
    private static long Long(JsonElement item, string name) =>
        OptionalLong(item, name) ?? throw new FormatException($"{name} is missing.");

    /// <summary>Reads a whole number property that may be missing, as a JSON number or decimal text.</summary>
    /// <param name="item">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number, or <see langword="null"/>.</returns>
    private static long? OptionalLong(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var number = WholeOf(value);
        return number <= long.MaxValue ? (long)number : throw new FormatException($"{name} is too large.");
    }

    /// <summary>Reads a string property that must be there.</summary>
    /// <param name="item">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The text.</returns>
    private static string String(JsonElement item, string name) =>
        OptionalString(item, name) ?? throw new FormatException($"{name} is missing.");

    /// <summary>Reads a string property that may be missing or null.</summary>
    /// <param name="item">The object.</param>
    /// <param name="name">The property.</param>
    /// <returns>The text, or <see langword="null"/>.</returns>
    private static string? OptionalString(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
