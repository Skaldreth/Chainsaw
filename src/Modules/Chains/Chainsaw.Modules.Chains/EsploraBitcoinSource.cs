using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Bitcoin from an Esplora API (the API of mempool.space and Blockstream, which can also be run in-house), asked only
/// through <see cref="IExitHttp"/>. Every response is sealed in the case before it is used; an answer that cannot
/// have changed is taken from the case without asking; without an exit the case answers and says what is missing.
/// </summary>
internal sealed class EsploraBitcoinSource : IBitcoinSource
{
    /// <summary>
    /// How old the newest known height may be before a lookup that depends on depth asks for a new one.
    /// </summary>
    private static readonly TimeSpan TipFreshness = TimeSpan.FromMinutes(1);

    /// <summary>The longest script that can run, in hex characters (10 000 bytes).</summary>
    private const int MaximumScriptHex = 20_000;

    /// <summary>The time now, to decide whether the known height is fresh.</summary>
    private readonly IClock _clock;

    /// <summary>The settings.</summary>
    private readonly BitcoinSourceOptions _options;

    /// <summary>Reads Esplora's JSON.</summary>
    private readonly EsploraParser _parser;

    /// <summary>Seals and finds the raw responses.</summary>
    private readonly RawResponseStore _store;

    /// <summary>Answers the lookups through the endpoints and the case.</summary>
    private readonly SourceLookups _lookups;

    /// <summary>Creates the source with the default endpoints in memory only, as a test or a tool does.</summary>
    /// <param name="http">The only way out.</param>
    /// <param name="operators">The operator profile.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The settings, with the default endpoints.</param>
    /// <param name="timeZone">The time zone local times are recorded in.</param>
    /// <exception cref="ArgumentException">A default endpoint is not an absolute <c>https</c> address of its
    /// own.</exception>
    public EsploraBitcoinSource(
        IExitHttp http, IOperatorProfileStore operators, IClock clock, BitcoinSourceOptions options,
        TimeZoneInfo timeZone)
        : this(http, operators, clock, options, timeZone,
            new ChainSources([new EsploraApi(options)], http, null, null, clock, new ChainSourcesOptions()))
    {
    }

    /// <summary>Creates the source.</summary>
    /// <param name="http">The only way out.</param>
    /// <param name="operators">The operator profile.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The settings.</param>
    /// <param name="timeZone">The time zone local times are recorded in.</param>
    /// <param name="sources">The chains' endpoints, of which Bitcoin's are asked.</param>
    public EsploraBitcoinSource(
        IExitHttp http, IOperatorProfileStore operators, IClock clock, BitcoinSourceOptions options,
        TimeZoneInfo timeZone, ChainSources sources)
    {
        _clock = clock;
        _options = options;
        _parser = new EsploraParser(options.Network);
        _store = new RawResponseStore(timeZone, options.Network);
        if (options.SafeConfirmations < 1)
        {
            throw new ArgumentException("The depth must be at least 1.", nameof(options));
        }

        _lookups = new SourceLookups(
            Chain.Bitcoin, "Bitcoin", http, operators, sources, _store, options.SafeConfirmations,
            async (lookups, @case, cancellationToken) => await RefreshHeightAsync(@case, cancellationToken)
                .ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public BitcoinNetwork Network => _options.Network;

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
        ICase @case, BitcoinAddress address, Txid? after = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(address);
        if (address.Network != Network)
        {
            return Task.FromResult(SourceLookups.Refused<BitcoinAddressPage>(
                "Adressen hör till ett annat Bitcoin-nät än källan. Inget skickades."));
        }

        var segment = Uri.EscapeDataString(address.Value);
        var path = after is null ? $"address/{segment}/txs" : $"address/{segment}/txs/chain/{after.Value}";
        var subject = after is null ? address.Value : $"{address.Value}/{after.Value}";
        var query = new LookupQuery<BitcoinAddressPage>(
            ResponseKinds.AddressTransactions,
            subject,
            path,
            body => Page(address, after, _parser.ParseAddressTransactions(body, address)),
            page => IndexOf(page.Transactions),
            reader => SourceLookups.StoredAs(_store.Latest(reader, ResponseKinds.AddressTransactions, subject),
                body => Page(address, after, _parser.ParseAddressTransactions(body, address))),
            page => after is null || page.Transactions.Count == 0 ? null : Shallowest(page.Transactions));
        return _lookups.LookupAsync(@case, query, LookupMode.AsNeeded, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(txid);
        var query = new LookupQuery<BitcoinTransaction>(
            ResponseKinds.Transaction,
            txid.Value,
            $"tx/{txid.Value}",
            body => _parser.ParseTransaction(body, txid),
            transaction => IndexOf([transaction]),
            reader => StoredTransaction(reader, txid),
            transaction => transaction.Block?.Height,
            Changed: BlockChange);
        var lookup = await _lookups.LookupAsync(@case, query, mode, cancellationToken).ConfigureAwait(false);
        if (lookup.Value is null)
        {
            return lookup;
        }

        var withSpends = await WithSpendsAsync(@case, lookup, cancellationToken).ConfigureAwait(false);
        var bytes = await _lookups.LookupAsync(@case, HexQuery(txid), LookupMode.CaseOnly, cancellationToken)
            .ConfigureAwait(false);
        return bytes.Value is null ? withSpends : CheckedAgainst(withSpends, bytes);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<BitcoinTransaction>> VerifyTransactionAsync(
        ICase @case, Txid txid, CancellationToken cancellationToken = default)
    {
        var lookup = await GetTransactionAsync(@case, txid, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Value is not { BytesVerified: false })
        {
            return lookup;
        }

        var bytes = await _lookups.LookupAsync(@case, HexQuery(txid), LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (bytes.Value is not null)
        {
            return CheckedAgainst(lookup, bytes);
        }

        var note = $"Tx:ens egna bytes kunde inte hämtas, så den är inte kontrollerad mot dem. {bytes.Problem}";
        return lookup with
        {
            Origins = [.. lookup.Origins.Concat(bytes.Origins).OrderBy(origin => origin.Sequence)],
            Notes = [.. lookup.Notes, note.TrimEnd()],
        };
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinAddressSummary>> GetAddressSummaryAsync(
        ICase @case, BitcoinAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(address);
        if (address.Network != Network)
        {
            return Task.FromResult(SourceLookups.Refused<BitcoinAddressSummary>(
                "Adressen hör till ett annat Bitcoin-nät än källan. Inget skickades."));
        }

        var query = new LookupQuery<BitcoinAddressSummary>(
            ResponseKinds.AddressSummary,
            address.Value,
            $"address/{Uri.EscapeDataString(address.Value)}",
            body => EsploraParser.ParseAddressSummary(body, address),
            _ => ResponseIndex.None,
            reader => SourceLookups.StoredAs(_store.Latest(reader, ResponseKinds.AddressSummary, address.Value),
                body => EsploraParser.ParseAddressSummary(body, address)),
            _ => null);
        return _lookups.LookupAsync(@case, query, LookupMode.AsNeeded, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>> GetOutputSpendsAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(txid);
        // The transaction's own outputs say which can never be spent (OP_RETURN), which never wait for a spend.
        var transaction = await Task.Run(
            () => @case.Read(reader => StoredTransaction(reader, txid)?.Value), cancellationToken)
            .ConfigureAwait(false);
        var query = new LookupQuery<IReadOnlyList<BitcoinOutputSpend>>(
            ResponseKinds.OutputSpends,
            txid.Value,
            $"tx/{txid.Value}/outspends",
            body => EsploraParser.ParseOutputSpends(body),
            _ => ResponseIndex.None,
            reader => SourceLookups.StoredAs(_store.Latest(reader, ResponseKinds.OutputSpends, txid.Value),
                body => EsploraParser.ParseOutputSpends(body)),
            spends => SettledAt(spends, transaction));
        return await _lookups.LookupAsync(@case, query, mode, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<BitcoinBlock>> GetBlockAsync(
        ICase @case, BlockHash hash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(hash);
        var query = new LookupQuery<BitcoinBlock>(
            ResponseKinds.Block,
            hash.Value,
            $"block/{hash.Value}",
            body => EsploraParser.ParseBlock(body, hash),
            _ => ResponseIndex.None,
            reader => SourceLookups.StoredAs(_store.Latest(reader, ResponseKinds.Block, hash.Value),
                body => EsploraParser.ParseBlock(body, hash)),
            block => block.Height,
            NeverChanges: true);
        var lookup = await _lookups.LookupAsync(@case, query, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Value is not { } block)
        {
            return lookup;
        }

        var header = await _lookups.LookupAsync(@case, HeaderQuery(hash), LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        var origins = lookup.Origins.Concat(header.Origins).DistinctBy(origin => origin.Sequence)
            .OrderBy(origin => origin.Sequence).ToList();
        if (header.Value is null)
        {
            var note = $"Blockets huvud kunde inte hämtas, så blocket är inte kontrollerat mot det. {header.Problem}";
            return lookup with { Origins = origins, Notes = [.. lookup.Notes, note.TrimEnd()] };
        }

        return BlockHeaderReader.Compare(block, header.Value) is null
            ? lookup with { Value = block with { HeaderVerified = true }, Origins = origins }
            : new BitcoinLookup<BitcoinBlock>(LookupStatus.Failed, null, origins,
                "Källans uppgifter om blocket stämmer inte med blockets eget huvud, som är sparat i ärendet; de "
                + "används inte.");
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(ICase @case, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return _lookups.LookupAsync(@case, TipQuery(), LookupMode.AsNeeded, cancellationToken);
    }

    /// <summary>The lookup of a transaction's own bytes, which never change in the parts its txid commits to.</summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>The query.</returns>
    private LookupQuery<RawTransaction> HexQuery(Txid txid) => new(
        ResponseKinds.TransactionHex,
        txid.Value,
        $"tx/{txid.Value}/hex",
        body => RawTransactionReader.ParseHex(body, txid),
        _ => ResponseIndex.None,
        reader => SourceLookups.StoredAs(_store.Latest(reader, ResponseKinds.TransactionHex, txid.Value),
            body => RawTransactionReader.ParseHex(body, txid)),
        _ => null,
        NeverChanges: true);

    /// <summary>The lookup of a block's header, which never changes.</summary>
    /// <param name="hash">The block.</param>
    /// <returns>The query.</returns>
    private LookupQuery<BlockHeader> HeaderQuery(BlockHash hash) => new(
        ResponseKinds.BlockHeader,
        hash.Value,
        $"block/{hash.Value}/header",
        body => BlockHeaderReader.ParseHex(body, hash),
        _ => ResponseIndex.None,
        reader => SourceLookups.StoredAs(_store.Latest(reader, ResponseKinds.BlockHeader, hash.Value),
            body => BlockHeaderReader.ParseHex(body, hash)),
        _ => null,
        NeverChanges: true);

    /// <summary>
    /// Checks a transaction against its bytes: marks it checked when they agree, and refuses it when they do not,
    /// since the bytes hash to the txid and the description therefore is not that transaction.
    /// </summary>
    /// <param name="lookup">The transaction's lookup, with a value.</param>
    /// <param name="bytes">The bytes' lookup, with a value.</param>
    /// <returns>The checked lookup, or a failed one.</returns>
    private static BitcoinLookup<BitcoinTransaction> CheckedAgainst(
        BitcoinLookup<BitcoinTransaction> lookup, BitcoinLookup<RawTransaction> bytes)
    {
        var origins = lookup.Origins.Concat(bytes.Origins).DistinctBy(origin => origin.Sequence)
            .OrderBy(origin => origin.Sequence).ToList();
        return RawTransactionReader.Compare(lookup.Value!, bytes.Value!) is null
            ? lookup with { Value = lookup.Value! with { BytesVerified = true }, Origins = origins }
            : new BitcoinLookup<BitcoinTransaction>(LookupStatus.Failed, null, origins,
                "Källans uppgifter om tx:en stämmer inte med tx:ens egna bytes, som är sparade i ärendet; de "
                + "används inte.");
    }

    /// <summary>
    /// Says that a transaction has left the block the case saw it in: the block is no longer part of the chain the
    /// source follows (a reorganisation), and the transaction is now in another block or unconfirmed.
    /// </summary>
    /// <param name="before">The case's earlier answer.</param>
    /// <param name="now">The fresh answer.</param>
    /// <returns>The text for the user, or <see langword="null"/> when the block is the same.</returns>
    private static string? BlockChange(BitcoinTransaction before, BitcoinTransaction now)
    {
        if (before.Block is not { } earlier || now.Block?.Hash == earlier.Hash)
        {
            return null;
        }

        var where = now.Block is { } later
            ? string.Create(CultureInfo.InvariantCulture, $"ligger nu i block {later.Height} ({later.Hash})")
            : "är nu obekräftad";
        var earlierText = string.Create(
            CultureInfo.InvariantCulture, $"Tx:en låg tidigare i block {earlier.Height} ({earlier.Hash})");
        return $"{earlierText} men {where}: blocket är inte längre en del av kedjan enligt källan (en omorganisation). "
            + "Båda svaren är sparade i ärendet.";
    }

    /// <summary>The lookup of the chain's height, which always changes.</summary>
    /// <returns>The query.</returns>
    private LookupQuery<BitcoinTip> TipQuery() => new(
        ResponseKinds.TipHeight,
        string.Empty,
        "blocks/tip/height",
        body => EsploraParser.ParseTipHeight(body),
        tip => new ResponseIndex([], tip.Height),
        reader => SourceLookups.StoredAs(_store.Latest(reader, ResponseKinds.TipHeight, string.Empty),
            body => EsploraParser.ParseTipHeight(body)),
        _ => null);








    /// <summary>
    /// Asks for the chain's height when the newest known one is older than <see cref="TipFreshness"/>, so that an
    /// answer whose block has become deep enough can be taken from the case.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns><see langword="true"/> when a new height was fetched.</returns>
    private async Task<bool> RefreshHeightAsync(ICase @case, CancellationToken cancellationToken)
    {
        var tip = TipQuery();
        var (stored, _) = await _lookups.ReadCaseAsync(@case, tip, cancellationToken).ConfigureAwait(false);
        if (stored is { } known && _clock.UtcNow - known.Response.Origin.ReceivedUtc < TipFreshness)
        {
            return false;
        }

        var fresh = await _lookups.FetchAsync(@case, tip, stored, cancellationToken).ConfigureAwait(false);
        return fresh.Status == LookupStatus.Fetched;
    }

    /// <summary>Adds who spends the outputs, from the case's newest answer, without asking the source.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="lookup">The transaction's lookup.</param>
    /// <param name="cancellationToken">Stops the lookup before it starts.</param>
    /// <returns>The lookup with the spends and their origin, or the same lookup when the case has none.</returns>
    private async Task<BitcoinLookup<BitcoinTransaction>> WithSpendsAsync(
        ICase @case, BitcoinLookup<BitcoinTransaction> lookup, CancellationToken cancellationToken)
    {
        var transaction = lookup.Value!;
        var (stored, heightThen) = await Task.Run(
            () => @case.Read(reader =>
            {
                var found = SourceLookups.StoredAs(
                    _store.Latest(reader, ResponseKinds.OutputSpends, transaction.Txid.Value),
                    body => EsploraParser.ParseOutputSpends(body));
                var height = found is { } spends ? _store.KnownHeight(reader, spends.Response.Origin.Sequence) : null;
                return (found, height);
            }),
            cancellationToken).ConfigureAwait(false);
        if (stored is not { } spends || spends.Value.Count != transaction.Outputs.Count)
        {
            return lookup;
        }

        var outputs = transaction.Outputs.Select(
            output => output with { SpentBy = spends.Value[output.Index].SpentBy });
        return lookup with
        {
            Value = transaction with { Outputs = [.. outputs] },
            Origins = [.. lookup.Origins.Append(spends.Response.Origin).OrderBy(origin => origin.Sequence)],
            Problem = Join(lookup.Problem, SpendsProblem(spends.Value, transaction, spends.Response, heightThen)),
        };
    }

    /// <summary>
    /// Says that the spends are as of when they were fetched, unless every output is spent deep in the chain: an
    /// output shown as unspent may have been spent since.
    /// </summary>
    /// <param name="spends">The spends.</param>
    /// <param name="transaction">The transaction, whose outputs say which can never be spent.</param>
    /// <param name="response">The response they come from.</param>
    /// <param name="heightThen">The highest block the case knew of when the response was sealed.</param>
    /// <returns>The text for the user, or <see langword="null"/> when the spends cannot have changed.</returns>
    private string? SpendsProblem(
        IReadOnlyList<BitcoinOutputSpend> spends, BitcoinTransaction transaction, StoredResponse response,
        long? heightThen)
    {
        if (SettledAt(spends, transaction) is { } height
            && SourceLookups.Confirmations(height, heightThen) >= _options.SafeConfirmations)
        {
            return null;
        }

        var fetchedAt = SourceLookups.LocalText(response.Origin.ReceivedLocal);
        return $"Vilka utgångar som är spenderade är hämtat {fetchedAt} och kan ha ändrats sedan.";
    }

    /// <summary>Joins two texts for the user, either of which may be missing.</summary>
    /// <param name="first">The first text.</param>
    /// <param name="second">The second text.</param>
    /// <returns>Both, one, or <see langword="null"/>.</returns>
    private static string? Join(string? first, string? second) =>
        first is null || second is null ? first ?? second : $"{first} {second}";

    /// <summary>Finds a transaction in the case, alone or on an address's page.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="txid">The transaction.</param>
    /// <returns>The transaction and its response, or <see langword="null"/>.</returns>
    private (BitcoinTransaction Value, StoredResponse Response)? StoredTransaction(ICaseReader reader, Txid txid)
    {
        // A description that contradicts the transaction's sealed bytes is passed over, so that one bad answer from
        // the source never hides an earlier good one, nor keeps the transaction from being asked for again.
        var bytes = SourceLookups.StoredAs(_store.Latest(reader, ResponseKinds.TransactionHex, txid.Value),
            body => RawTransactionReader.ParseHex(body, txid))?.Value;
        foreach (var response in _store.AllWithTransaction(reader, txid))
        {
            var transaction = SourceLookups.StoredAs(response, body => response.Kind == ResponseKinds.Transaction
                ? _parser.ParseTransaction(body, txid)
                : _parser.ParseTransactionList(body).FirstOrDefault(candidate => candidate.Txid == txid));
            if (transaction is { } found && (bytes is null || RawTransactionReader.Compare(found.Value, bytes) is null))
            {
                return found;
            }
        }

        return null;
    }





    /// <summary>Gives the height of the shallowest of some transactions, when they are all confirmed.</summary>
    /// <param name="transactions">The transactions.</param>
    /// <returns>The highest block height, or <see langword="null"/> when one is unconfirmed.</returns>
    private static long? Shallowest(IEnumerable<BitcoinTransaction> transactions)
    {
        long highest = -1;
        foreach (var transaction in transactions)
        {
            if (transaction.Block is null)
            {
                return null;
            }

            highest = Math.Max(highest, transaction.Block.Height);
        }

        return highest < 0 ? null : highest;
    }

    /// <summary>
    /// Gives the height of the shallowest spend, when every output that can be spent is spent in a block; an unspent
    /// output can still be spent, so the answer can change. An output that can never be spent (OP_RETURN, or a script
    /// too large to run) never waits for a spend, so it counts from the transaction's own block.
    /// </summary>
    /// <param name="spends">The spends.</param>
    /// <param name="transaction">
    /// The transaction, when the case has it; without it every output counts as one that can be spent.
    /// </param>
    /// <returns>The highest block height, or <see langword="null"/>.</returns>
    private static long? SettledAt(IReadOnlyList<BitcoinOutputSpend> spends, BitcoinTransaction? transaction)
    {
        if (spends.Count == 0)
        {
            return null;
        }

        var outputs = transaction?.Outputs.Count == spends.Count ? transaction.Outputs : null;
        var highest = outputs is null ? null : transaction!.Block?.Height;
        foreach (var spend in spends)
        {
            if (spend.SpentBy is null && outputs is not null && IsUnspendable(outputs[spend.Index]))
            {
                continue;
            }

            if (spend.SpentBy?.Block is not { } block)
            {
                return null;
            }

            highest = Math.Max(highest ?? block.Height, block.Height);
        }

        return highest;
    }

    /// <summary>
    /// Says whether an output can never be spent: an OP_RETURN script, or a script larger than the 10 000 bytes a
    /// script may have when it runs.
    /// </summary>
    /// <param name="output">The output.</param>
    /// <returns><see langword="true"/> when no transaction can ever spend it.</returns>
    public static bool IsUnspendable(BitcoinOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        return output.ScriptType == BitcoinScriptType.OpReturn || output.Script.Length > MaximumScriptHex;
    }

    /// <summary>
    /// Makes a page of an address's transactions and works out where the next page starts: after the last confirmed
    /// transaction, as long as the page has one. The page size is the source's own, so only an empty page ends the
    /// history; a short page could just be a source with smaller pages.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="after">The txid the page continues after.</param>
    /// <param name="transactions">The transactions on the page.</param>
    /// <returns>The page.</returns>
    private static BitcoinAddressPage Page(BitcoinAddress address, Txid? after, List<BitcoinTransaction> transactions)
    {
        var lastConfirmed = transactions.LastOrDefault(transaction => transaction.IsConfirmed);
        return new BitcoinAddressPage(address, after, transactions, lastConfirmed?.Txid);
    }

    /// <summary>Gives what some transactions add to the indexes.</summary>
    /// <param name="transactions">The transactions.</param>
    /// <returns>The index entry.</returns>
    private static ResponseIndex IndexOf(IEnumerable<BitcoinTransaction> transactions) =>
        new([.. transactions.Select(transaction => (transaction.Txid, transaction.Block?.Height))], null);
}
