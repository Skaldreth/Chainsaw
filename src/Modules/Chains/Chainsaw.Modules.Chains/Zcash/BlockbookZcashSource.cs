using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Zcash's transparent part from a Blockbook (<see cref="BlockbookApi"/>), asked only through the chains' endpoints
/// (the exit, or the user's own node) and sealed under <see cref="RawResponseStore.ZcashChainName"/>. A transaction is
/// two answers: Blockbook's, with what each input spends, and zcashd's own (<c>tx-specific</c>), with the shielded
/// pools' value balances; both are sealed and they must agree. Who spends an output is read from the output's
/// address's history from the transaction's block, since Blockbook does not name the spender itself.
/// </summary>
internal sealed class BlockbookZcashSource : IZcashSource
{
    /// <summary>The kind of zcashd's own answer about a transaction, with the shielded pools.</summary>
    public const string ShieldedKind = "zcash.tx-specific";

    /// <summary>The kind of an address's history from a height, asked to find who spent an output.</summary>
    public const string AddressSpendsKind = "zcash.address-spends";

    /// <summary>How old the newest known height may be before it is asked again.</summary>
    private static readonly TimeSpan TipFreshness = TimeSpan.FromMinutes(1);

    /// <summary>The clock.</summary>
    private readonly IClock _clock;

    /// <summary>The settings.</summary>
    private readonly ZcashSourceOptions _options;

    /// <summary>Answers the lookups through the endpoints and the case.</summary>
    private readonly SourceLookups _lookups;

    /// <summary>Creates the source.</summary>
    /// <param name="sources">The chains' endpoints, of which Zcash's are asked.</param>
    /// <param name="operators">The operator profile.</param>
    /// <param name="clock">The clock.</param>
    /// <param name="options">The settings.</param>
    /// <param name="timeZone">The time zone local times are recorded in.</param>
    /// <exception cref="ArgumentException">The depth or the page size is less than 1.</exception>
    public BlockbookZcashSource(
        ChainSources sources, IOperatorProfileStore operators, IClock clock, ZcashSourceOptions options,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.PageSize < 1)
        {
            throw new ArgumentException("A page holds at least one transaction.", nameof(options));
        }

        (_clock, _options) = (clock, options);
        _lookups = new SourceLookups(
            Chain.Zcash, Chain.Zcash.DisplayName(), operators, sources,
            new RawResponseStore(timeZone, BitcoinNetwork.Zcash), options.SafeConfirmations,
            async (_, @case, cancellationToken) => await RefreshHeightAsync(@case, cancellationToken)
                .ConfigureAwait(false));
    }

    /// <inheritdoc/>
    public BitcoinNetwork Network => BitcoinNetwork.Zcash;

    /// <inheritdoc/>
    public async Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
        ICase @case, BitcoinAddress address, Txid? after = null, CancellationToken cancellationToken = default) =>
        await GetAddressTransactionsAsync(@case, address, after, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);

    /// <inheritdoc/>
    /// <remarks>
    /// Blockbook pages by number, so the page after a transaction is asked as the history up to the transaction's
    /// block; the transactions of that block that came before are given again and passed over by the caller.
    /// </remarks>
    public async Task<BitcoinLookup<BitcoinAddressPage>> GetAddressTransactionsAsync(
        ICase @case, BitcoinAddress address, Txid? after, LookupMode mode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(address);
        if (address.Network != BitcoinNetwork.Zcash)
        {
            return SourceLookups.Refused<BitcoinAddressPage>(
                "Adressen hör till en annan kedja än källan. Inget skickades.");
        }

        long? upTo = null;
        if (after is not null)
        {
            var known = await GetTransactionAsync(@case, after, LookupMode.CaseOnly, cancellationToken)
                .ConfigureAwait(false);
            upTo = known.Value?.Block?.Height;
        }

        var to = upTo is { } height ? "&to=" + height.ToString(CultureInfo.InvariantCulture) : string.Empty;
        var query = new LookupQuery<BitcoinAddressPage>(
            ResponseKinds.AddressTransactions,
            after is null ? address.Value : $"{address.Value}/{after.Value}",
            $"api/v2/address/{Uri.EscapeDataString(address.Value)}?details=txs&pageSize={_options.PageSize}{to}",
            body =>
            {
                var (transactions, more) = BlockbookParser.ParseAddressPage(body, address);
                return new BitcoinAddressPage(address, after, transactions, more ? transactions[^1].Txid : null);
            },
            page => IndexOf(page.Transactions),
            _ => null);
        return await _lookups.LookupAsync(@case, query, mode, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<BitcoinTransaction>> GetTransactionAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(txid);
        txid = txid.On(Chain.Zcash);
        var transparent = await _lookups.LookupAsync(@case, TransactionQuery(txid), mode, cancellationToken)
            .ConfigureAwait(false);
        if (transparent.Value is not { } read)
        {
            return transparent;
        }

        // zcashd's answer is asked in the same mode, and of the same block: an answer from another block is asked
        // again, so that the two parts are always of the same transaction in the same place.
        var shielded = await _lookups.LookupAsync(@case, ShieldedQuery(read), mode, cancellationToken)
            .ConfigureAwait(false);
        var origins = SourceLookups.Ordered(transparent.Origins.Concat(shielded.Origins));
        if (shielded.Value is not { } whole)
        {
            return new BitcoinLookup<BitcoinTransaction>(
                shielded.Status == LookupStatus.NotInCase ? LookupStatus.NotInCase : LookupStatus.Failed,
                null,
                origins,
                $"Tx:ens skyddade del (zcashd) kunde inte läsas. {shielded.Problem}".TrimEnd());
        }

        return transparent with
        {
            Status = Worse(transparent.Status, shielded.Status),
            Value = whole,
            Origins = origins,
            Problem = Join(transparent.Problem, shielded.Problem),
            Notes = [.. transparent.Notes, .. shielded.Notes],
        };
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Zcash's newer transaction ids are a tree of BLAKE2b hashes over the transaction's parts (ZIP 244), not a double
    /// SHA-256 of its bytes, so the transaction is not checked against its own bytes and is never marked so.
    /// </remarks>
    public Task<BitcoinLookup<BitcoinTransaction>> VerifyTransactionAsync(
        ICase @case, Txid txid, CancellationToken cancellationToken = default) =>
        GetTransactionAsync(@case, txid, LookupMode.AsNeeded, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinAddressSummary>> GetAddressSummaryAsync(
        ICase @case, BitcoinAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(address);
        if (address.Network != BitcoinNetwork.Zcash)
        {
            return Task.FromResult(SourceLookups.Refused<BitcoinAddressSummary>(
                "Adressen hör till en annan kedja än källan. Inget skickades."));
        }

        var query = new LookupQuery<BitcoinAddressSummary>(
            ResponseKinds.AddressSummary,
            address.Value,
            $"api/v2/address/{Uri.EscapeDataString(address.Value)}?details=basic",
            body => BlockbookParser.ParseAddressSummary(body, address),
            _ => ResponseIndex.None,
            _ => null);
        return _lookups.LookupAsync(@case, query, LookupMode.AsNeeded, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>> GetOutputSpendsAsync(
        ICase @case, Txid txid, LookupMode mode = LookupMode.AsNeeded, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(txid);
        var asked = mode == LookupMode.CaseOnly ? LookupMode.CaseOnly : LookupMode.AsNeeded;
        var lookup = await GetTransactionAsync(@case, txid, asked, cancellationToken).ConfigureAwait(false);
        if (lookup.Value is not { } transaction)
        {
            return new BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>(
                lookup.Status, null, lookup.Origins, lookup.Problem);
        }

        var spends = new BitcoinSpend?[transaction.Outputs.Count];
        var origins = new List<ResponseOrigin>();
        var status = LookupStatus.FromCase;
        string? problem = null;
        foreach (var address in transaction.Outputs.Select(output => output.Address).OfType<BitcoinAddress>()
            .Distinct())
        {
            var history = await _lookups.LookupAsync(
                    @case, SpendsQuery(transaction, address), mode, cancellationToken)
                .ConfigureAwait(false);
            origins.AddRange(history.Origins);
            if (history.Value is not { } page)
            {
                return new BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>(
                    history.Status, null, SourceLookups.Ordered(origins), history.Problem);
            }

            if (!FindSpenders(transaction, address, page, spends))
            {
                return new BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>(
                    LookupStatus.Failed, null, SourceLookups.Ordered(origins),
                    "Adressen har fler tx än källan ger på en sida, så vem som spenderade utgången kunde inte läsas.");
            }

            status = Worse(status, history.Status);
            problem = Join(problem, history.Problem);
        }

        IReadOnlyList<BitcoinOutputSpend> answer =
            [.. spends.Select((spend, index) => new BitcoinOutputSpend(index, spend))];
        return new BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>(
            status, answer, SourceLookups.Ordered(origins), problem);
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinBlock>> GetBlockAsync(
        ICase @case, BlockHash hash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(hash);
        var query = new LookupQuery<BitcoinBlock>(
            ResponseKinds.Block,
            hash.Value,
            $"api/v2/block/{hash.Value}?pageSize=1",
            body => BlockbookParser.ParseBlock(body, hash),
            block => new ResponseIndex([], block.Height),
            block => block.Height);
        return _lookups.LookupAsync(@case, query, LookupMode.AsNeeded, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(ICase @case, CancellationToken cancellationToken = default)
        => GetTipHeightAsync(@case, LookupMode.AsNeeded, cancellationToken);

    /// <inheritdoc/>
    public Task<BitcoinLookup<BitcoinTip>> GetTipHeightAsync(
        ICase @case, LookupMode mode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return _lookups.LookupAsync(@case, TipQuery(), mode, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<BitcoinLookup<ChainTip>> GetTipAsync(ICase @case, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var tip = await GetTipHeightAsync(@case, LookupMode.AsNeeded, cancellationToken).ConfigureAwait(false);
        if (tip.Value is not { } height)
        {
            return new BitcoinLookup<ChainTip>(tip.Status, null, tip.Origins, tip.Problem);
        }

        var index = new LookupQuery<BlockHash>(
            ResponseKinds.BlockAtHeight,
            height.Height.ToString(CultureInfo.InvariantCulture),
            "api/v2/block-index/" + height.Height.ToString(CultureInfo.InvariantCulture),
            body => BlockbookParser.ParseBlockIndex(body),
            _ => new ResponseIndex([], height.Height),
            _ => null);
        var hash = await _lookups.LookupAsync(@case, index, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        var origins = tip.Origins.Concat(hash.Origins).ToList();
        if (hash.Value is not { } found)
        {
            return new BitcoinLookup<ChainTip>(hash.Status, null, SourceLookups.Ordered(origins), hash.Problem);
        }

        var block = await GetBlockAsync(@case, found, cancellationToken).ConfigureAwait(false);
        origins.AddRange(block.Origins);
        return block.Value is { } newest
            ? new BitcoinLookup<ChainTip>(
                tip.Status, new ChainTip(newest.Height, newest.Hash.Value, newest.Time), SourceLookups.Ordered(origins),
                tip.Problem)
            : new BitcoinLookup<ChainTip>(block.Status, null, SourceLookups.Ordered(origins), block.Problem);
    }

    /// <summary>The lookup of a transaction's transparent part, which holds once its block is deep.</summary>
    /// <param name="txid">The transaction, on Zcash.</param>
    /// <returns>The query.</returns>
    private static LookupQuery<BitcoinTransaction> TransactionQuery(Txid txid) => new(
        ResponseKinds.Transaction,
        txid.Value,
        $"api/v2/tx/{txid.Value}",
        body => BlockbookParser.ParseTransaction(body, txid),
        transaction => IndexOf([transaction]),
        transaction => transaction.Block?.Height);

    /// <summary>
    /// The lookup of zcashd's own answer about a transaction, with the pools' value balances, read together with the
    /// transparent part it must agree with.
    /// </summary>
    /// <param name="transparent">The transaction's transparent part.</param>
    /// <returns>The query, whose value is the whole transaction.</returns>
    private static LookupQuery<BitcoinTransaction> ShieldedQuery(BitcoinTransaction transparent) => new(
        ShieldedKind,
        transparent.Txid.Value,
        $"api/v2/tx-specific/{transparent.Txid.Value}",
        body => BlockbookParser.WithShielded(body, transparent),
        _ => ResponseIndex.None,
        transaction => transaction.Block?.Height);

    /// <summary>
    /// The lookup of an output's address's history from the transaction's block, which holds once every output of
    /// the transaction to the address is spent deep in the chain.
    /// </summary>
    /// <param name="transaction">The transaction whose outputs are asked about.</param>
    /// <param name="address">The address.</param>
    /// <returns>The query.</returns>
    private LookupQuery<BitcoinAddressPage> SpendsQuery(BitcoinTransaction transaction, BitcoinAddress address)
    {
        var from = transaction.Block is { } block
            ? "&from=" + block.Height.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        return new LookupQuery<BitcoinAddressPage>(
            AddressSpendsKind,
            $"{address.Value}/{transaction.Txid.Value}",
            $"api/v2/address/{Uri.EscapeDataString(address.Value)}?details=txs&pageSize={_options.PageSize}{from}",
            body =>
            {
                var (transactions, more) = BlockbookParser.ParseAddressPage(body, address);
                return new BitcoinAddressPage(address, null, transactions, more ? transactions[^1].Txid : null);
            },
            page => IndexOf(page.Transactions),
            page => SettledAt(transaction, address, page));
    }

    /// <summary>
    /// Finds who spends each of a transaction's outputs to an address, in the address's history from its block.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="address">The address.</param>
    /// <param name="page">The address's history.</param>
    /// <param name="spends">The spends by output, filled in for the address's outputs.</param>
    /// <returns>
    /// <see langword="false"/> when an output is not spent in the history and the history goes on beyond the page,
    /// so that whether it is spent cannot be said.
    /// </returns>
    private static bool FindSpenders(
        BitcoinTransaction transaction, BitcoinAddress address, BitcoinAddressPage page, BitcoinSpend?[] spends)
    {
        foreach (var output in transaction.Outputs.Where(output => output.Address == address))
        {
            spends[output.Index] = SpenderOf(transaction.Txid, output.Index, page);
            if (spends[output.Index] is null && page.NextAfter is not null)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Finds the transaction in a page that spends an output.</summary>
    /// <param name="txid">The output's transaction.</param>
    /// <param name="index">The output's index.</param>
    /// <param name="page">The page.</param>
    /// <returns>The spend, or <see langword="null"/> when no transaction in the page spends it.</returns>
    private static BitcoinSpend? SpenderOf(Txid txid, int index, BitcoinAddressPage page)
    {
        foreach (var spender in page.Transactions)
        {
            var input = spender.Inputs.FirstOrDefault(input => input.PreviousTxid == txid && input.PreviousIndex == index);
            if (input is not null)
            {
                return new BitcoinSpend(spender.Txid, input.Index, spender.Block);
            }
        }

        return null;
    }

    /// <summary>
    /// Gives the height of the shallowest spend of a transaction's outputs to an address, when all are spent in a
    /// block; an unspent output can still be spent, so the answer can change.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="address">The address.</param>
    /// <param name="page">The address's history.</param>
    /// <returns>The height, or <see langword="null"/>.</returns>
    private static long? SettledAt(BitcoinTransaction transaction, BitcoinAddress address, BitcoinAddressPage page)
    {
        long highest = -1;
        foreach (var output in transaction.Outputs.Where(output => output.Address == address))
        {
            if (SpenderOf(transaction.Txid, output.Index, page)?.Block is not { } block)
            {
                return null;
            }

            highest = Math.Max(highest, block.Height);
        }

        return highest < 0 ? null : highest;
    }

    /// <summary>The lookup of the chain's height.</summary>
    /// <returns>The query.</returns>
    private static LookupQuery<BitcoinTip> TipQuery() => new(
        ResponseKinds.TipHeight,
        string.Empty,
        "api/",
        body => BlockbookParser.ParseTip(body),
        tip => new ResponseIndex([], tip.Height),
        _ => null);

    /// <summary>Asks for the chain's height when the newest known one is old.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>A task that completes when the height is known.</returns>
    private async Task RefreshHeightAsync(ICase @case, CancellationToken cancellationToken)
    {
        var tip = TipQuery();
        var (stored, _) = await _lookups.ReadCaseAsync(@case, tip, cancellationToken).ConfigureAwait(false);
        if (stored is { } known && _clock.UtcNow - known.Response.Origin.ReceivedUtc < TipFreshness)
        {
            return;
        }

        await _lookups.FetchAsync(@case, tip, stored, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Gives the case's index of some transactions: each with its block.</summary>
    /// <param name="transactions">The transactions.</param>
    /// <returns>The index.</returns>
    private static ResponseIndex IndexOf(IEnumerable<BitcoinTransaction> transactions) =>
        new([.. transactions.Select(transaction => (transaction.Txid, transaction.Block?.Height,
            transaction.Block?.Hash.Value))], null);

    /// <summary>Gives the weaker of two answers' statuses, the one that says the least.</summary>
    /// <param name="first">The first.</param>
    /// <param name="second">The second.</param>
    /// <returns>Fetched when either was fetched; otherwise the one that may be outdated.</returns>
    private static LookupStatus Worse(LookupStatus first, LookupStatus second) => (first, second) switch
    {
        (LookupStatus.FromCaseMayBeOutdated, _) or (_, LookupStatus.FromCaseMayBeOutdated) =>
            LookupStatus.FromCaseMayBeOutdated,
        (LookupStatus.Fetched, _) or (_, LookupStatus.Fetched) => LookupStatus.Fetched,
        _ => first,
    };

    /// <summary>Joins two texts for the user, either of which may be missing.</summary>
    /// <param name="first">The first text.</param>
    /// <param name="second">The second text.</param>
    /// <returns>Both, one, or <see langword="null"/>.</returns>
    private static string? Join(string? first, string? second) =>
        first is null || second is null ? first ?? second : $"{first} {second}";
}
