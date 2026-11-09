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
    /// Blockbook pages by number, newest first, and the numbers move when new transactions come; so the page after a
    /// transaction is asked as the history below the transaction's block (<c>to</c>). For that to give every
    /// transaction once, a page with more after it never ends inside a block: the transactions of its last block are
    /// left for the next page (<see cref="EndAtWholeBlock"/>). The block of the transaction a page is asked after is
    /// read from Blockbook's own answer about it, sealed as any other.
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

        var to = string.Empty;
        if (after is not null)
        {
            var known = await _lookups.LookupAsync(@case, TransactionQuery(after.On(Chain.Zcash)), mode, cancellationToken)
                .ConfigureAwait(false);
            if (known.Value?.Block is not { } block)
            {
                return new BitcoinLookup<BitcoinAddressPage>(
                    known.Value is null ? known.Status : LookupStatus.Failed, null, known.Origins,
                    known.Problem ?? AfterUnconfirmedText);
            }

            to = "&to=" + (block.Height - 1).ToString(CultureInfo.InvariantCulture);
        }

        var query = new LookupQuery<BitcoinAddressPage>(
            ResponseKinds.AddressTransactions,
            after is null ? address.Value : $"{address.Value}/{after.Value}",
            $"api/v2/address/{Uri.EscapeDataString(address.Value)}?details=txs&pageSize={_options.PageSize}{to}",
            body =>
            {
                var (transactions, page, pages) = BlockbookParser.ParseAddressPage(body, address);
                return EndAtWholeBlock(address, after, transactions, page < pages);
            },
            page => IndexOf(page.Transactions),
            _ => null);
        return await _lookups.LookupAsync(@case, query, mode, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>What a page says when it is asked after a transaction that is not in a block.</summary>
    public const string AfterUnconfirmedText =
        "Sidan efter en obekräftad tx kan inte frågas: Blockbook räknar sidorna efter block. Inget skickades.";

    /// <summary>
    /// Makes a page that never ends inside a block when more follow: the transactions of the page's oldest block are
    /// left for the next page, which is asked below the last block kept, so that each comes exactly once. A page that
    /// is one block only (more transactions of the address in a block than a page holds) is kept whole and the rest of
    /// that block is passed over; a page of unconfirmed transactions only has no next page.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="after">The transaction the page was asked after, or <see langword="null"/>.</param>
    /// <param name="transactions">The page's transactions, newest first.</param>
    /// <param name="more">Whether Blockbook has more pages.</param>
    /// <returns>The page.</returns>
    internal static BitcoinAddressPage EndAtWholeBlock(
        BitcoinAddress address, Txid? after, List<BitcoinTransaction> transactions, bool more)
    {
        if (!more || transactions.Count == 0)
        {
            return new BitcoinAddressPage(address, after, transactions, null);
        }

        var oldest = transactions[^1].Block?.Height;
        var kept = oldest is { } height
            ? transactions.Where(transaction => transaction.Block?.Height != height).ToList()
            : transactions;
        if (kept.Count == 0)
        {
            kept = transactions;
        }

        var next = kept[^1].Block is null ? null : kept[^1].Txid;
        return new BitcoinAddressPage(address, after, kept, next);
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

        // zcashd's answer is asked in the same mode and must name the same block and spend the same outputs
        // (BlockbookParser.WithShielded); a stored answer that does not agree is not used.
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
    /// <remarks>
    /// Who spends an output is read from its address's history from the transaction's block, newest first: the first
    /// page says how many there are, and the oldest pages, where a spend soon after lies, are read next, up to
    /// <see cref="MostSpendPages"/> pages per address. An output whose spender is not found while pages are left is
    /// said to be unknown (<see cref="BitcoinOutputSpend.IsUnknown"/>), never unspent, and the others keep their
    /// answers.
    /// </remarks>
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

        var spends = transaction.Outputs.Select(output => new BitcoinOutputSpend(output.Index, null)).ToArray();
        var reading = new SpendReading();
        foreach (var address in transaction.Outputs.Select(output => output.Address).OfType<BitcoinAddress>()
            .Distinct())
        {
            if (!await ReadSpendsAsync(@case, transaction, address, mode, spends, reading, cancellationToken)
                .ConfigureAwait(false))
            {
                return new BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>(
                    reading.Failure!.Status, null, SourceLookups.Ordered(reading.Origins), reading.Failure.Problem);
            }
        }

        var unknown = spends.Count(spend => spend.IsUnknown);
        var problem = unknown == 0 ? reading.Problem : Join(reading.Problem, string.Format(
            CultureInfo.InvariantCulture, UnknownSpendsFormat, unknown, MostSpendPages));
        return new BitcoinLookup<IReadOnlyList<BitcoinOutputSpend>>(
            reading.Status, spends, SourceLookups.Ordered(reading.Origins), problem);
    }

    /// <summary>How many pages of an address's history are read at most to find who spent its outputs.</summary>
    public const int MostSpendPages = 5;

    /// <summary>What is said of outputs whose spender could not be found in the pages read.</summary>
    public const string UnknownSpendsFormat =
        "Vem som spenderade {0} utgång(ar) kunde inte läsas: adressen har fler tx än de {1} sidor som lästes.";

    /// <summary>
    /// Reads who spends a transaction's outputs to one address: the first page of its history from the block, then the
    /// oldest pages until every output is found or <see cref="MostSpendPages"/> are read.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="address">The address.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="spends">The spends by output, filled in for the address's outputs.</param>
    /// <param name="reading">What the readings gave so far.</param>
    /// <param name="cancellationToken">Stops the lookups.</param>
    /// <returns><see langword="false"/> when a page could not be read at all.</returns>
    private async Task<bool> ReadSpendsAsync(
        ICase @case, BitcoinTransaction transaction, BitcoinAddress address, LookupMode mode,
        BitcoinOutputSpend[] spends, SpendReading reading, CancellationToken cancellationToken)
    {
        var outputs = transaction.Outputs.Where(output => output.Address == address).Select(o => o.Index).ToList();
        var pages = 1;
        var read = new List<int>();
        for (var number = 1; ; number = NextPage(pages, read))
        {
            var history = await _lookups.LookupAsync(
                    @case, SpendsQuery(transaction, address, outputs, number), mode, cancellationToken)
                .ConfigureAwait(false);
            reading.Origins.AddRange(history.Origins);
            if (history.Value is not { } page)
            {
                reading.Failure = history;
                return false;
            }

            reading.Status = Worse(reading.Status, history.Status);
            reading.Problem = Join(reading.Problem, history.Problem);
            pages = page.Pages;
            read.Add(number);
            foreach (var index in outputs.Where(index => spends[index].SpentBy is null))
            {
                spends[index] = new BitcoinOutputSpend(index, SpenderOf(transaction.Txid, index, page.Transactions));
            }

            var missing = outputs.Where(index => spends[index].SpentBy is null).ToList();
            var left = pages > read.Count;
            if (missing.Count == 0 || !left || read.Count == MostSpendPages)
            {
                foreach (var index in missing.Where(_ => left))
                {
                    spends[index] = spends[index] with { IsUnknown = true };
                }

                return true;
            }
        }
    }

    /// <summary>Gives the next page to read: the oldest not read yet, since a spend soon after lies there.</summary>
    /// <param name="pages">How many pages there are.</param>
    /// <param name="read">The pages read.</param>
    /// <returns>The page's number.</returns>
    private static int NextPage(int pages, List<int> read) =>
        Enumerable.Range(1, pages).Reverse().First(number => !read.Contains(number));

    /// <summary>What the readings of an address's history gave, across the pages and addresses read.</summary>
    private sealed class SpendReading
    {
        /// <summary>Gets the sealed responses read.</summary>
        public List<ResponseOrigin> Origins { get; } = [];

        /// <summary>Gets or sets the weakest status read.</summary>
        public LookupStatus Status { get; set; } = LookupStatus.FromCase;

        /// <summary>Gets or sets what the readings said.</summary>
        public string? Problem { get; set; }

        /// <summary>Gets or sets the reading that failed, or <see langword="null"/>.</summary>
        public BitcoinLookup<HistoryPage>? Failure { get; set; }
    }

    /// <summary>One page of an address's history from a block.</summary>
    /// <param name="Transactions">The page's transactions.</param>
    /// <param name="Pages">How many pages there are.</param>
    private sealed record HistoryPage(IReadOnlyList<BitcoinTransaction> Transactions, int Pages);

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
        transaction => IndexOf([transaction]),
        transaction => transaction.Block?.Height);

    /// <summary>
    /// The lookup of a page of an output's address's history from the transaction's block, which holds once every output
    /// of the transaction to the address is spent deep in the chain on that page.
    /// </summary>
    /// <param name="transaction">The transaction whose outputs are asked about.</param>
    /// <param name="address">The address.</param>
    /// <param name="outputs">The transaction's outputs to the address.</param>
    /// <param name="number">The page's number, from 1.</param>
    /// <returns>The query.</returns>
    private LookupQuery<HistoryPage> SpendsQuery(
        BitcoinTransaction transaction, BitcoinAddress address, IReadOnlyList<int> outputs, int number)
    {
        var from = transaction.Block is { } block
            ? "&from=" + block.Height.ToString(CultureInfo.InvariantCulture)
            : string.Empty;
        var page = number.ToString(CultureInfo.InvariantCulture);
        return new LookupQuery<HistoryPage>(
            AddressSpendsKind,
            number == 1 ? $"{address.Value}/{transaction.Txid.Value}" : $"{address.Value}/{transaction.Txid.Value}/{page}",
            $"api/v2/address/{Uri.EscapeDataString(address.Value)}?details=txs&pageSize={_options.PageSize}{from}"
                + (number == 1 ? string.Empty : "&page=" + page),
            body =>
            {
                var (transactions, _, pages) = BlockbookParser.ParseAddressPage(body, address);
                return new HistoryPage(transactions, pages);
            },
            history => IndexOf(history.Transactions),
            history => SettledAt(transaction.Txid, outputs, history.Transactions));
    }

    /// <summary>Finds the transaction among some that spends an output.</summary>
    /// <param name="txid">The output's transaction.</param>
    /// <param name="index">The output's index.</param>
    /// <param name="transactions">The transactions.</param>
    /// <returns>The spend, or <see langword="null"/> when none of them spends it.</returns>
    private static BitcoinSpend? SpenderOf(Txid txid, int index, IEnumerable<BitcoinTransaction> transactions)
    {
        foreach (var spender in transactions)
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
    /// Gives the height of the shallowest spend of some outputs on a page, when all are spent there in a block; an
    /// output not spent on the page can still be, so the answer can change.
    /// </summary>
    /// <param name="txid">The outputs' transaction.</param>
    /// <param name="outputs">The outputs' indexes.</param>
    /// <param name="transactions">The page's transactions.</param>
    /// <returns>The height, or <see langword="null"/>.</returns>
    private static long? SettledAt(Txid txid, IReadOnlyList<int> outputs, IReadOnlyList<BitcoinTransaction> transactions)
    {
        long highest = -1;
        foreach (var index in outputs)
        {
            if (SpenderOf(txid, index, transactions)?.Block is not { } block)
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
