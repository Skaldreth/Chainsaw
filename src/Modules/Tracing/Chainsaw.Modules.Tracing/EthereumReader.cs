using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Reads Ethereum for the graph through <see cref="IEthereumSource"/>: an address's three histories page by page
/// (its transactions, internal transactions and token transfers), a transaction with every movement in it, and the
/// hash of each block a movement lies in. Expanding asks the source; building the graph reads only the case
/// (<see cref="LookupMode.CaseOnly"/>), so showing the graph again never sends anything (principles 4 and 6).
/// </summary>
/// <param name="source">Ethereum's facts.</param>
internal sealed class EthereumReader(IEthereumSource source)
{
    /// <summary>The most pages of one history one read goes through, so that a source that never ends is left.</summary>
    private const int MostPages = 50;

    /// <summary>The most blocks whose hash one read asks for, so that a long history does not flood the source.</summary>
    private const int MostBlockLookups = 100;

    /// <summary>The three histories, in the order they are read.</summary>
    private static readonly EthereumHistory[] Histories =
        [EthereumHistory.Transactions, EthereumHistory.Internal, EthereumHistory.Tokens];

    /// <summary>
    /// Brings in an address's history, newest first, up to the limit of transactions over its three histories. What
    /// is held back, cut short, not counted or not read yet is said (principles 7 and 9).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <param name="maximum">The most transactions.</param>
    /// <param name="earlier">
    /// The lowest block of what earlier expansions of the address brought in, or <see langword="null"/>; every history
    /// is read down past it again, so that the case's pages reach those movements from the newest page.
    /// </param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The transactions it brought in, newest first, and what went wrong.</returns>
    public async Task<EthereumStep> ExpandAddressAsync(
        ICase @case,
        EthereumAddress address,
        int maximum,
        long? earlier,
        CancellationToken cancellationToken)
    {
        var walk = await WalkAsync(@case, address, LookupMode.AsNeeded, maximum, earlier, cancellationToken)
            .ConfigureAwait(false);
        if (!walk.Answered)
        {
            return new EthereumStep([], walk.Problem, false);
        }

        var newest = Newest(walk.Transfers, walk.Fees);
        var taken = newest.Take(maximum).ToList();
        string? problem = walk.Problem;
        ExpansionHoldBack? heldBack = null;
        if (newest.Count > maximum || walk.MoreThanRead)
        {
            heldBack = new ExpansionHoldBack(taken.Count, null);
            problem = Join(problem, string.Format(
                CultureInfo.InvariantCulture,
                "Adressen har fler än {0} tx; de nyaste {0} togs med och resten hölls tillbaka.",
                taken.Count));
        }

        var kept = taken.ToHashSet();
        var blocks = walk.Transfers.Where(transfer => kept.Contains(transfer.Transaction))
            .Select(transfer => transfer.Block);
        var missing = await FetchBlocksAsync(@case, blocks, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        return new EthereumStep(taken, Join(problem, missing.Problem), true, heldBack) { Notes = walk.Notes };
    }

    /// <summary>Brings in a transaction with every movement in it, and its block's hash.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="hash">The transaction.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The transaction, and what went wrong.</returns>
    public async Task<EthereumStep> ExpandTransactionAsync(
        ICase @case, EthereumTxHash hash, CancellationToken cancellationToken)
    {
        var lookup = await source.GetTransactionAsync(@case, hash, LookupMode.AsNeeded, cancellationToken)
            .ConfigureAwait(false);
        if (lookup.Value is null)
        {
            return new EthereumStep([], Lookups.Problem(lookup.Status, lookup.Problem), false);
        }

        var notes = lookup.Notes.Aggregate((string?)null, Join);
        if (!lookup.Value.IsComplete)
        {
            notes = Join(notes, "Tx:en kunde inte läsas helt; en rörelse kan saknas.");
        }

        return new EthereumStep([hash], Lookups.Outdated(lookup), true) { Notes = notes };
    }

    /// <summary>
    /// Reads every Ethereum fact the graph's state holds, from the case only: the whole transactions, the histories of
    /// the expanded addresses limited to the transactions their expansions brought in, and the blocks' hashes.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="state">The graph's state.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The account data.</returns>
    public async Task<AccountData> ReadAsync(ICase @case, GraphState state, CancellationToken cancellationToken)
    {
        var problems = new List<string>();
        var whole = new Dictionary<EthereumTxHash, EthereumTransaction>();
        foreach (var hash in state.EthereumTransactions)
        {
            var lookup = await source.GetTransactionAsync(@case, hash, LookupMode.CaseOnly, cancellationToken)
                .ConfigureAwait(false);
            if (lookup.Value is null)
            {
                problems.Add($"Tx {hash}: {Lookups.Problem(lookup.Status, lookup.Problem)}");
                continue;
            }

            whole[hash] = lookup.Value;
        }

        var transfers = new List<EthereumTransfer>();
        var fees = new List<EthereumFee>();
        var read = new HashSet<ChainAddress>();
        var partly = new HashSet<ChainAddress>();
        var outdated = new HashSet<ChainAddress>();
        foreach (var expansion in state.EthereumHistories)
        {
            var address = expansion.EthereumAddress!;
            var wanted = expansion.EthereumTransactions.ToHashSet();
            var walk = await WalkAsync(@case, address, LookupMode.CaseOnly, null, null, cancellationToken)
                .ConfigureAwait(false);
            if (!walk.Answered)
            {
                problems.Add($"Adressen {address}: {walk.Problem}");
                continue;
            }

            // Only an expansion that brought in all it found carries money on; one held back at the limit or left
            // incomplete has a gap in its ledger, so the money stops there instead of being counted through it.
            (expansion.IsComplete ? read : partly).Add(address.ToChainAddress());
            if (walk.Outdated)
            {
                outdated.Add(address.ToChainAddress());
            }

            transfers.AddRange(walk.Transfers.Where(transfer => wanted.Contains(transfer.Transaction)));
            fees.AddRange(walk.Fees.Where(fee => wanted.Contains(fee.Transaction)));
            var seen = walk.Transfers.Select(transfer => transfer.Transaction)
                .Concat(walk.Fees.Select(fee => fee.Transaction)).ToHashSet();
            var lost = wanted.Count(hash => !seen.Contains(hash));
            if (lost > 0)
            {
                problems.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Adressen {0}: {1} tx som expansionen tog med finns inte i ärendets sidor; expandera igen.",
                    address,
                    lost));
            }
        }

        var data = AccountData.Combine(whole, transfers, fees, read, outdated);
        var blocks = await FetchBlocksAsync(
            @case, data.Transfers.Select(transfer => transfer.Block), LookupMode.CaseOnly, cancellationToken)
            .ConfigureAwait(false);
        return data with { BlockHashes = blocks.Hashes, Problems = problems, PartlyRead = partly };
    }

    /// <summary>
    /// Gives the transactions of some movements and fees, newest first: pending ones first, then by block, and in one
    /// block by hash.
    /// </summary>
    /// <param name="transfers">The movements.</param>
    /// <param name="fees">The fees.</param>
    /// <returns>The transactions, each once.</returns>
    private static List<EthereumTxHash> Newest(
        IReadOnlyList<EthereumTransfer> transfers, IReadOnlyList<EthereumFee> fees) =>
    [
        .. transfers.Select(transfer => (transfer.Transaction, transfer.Block))
            .Concat(fees.Select(fee => (fee.Transaction, fee.Block)))
            .GroupBy(pair => pair.Transaction)
            .Select(group => (Hash: group.Key, Height: group.Max(pair => pair.Block?.Height ?? long.MaxValue)))
            .OrderByDescending(pair => pair.Height)
            .ThenBy(pair => pair.Hash.Value, StringComparer.Ordinal)
            .Select(pair => pair.Hash),
    ];

    /// <summary>
    /// Goes through an address's three histories, page by page from the newest. On expansion it stops a history once
    /// it holds more than the limit of transactions and has gone below the earlier expansions' lowest block (each history
    /// for itself); when reading the case it goes on until the case has no further page, so that no movement of a wanted
    /// transaction on a later page is lost.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="maximum">The limit on expansion, or <see langword="null"/> when reading.</param>
    /// <param name="floor">On expansion, the lowest block earlier expansions brought in, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The movements and fees, each once, and what was said.</returns>
    private async Task<HistoryWalk> WalkAsync(
        ICase @case,
        EthereumAddress address,
        LookupMode mode,
        int? maximum,
        long? floor,
        CancellationToken cancellationToken)
    {
        var walk = new HistoryWalk();
        foreach (var history in Histories)
        {
            var hashes = new HashSet<EthereumTxHash>();
            long? atOrBelow = null;
            for (var pages = 0; pages < MostPages; pages++)
            {
                var lookup = await source.GetAddressHistoryAsync(
                    @case, address, history, atOrBelow, mode, cancellationToken).ConfigureAwait(false);
                if (lookup.Value is not { } page)
                {
                    walk.Fail(Lookups.Problem(lookup.Status, lookup.Problem), atOrBelow is null);
                    break;
                }

                walk.Answered = true;
                walk.Outdated |= lookup.Status == LookupStatus.FromCaseMayBeOutdated;
                walk.Problem = Join(walk.Problem, Lookups.Outdated(lookup));
                var before = hashes.Count;
                walk.Add(page);
                hashes.UnionWith(page.Transfers.Select(transfer => transfer.Transaction));
                hashes.UnionWith(page.Fees.Select(fee => fee.Transaction));
                if (page.NextAtOrBelow is not { } next || next == atOrBelow || hashes.Count == before)
                {
                    break;
                }

                // An expansion goes on past its limit until it has also read the pages that hold what earlier
                // expansions brought in, so that the case's pages reach them from the newest page again.
                if (maximum is { } limit && hashes.Count > limit && (floor is not { } lowest || next < lowest))
                {
                    walk.MoreThanRead = true;
                    break;
                }

                atOrBelow = next;
            }
        }

        return walk;
    }

    /// <summary>
    /// Finds the hash of every block a movement lies in whose answer did not give it (an internal transaction's), by
    /// asking for the block, so that every step can be checked against the chain (principle 1).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="blocks">The movements' blocks.</param>
    /// <param name="mode">Whether the source may be asked.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>The hashes by height, and what could not be found.</returns>
    private async Task<(IReadOnlyDictionary<long, string> Hashes, string? Problem)> FetchBlocksAsync(
        ICase @case, IEnumerable<EthereumBlockReference?> blocks, LookupMode mode, CancellationToken cancellationToken)
    {
        var known = blocks.OfType<EthereumBlockReference>().ToList();
        var hashes = known.Where(block => block.Hash is not null)
            .GroupBy(block => block.Height)
            .ToDictionary(group => group.Key, group => group.First().Hash!);
        var missing = known.Select(block => block.Height).Where(height => !hashes.ContainsKey(height)).Distinct()
            .Order().ToList();
        var unknown = 0;
        var most = mode == LookupMode.CaseOnly ? missing.Count : MostBlockLookups;
        foreach (var height in missing.Take(most))
        {
            var lookup = await source.GetBlockAsync(@case, height, mode, cancellationToken).ConfigureAwait(false);
            if (lookup.Value?.Hash is { } hash)
            {
                hashes[height] = hash;
            }
            else
            {
                unknown++;
            }
        }

        unknown += Math.Max(0, missing.Count - most);
        var problem = unknown == 0
            ? null
            : string.Format(
                CultureInfo.InvariantCulture,
                "Hashen saknas för {0} block; deras rörelser ritas streckade och utan hash tills blocken har hämtats.",
                unknown);
        return (hashes, problem);
    }

    /// <summary>Joins two texts for the user, either of which may be missing, each once.</summary>
    /// <param name="first">The first text.</param>
    /// <param name="second">The second text.</param>
    /// <returns>Both, one, or <see langword="null"/>.</returns>
    private static string? Join(string? first, string? second) => Lookups.Join(first, second);

    /// <summary>What a walk through an address's histories found.</summary>
    private sealed class HistoryWalk
    {
        /// <summary>The movements seen, by their identity, so that the entries a page repeats count once.</summary>
        private readonly Dictionary<TransferKey, EthereumTransfer> _transfers = [];

        /// <summary>The fees seen, by transaction.</summary>
        private readonly Dictionary<EthereumTxHash, EthereumFee> _fees = [];

        /// <summary>The transactions seen.</summary>
        private readonly HashSet<EthereumTxHash> _seen = [];

        /// <summary>Gets or sets whether the source or the case answered at all.</summary>
        public bool Answered { get; set; }

        /// <summary>Gets or sets whether a page came from the case and may have changed.</summary>
        public bool Outdated { get; set; }

        /// <summary>Gets or sets whether a history was left with more in it than was read.</summary>
        public bool MoreThanRead { get; set; }

        /// <summary>Gets or sets what makes the read incomplete, or <see langword="null"/>.</summary>
        public string? Problem { get; set; }

        /// <summary>Gets what the user should know that does not make the read incomplete.</summary>
        public string? Notes { get; private set; }

        /// <summary>Gets the movements, each once.</summary>
        public IReadOnlyList<EthereumTransfer> Transfers => [.. _transfers.Values];

        /// <summary>Gets the fees, each once.</summary>
        public IReadOnlyList<EthereumFee> Fees => [.. _fees.Values];

        /// <summary>Takes in a page, and says what it could not count.</summary>
        /// <param name="page">The page.</param>
        public void Add(EthereumAddressPage page)
        {
            foreach (var transfer in page.Transfers)
            {
                _transfers.TryAdd(TransferKey.Of(transfer), transfer);
                _seen.Add(transfer.Transaction);
            }

            foreach (var fee in page.Fees)
            {
                _fees.TryAdd(fee.Transaction, fee);
                _seen.Add(fee.Transaction);
            }

            if (page.IsCutShort)
            {
                Problem = Lookups.Join(Problem,
                    "En sida av historiken låg i ett enda block; det som är äldre kunde inte läsas.");
            }

            if (page.IsIncomplete)
            {
                Problem = Lookups.Join(Problem, "Källan har inte läst hela historiken än; expandera igen senare.");
            }

            if (page.Unread > 0)
            {
                Problem = Lookups.Join(Problem,
                    "Källan saknade decimaler för en token; de posterna räknades inte än.");
            }

            if (page.Skipped > 0)
            {
                Notes = Lookups.Join(Notes,
                    "Poster som Chainsaw inte räknar hoppades över (NFT, token med för många decimaler, "
                    + "överföring på noll).");
            }
        }

        /// <summary>Records a lookup that failed.</summary>
        /// <param name="problem">Why.</param>
        /// <param name="first">Whether it was a history's first page.</param>
        public void Fail(string problem, bool first)
        {
            Problem = Lookups.Join(Problem, problem);
            MoreThanRead |= !first;
        }
    }
}

/// <summary>What one Ethereum expansion step brought in.</summary>
/// <param name="Transactions">The transactions, newest first.</param>
/// <param name="Problem">What makes it incomplete, or <see langword="null"/>.</param>
/// <param name="Answered">Whether the source or the case answered at all.</param>
/// <param name="HeldBack">What was held back at the limit, if anything.</param>
internal sealed record EthereumStep(
    IReadOnlyList<EthereumTxHash> Transactions, string? Problem, bool Answered, ExpansionHoldBack? HeldBack = null)
{
    /// <summary>Gets what the user should know that does not make the step incomplete.</summary>
    public string? Notes { get; init; }
}

/// <summary>
/// The identity of a movement in an address's history: the transaction, how the value moved, where in the
/// transaction, and who sent what to whom, so that an entry the next page repeats is counted once.
/// </summary>
/// <param name="Transaction">The transaction.</param>
/// <param name="Kind">How the value moved.</param>
/// <param name="Position">Where in the transaction.</param>
/// <param name="From">The sender.</param>
/// <param name="To">The receiver.</param>
/// <param name="Amount">The amount.</param>
internal sealed record TransferKey(
    EthereumTxHash Transaction, EthereumTransferKind Kind, long Position, EthereumAddress From, EthereumAddress To,
    AssetAmount Amount)
{
    /// <summary>Gives a movement's identity.</summary>
    /// <param name="transfer">The movement.</param>
    /// <returns>The identity.</returns>
    public static TransferKey Of(EthereumTransfer transfer) => new(
        transfer.Transaction, transfer.Kind, transfer.Position, transfer.From, transfer.To, transfer.Amount);
}
