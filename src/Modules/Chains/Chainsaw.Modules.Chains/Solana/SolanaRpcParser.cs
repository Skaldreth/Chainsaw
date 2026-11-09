using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// One entry of an address's history as <c>getSignaturesForAddress</c> lists it: a transaction that names the address
/// among its accounts, with its slot, before what it moved is read from the transaction itself.
/// </summary>
/// <param name="Transaction">The transaction, named by its first signature.</param>
/// <param name="Slot">The slot it is in.</param>
/// <param name="Failed">Whether it failed (<c>err</c> is set): it moved nothing, but its fee was paid.</param>
internal sealed record SolanaSignature(EthereumTxHash Transaction, long Slot, bool Failed);

/// <summary>
/// One page of an address's signatures, newest first, as <c>getSignaturesForAddress</c> gives it with
/// <c>commitment: finalized</c>.
/// </summary>
/// <param name="Address">The address (an owner, or one of its token accounts).</param>
/// <param name="AtOrBelow">The slot the page goes back from, or <see langword="null"/> for the newest.</param>
/// <param name="Size">How many entries were asked for.</param>
/// <param name="Entries">The entries, newest first.</param>
internal sealed record SolanaSignaturePage(
    EthereumAddress Address, long? AtOrBelow, int Size, IReadOnlyList<SolanaSignature> Entries)
{
    /// <summary>Gets whether the page holds as many entries as were asked for, so that older ones may follow.</summary>
    public bool IsFull => Entries.Count >= Size;

    /// <summary>
    /// Gets whether a full page lies in one slot, so that the rest of that slot and everything older cannot be reached
    /// by going back by slot.
    /// </summary>
    public bool IsCutShort => IsFull && Entries.Select(entry => entry.Slot).Distinct().Count() == 1;

    /// <summary>
    /// Gets the slot the next page starts at (its oldest slot, whose entries come again), or <see langword="null"/>
    /// when the history ends here or cannot be paged further (<see cref="IsCutShort"/>).
    /// </summary>
    public long? NextAtOrBelow => IsFull && !IsCutShort ? Entries[^1].Slot : null;
}

/// <summary>A token account's balance before or after a transaction, as its meta lists it.</summary>
/// <param name="AccountIndex">The token account's place among the transaction's accounts.</param>
/// <param name="Mint">The token's mint.</param>
/// <param name="Owner">
/// The token account's owner, or <see langword="null"/> when the answer does not say (transactions from before the
/// owner was listed).
/// </param>
/// <param name="Amount">The amount in the token's smallest unit.</param>
/// <param name="Decimals">The token's decimals, from its mint.</param>
internal sealed record SolanaTokenBalance(
    int AccountIndex, string Mint, EthereumAddress? Owner, BigInteger Amount, int Decimals);

/// <summary>
/// A Solana transaction as <c>getTransaction</c> gives it: its slot, who paid its fee, how it ended, and every
/// account's balance in SOL and in tokens before and after it ran, which are what the chain recorded (principle 2).
/// </summary>
/// <param name="Hash">The transaction, named by its first signature.</param>
/// <param name="Slot">The slot it is in.</param>
/// <param name="Time">The slot's time in UTC (<c>blockTime</c>).</param>
/// <param name="Index">Its place in the slot (<c>transactionIndex</c>), when the answer gave it.</param>
/// <param name="Fee">The fee in lamports, paid by the first account.</param>
/// <param name="Failed">Whether it failed: then only the fee moved.</param>
/// <param name="Accounts">
/// Its accounts in order: the message's own, then those loaded from address lookup tables (writable, then read-only).
/// </param>
/// <param name="Pre">Each account's lamports before it ran.</param>
/// <param name="Post">Each account's lamports after it ran.</param>
/// <param name="PreTokens">The token accounts' balances before it ran.</param>
/// <param name="PostTokens">The token accounts' balances after it ran.</param>
/// <param name="Moves">
/// What its parsed instructions moved, top-level and inner, in the order they ran (<see cref="SolanaInstructions"/>).
/// </param>
/// <param name="Hints">The token accounts' owners its instructions name (accounts created in it).</param>
internal sealed record SolanaTransactionRaw(
    EthereumTxHash Hash,
    long Slot,
    DateTimeOffset Time,
    long? Index,
    BigInteger Fee,
    bool Failed,
    IReadOnlyList<EthereumAddress> Accounts,
    IReadOnlyList<BigInteger> Pre,
    IReadOnlyList<BigInteger> Post,
    IReadOnlyList<SolanaTokenBalance> PreTokens,
    IReadOnlyList<SolanaTokenBalance> PostTokens,
    IReadOnlyList<SolanaMove> Moves,
    IReadOnlyList<SolanaTokenAccountHint> Hints)
{
    /// <summary>Gets the account that paid the fee: the first one, which signed it.</summary>
    public EthereumAddress Payer => Accounts[0];
}

/// <summary>
/// A token account an owner holds, as <c>getTokenAccountsByOwner</c> lists it with <c>jsonParsed</c>: its address,
/// mint and amount now.
/// </summary>
/// <param name="Account">The token account.</param>
/// <param name="Mint">The token's mint.</param>
/// <param name="Amount">The amount in the token's smallest unit.</param>
/// <param name="Decimals">The token's decimals.</param>
/// <param name="IsFrozen">
/// Whether the account is frozen (<c>state: frozen</c>): its owner cannot move the token.
/// </param>
internal sealed record SolanaTokenAccount(
    EthereumAddress Account, string Mint, BigInteger Amount, int Decimals, bool IsFrozen);

/// <summary>
/// Reads Solana's JSON-RPC answers (<see cref="SolanaRpcApi"/>), checking that each is the answer to what was asked.
/// A JSON-RPC error says what kind it is: a rate limit (<see cref="SourceBusyException"/>), something the node does not
/// know (<see cref="SourceNotFoundException"/>), or a failure (<see cref="FormatException"/>).
/// </summary>
internal static class SolanaRpcParser
{
    /// <summary>The commitment every lookup asks with, and the only one an entry is taken at.</summary>
    public const string Finalized = "finalized";

    /// <summary>The error code some providers give a rate limit in the body.</summary>
    private const int RateLimitCode = 429;

    /// <summary>The error code some providers give a rate limit in JSON-RPC's own range.</summary>
    private const int JsonRpcRateLimitCode = -32429;

    /// <summary>The error code of a slot that was skipped, or missing after a jump to a recent snapshot.</summary>
    private const int SlotSkippedCode = -32007;

    /// <summary>The error code of a slot that was skipped, or is missing in long-term storage.</summary>
    private const int SlotSkippedInStorageCode = -32009;

    /// <summary>Reads the newest finalized slot (<c>getSlot</c>).</summary>
    /// <param name="body">The body.</param>
    /// <returns>The slot.</returns>
    /// <exception cref="FormatException">The body is not a slot above zero.</exception>
    public static long ParseSlot(ReadOnlySpan<byte> body)
    {
        using var document = Envelope(body.ToArray(), out var result);
        return result.ValueKind == JsonValueKind.Number && result.TryGetInt64(out var slot) && slot > 0
            ? slot
            : throw new FormatException("The newest slot is not a number above zero.");
    }

    /// <summary>
    /// Reads a block's hash: the slot's <c>blockhash</c> from <c>getBlock</c>, or the cluster's genesis hash from
    /// <c>getGenesisHash</c>, which is what an endpoint is checked against for height 0.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <returns>The hash in Base58.</returns>
    /// <exception cref="FormatException">The body holds neither.</exception>
    public static string ParseHash(ReadOnlySpan<byte> body)
    {
        using var document = Envelope(body.ToArray(), out var result);
        var hash = result.ValueKind == JsonValueKind.String ? result.GetString() : String(result, "blockhash");
        return Base58.TryDecode(hash, EthereumAddress.SolanaBytes, out _)
            ? hash!
            : throw new FormatException("The hash is not 32 bytes in Base58.");
    }

    /// <summary>Reads a slot's block from <c>getBlock</c> without its transactions.</summary>
    /// <param name="body">The body.</param>
    /// <param name="slot">The slot asked for.</param>
    /// <returns>The block: the slot, its hash and its time.</returns>
    /// <exception cref="SourceNotFoundException">The slot was skipped: there is no block in it.</exception>
    /// <exception cref="FormatException">The body is not a block.</exception>
    public static EthereumBlockReference ParseBlock(byte[] body, long slot)
    {
        using var document = Envelope(body, out var result);
        if (result.ValueKind != JsonValueKind.Object)
        {
            throw new SourceNotFoundException();
        }

        var hash = String(result, "blockhash");
        if (!Base58.TryDecode(hash, EthereumAddress.SolanaBytes, out _))
        {
            throw new FormatException("The block's hash is not 32 bytes in Base58.");
        }

        var time = Long(result, "blockTime") ?? throw new FormatException("blockTime is missing.");
        return new EthereumBlockReference(slot, hash, DateTimeOffset.FromUnixTimeSeconds(time));
    }

    /// <summary>Reads a page of an address's signatures (<c>getSignaturesForAddress</c>).</summary>
    /// <param name="body">The body.</param>
    /// <param name="address">The address asked about.</param>
    /// <param name="atOrBelow">The slot the page goes back from, or <see langword="null"/>.</param>
    /// <param name="size">How many entries were asked for.</param>
    /// <returns>The page.</returns>
    /// <exception cref="FormatException">
    /// An entry is not final, is newer than where the page starts, or the entries are not newest first.
    /// </exception>
    public static SolanaSignaturePage ParseSignatures(
        byte[] body, EthereumAddress address, long? atOrBelow, int size)
    {
        using var document = Envelope(body, out var result);
        if (result.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("The signatures are not a list.");
        }

        var entries = new List<SolanaSignature>();
        foreach (var item in result.EnumerateArray())
        {
            var signature = String(item, "signature");
            if (!EthereumTxHash.TryParse(Chain.Solana, signature, out var hash))
            {
                throw new FormatException("A signature is not 64 bytes in Base58.");
            }

            var slot = Long(item, "slot") ?? throw new FormatException("slot is missing.");
            if (!string.Equals(String(item, "confirmationStatus"), Finalized, StringComparison.Ordinal))
            {
                throw new FormatException("An entry on the page is not finalized.");
            }

            if (slot > atOrBelow || (entries.Count > 0 && slot > entries[^1].Slot))
            {
                throw new FormatException("The entries are not newest first from where the page starts.");
            }

            var failed = item.TryGetProperty("err", out var error) && error.ValueKind != JsonValueKind.Null;
            entries.Add(new SolanaSignature(hash, slot, failed));
        }

        if (entries.Count > size)
        {
            throw new FormatException("The page holds more entries than were asked for.");
        }

        return new SolanaSignaturePage(address, atOrBelow, size, entries);
    }

    /// <summary>
    /// Reads a transaction from <c>getTransaction</c> with <c>encoding: jsonParsed</c>: its accounts, including those
    /// loaded from address lookup tables, each one's balance before and after, and what its parsed instructions moved.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <param name="hash">The transaction asked for.</param>
    /// <returns>The transaction.</returns>
    /// <exception cref="SourceNotFoundException">The node does not know it as finalized.</exception>
    /// <exception cref="FormatException">The answer is not the transaction asked for, or does not add up.</exception>
    public static SolanaTransactionRaw ParseTransaction(byte[] body, EthereumTxHash hash)
    {
        using var document = Envelope(body, out var result);
        if (result.ValueKind == JsonValueKind.Null)
        {
            throw new SourceNotFoundException();
        }

        var transaction = Object(result, "transaction");
        var signatures = Array(transaction, "signatures");
        if (signatures.GetArrayLength() == 0
            || !string.Equals(signatures[0].GetString(), hash.Value, StringComparison.Ordinal))
        {
            throw new FormatException("The transaction is not the one asked for.");
        }

        var meta = Object(result, "meta");
        var message = Object(transaction, "message");
        var accounts = Accounts(message, meta);
        var pre = Lamports(meta, "preBalances", accounts.Count);
        var post = Lamports(meta, "postBalances", accounts.Count);
        var slot = Long(result, "slot") ?? throw new FormatException("slot is missing.");
        var time = Long(result, "blockTime") ?? throw new FormatException("blockTime is missing.");
        var fee = Long(meta, "fee") ?? throw new FormatException("fee is missing.");
        var failed = meta.TryGetProperty("err", out var error) && error.ValueKind != JsonValueKind.Null;
        var (moves, hints) = SolanaInstructions.Read(message, meta);
        return new SolanaTransactionRaw(
            hash, slot, DateTimeOffset.FromUnixTimeSeconds(time), Long(result, "transactionIndex"), fee, failed,
            accounts, pre, post, TokenBalances(meta, "preTokenBalances", accounts.Count),
            TokenBalances(meta, "postTokenBalances", accounts.Count), moves, hints);
    }

    /// <summary>Reads an address's SOL now (<c>getBalance</c>).</summary>
    /// <param name="body">The body.</param>
    /// <returns>The lamports.</returns>
    /// <exception cref="FormatException">The body is not a balance.</exception>
    public static BigInteger ParseBalance(byte[] body)
    {
        using var document = Envelope(body, out var result);
        var value = Long(result, "value") ?? throw new FormatException("value is missing.");
        return value >= 0 ? value : throw new FormatException("A balance is not negative.");
    }

    /// <summary>
    /// Reads the token accounts an owner holds now (<c>getTokenAccountsByOwner</c> with <c>jsonParsed</c>), checking
    /// that each belongs to the owner asked about.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <param name="owner">The owner asked about.</param>
    /// <returns>The token accounts.</returns>
    /// <exception cref="FormatException">An account is not a token account of the owner.</exception>
    public static IReadOnlyList<SolanaTokenAccount> ParseTokenAccounts(byte[] body, EthereumAddress owner)
    {
        using var document = Envelope(body, out var result);
        var accounts = new List<SolanaTokenAccount>();
        foreach (var item in Array(result, "value").EnumerateArray())
        {
            var account = Address(String(item, "pubkey"));
            var info = Object(Object(Object(Object(item, "account"), "data"), "parsed"), "info");
            if (Address(String(info, "owner")) != owner)
            {
                throw new FormatException("A token account does not belong to the owner asked about.");
            }

            var amount = Object(info, "tokenAmount");
            accounts.Add(new SolanaTokenAccount(
                account, Mint(String(info, "mint")), Units(String(amount, "amount")), Decimals(amount),
                string.Equals(String(info, "state"), "frozen", StringComparison.Ordinal)));
        }

        return accounts;
    }

    /// <summary>
    /// Reads the transaction's accounts in the order its balances are listed: the message's own keys, then the
    /// writable and the read-only ones loaded from address lookup tables.
    /// </summary>
    /// <param name="message">The transaction's message.</param>
    /// <param name="meta">The transaction's meta.</param>
    /// <returns>The accounts.</returns>
    private static List<EthereumAddress> Accounts(JsonElement message, JsonElement meta)
    {
        // With jsonParsed each key is an object that names its pubkey, and the loaded ones are among them already.
        var keys = Array(message, "accountKeys").EnumerateArray().ToList();
        var parsed = keys.Count > 0 && keys[0].ValueKind == JsonValueKind.Object;
        var accounts = keys.Select(key => Address(parsed ? String(key, "pubkey") : key.GetString())).ToList();
        if (!parsed && meta.TryGetProperty("loadedAddresses", out var loaded)
            && loaded.ValueKind == JsonValueKind.Object)
        {
            accounts.AddRange(Array(loaded, "writable").EnumerateArray().Select(key => Address(key.GetString())));
            accounts.AddRange(Array(loaded, "readonly").EnumerateArray().Select(key => Address(key.GetString())));
        }

        return accounts.Count > 0 ? accounts : throw new FormatException("A transaction has at least one account.");
    }

    /// <summary>Reads each account's lamports before or after, one per account.</summary>
    /// <param name="meta">The transaction's meta.</param>
    /// <param name="name">The list, <c>preBalances</c> or <c>postBalances</c>.</param>
    /// <param name="count">How many accounts the transaction has.</param>
    /// <returns>The lamports.</returns>
    private static List<BigInteger> Lamports(JsonElement meta, string name, int count)
    {
        var values = Array(meta, name).EnumerateArray()
            .Select(value => value.TryGetInt64(out var lamports) && lamports >= 0
                ? new BigInteger(lamports)
                : throw new FormatException("A balance is not a whole number of lamports."))
            .ToList();
        return values.Count == count ? values : throw new FormatException($"{name} has not one entry per account.");
    }

    /// <summary>Reads the token accounts' balances before or after.</summary>
    /// <param name="meta">The transaction's meta.</param>
    /// <param name="name">The list, <c>preTokenBalances</c> or <c>postTokenBalances</c>.</param>
    /// <param name="count">How many accounts the transaction has.</param>
    /// <returns>The balances; empty when the list is missing.</returns>
    private static List<SolanaTokenBalance> TokenBalances(JsonElement meta, string name, int count)
    {
        if (!meta.TryGetProperty(name, out var list) || list.ValueKind == JsonValueKind.Null)
        {
            return [];
        }

        var balances = new List<SolanaTokenBalance>();
        foreach (var item in list.EnumerateArray())
        {
            var index = Long(item, "accountIndex") ?? throw new FormatException("accountIndex is missing.");
            if (index < 0 || index >= count)
            {
                throw new FormatException("A token balance names an account the transaction does not have.");
            }

            var owner = item.TryGetProperty("owner", out var named) && named.ValueKind == JsonValueKind.String
                ? Address(named.GetString())
                : null;
            var amount = Object(item, "uiTokenAmount");
            balances.Add(new SolanaTokenBalance(
                (int)index, Mint(String(item, "mint")), owner, Units(String(amount, "amount")), Decimals(amount)));
        }

        return balances;
    }

    /// <summary>
    /// Opens a JSON-RPC answer and gives its <c>result</c>, or throws by its <c>error</c>: a rate limit is a wait, a
    /// skipped slot is not found, anything else a failure.
    /// </summary>
    /// <param name="body">The body.</param>
    /// <param name="result">The result.</param>
    /// <returns>The document, which the caller disposes.</returns>
    private static JsonDocument Envelope(byte[] body, out JsonElement result)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new FormatException("The answer is not JSON.", exception);
        }

        var root = document.RootElement;
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("error", out var error)
            && error.ValueKind == JsonValueKind.Object)
        {
            var code = Long(error, "code");
            document.Dispose();
            throw code switch
            {
                RateLimitCode or JsonRpcRateLimitCode => new SourceBusyException(),
                SlotSkippedCode or SlotSkippedInStorageCode => new SourceNotFoundException(),
                _ => new FormatException("The node answered with an error: "
                    + (code?.ToString(CultureInfo.InvariantCulture) ?? "?")),
            };
        }

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("result", out result))
        {
            document.Dispose();
            throw new FormatException("The answer is not a JSON-RPC result.");
        }

        return document;
    }

    /// <summary>Reads a Solana address the answer gives.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The address.</returns>
    /// <exception cref="FormatException">It is not one.</exception>
    private static EthereumAddress Address(string? text) =>
        EthereumAddress.TryParseSolana(text, out var address, out _)
            ? address
            : throw new FormatException("Not a Solana address.");

    /// <summary>Reads a mint, which is an address.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The mint as written.</returns>
    private static string Mint(string? text) => Address(text).Value;

    /// <summary>Reads an amount in a token's smallest unit, which the answer writes as a string.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The amount.</returns>
    private static BigInteger Units(string? text) =>
        BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var units)
            ? units
            : throw new FormatException("An amount is not a whole number.");

    /// <summary>Reads a token's decimals from its amount.</summary>
    /// <param name="amount">The <c>uiTokenAmount</c> or <c>tokenAmount</c>.</param>
    /// <returns>The decimals.</returns>
    private static int Decimals(JsonElement amount) =>
        Long(amount, "decimals") is { } decimals and >= 0 and <= byte.MaxValue
            ? (int)decimals
            : throw new FormatException("A token's decimals are not between 0 and 255.");

    /// <summary>Reads a property that must be an object.</summary>
    /// <param name="item">The element.</param>
    /// <param name="name">The property.</param>
    /// <returns>The object.</returns>
    private static JsonElement Object(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Object
            ? value
            : throw new FormatException($"{name} is missing.");

    /// <summary>Reads a property that must be a list.</summary>
    /// <param name="item">The element.</param>
    /// <param name="name">The property.</param>
    /// <returns>The list.</returns>
    private static JsonElement Array(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Array
            ? value
            : throw new FormatException($"{name} is missing.");

    /// <summary>Reads a property that is a string, if it is there.</summary>
    /// <param name="item">The element.</param>
    /// <param name="name">The property.</param>
    /// <returns>The string, or <see langword="null"/>.</returns>
    private static string? String(JsonElement item, string name) =>
        item.ValueKind == JsonValueKind.Object && item.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Reads a property that is a whole number, if it is there.</summary>
    /// <param name="item">The element.</param>
    /// <param name="name">The property.</param>
    /// <returns>The number, or <see langword="null"/> when it is missing or null.</returns>
    private static long? Long(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value)
            || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)
            ? number
            : throw new FormatException($"{name} is not a whole number.");
    }
}
