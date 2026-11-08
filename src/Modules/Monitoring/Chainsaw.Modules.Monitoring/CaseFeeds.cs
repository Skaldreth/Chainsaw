using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring;

/// <summary>What the case's feeds say, in Swedish.</summary>
internal static class FeedTexts
{
    /// <summary>When a check stopped before it reached back to where it was.</summary>
    public const string CutShort =
        "{0} hade fler nya tx än en kontroll läser ({1} sidor); tx under block {3} kan ha missats.";

    /// <summary>When the source cannot page further back than a block, or has not read a history yet.</summary>
    public const string Incomplete =
        "Källan gav inte hela historiken för {0} ner till block {1}; resten kan ha missats.";

    /// <summary>When an address on a watch is not one the chain knows.</summary>
    public const string NotAnAddress = "{0} är ingen adress på kedjan.";

    /// <summary>When a block's hash could not be had, so that its transactions wait for the next check.</summary>
    public const string NoBlockHash =
        "Blockets hash för block {0} kunde inte hämtas; träffen tas med nästa kontroll.";

    /// <summary>
    /// Gives the status of a lookup as a feed reports it: read when it was answered, failed when it failed (also when
    /// the case's older answer stood in, and when no endpoint was in step: the problem says which), and not asked when
    /// the source said it was offline or answered only from the case.
    /// </summary>
    /// <typeparam name="T">What was looked up.</typeparam>
    /// <param name="lookup">The lookup.</param>
    /// <returns>The status.</returns>
    public static WatchFeedStatus StatusOf<T>(BitcoinLookup<T> lookup)
        where T : class => lookup.Status switch
    {
        LookupStatus.Fetched or LookupStatus.FromCase or LookupStatus.NotFound => WatchFeedStatus.Read,
        // Failed, with or without a sealed answer (a timeout seals nothing), and failed with the case's older answer
        // standing in, are both failed lookups; the lookup's problem says why, also when no endpoint was in step.
        LookupStatus.Failed or LookupStatus.FromCaseMayBeOutdated => WatchFeedStatus.Failed,
        _ => WatchFeedStatus.NotAsked,
    };

    /// <summary>Formats a text.</summary>
    /// <param name="text">The text.</param>
    /// <param name="values">Its values.</param>
    /// <returns>The formatted text.</returns>
    public static string Format(string text, params object[] values) =>
        string.Format(CultureInfo.InvariantCulture, text, values);
}

/// <summary>
/// Reads a Bitcoin address's newest transactions for a check through <see cref="IBitcoinSource"/> with
/// <see cref="LookupMode.InStep"/>: only an endpoint in step with the chain answers, and every response is sealed in
/// the case.
/// </summary>
/// <param name="source">Bitcoin's source.</param>
/// <param name="case">The open case.</param>
internal sealed class BitcoinCaseFeed(IBitcoinSource source, ICase @case) : IWatchFeed
{
    /// <inheritdoc/>
    public async Task<WatchFeedPage> ReadAsync(
        ChainAddress address, Asset? asset, long downTo, CancellationToken cancellationToken = default)
    {
        if (!BitcoinAddress.TryParse(address.Value, out var bitcoin))
        {
            return new WatchFeedPage(
                WatchFeedStatus.Failed, [], [], false, FeedTexts.Format(FeedTexts.NotAnAddress, address.Value));
        }

        var transactions = new List<WatchTransaction>();
        var origins = new List<long>();
        Txid? after = null;
        for (var page = 0; page < WatchRules.MaximumPages; page++)
        {
            var lookup = await source.GetAddressTransactionsAsync(
                @case, bitcoin, after, LookupMode.InStep, cancellationToken).ConfigureAwait(false);
            var status = FeedTexts.StatusOf(lookup);

            // Only an answer the check rests on is cited; a failed or stood-in response is not (principle 1).
            if (status == WatchFeedStatus.Read)
            {
                origins.AddRange(lookup.Origins.Select(origin => origin.Sequence));
            }

            if (status != WatchFeedStatus.Read || lookup.Value is not { } read)
            {
                return new WatchFeedPage(
                    status, transactions, origins, false, status == WatchFeedStatus.Read ? null : lookup.Problem);
            }

            transactions.AddRange(read.Transactions.Select(Of));
            var oldest = read.Transactions.Where(tx => tx.Block is not null).Select(tx => tx.Block!.Height)
                .DefaultIfEmpty(long.MinValue).Min();
            if (read.NextAfter is null || oldest <= downTo)
            {
                return new WatchFeedPage(WatchFeedStatus.Read, transactions, origins, false, null);
            }

            after = read.NextAfter;
        }

        // Everything above the oldest block read was read; the blocks between it and where the check was are not, and
        // the check moves on so that it never stays blind (the gap is said, and kept with the check).
        var lowest = transactions.Where(tx => tx.Block is not null).Select(tx => tx.Block!.Height)
            .DefaultIfEmpty(downTo).Min();
        return new WatchFeedPage(WatchFeedStatus.Partial, transactions, origins, true,
            FeedTexts.Format(FeedTexts.CutShort, address.Value, WatchRules.MaximumPages, downTo + 1, lowest))
        {
            CompleteAbove = lowest,
        };
    }

    /// <summary>Gives a Bitcoin transaction in the check's form: one flow from its inputs to its outputs.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The check's form.</returns>
    public static WatchTransaction Of(BitcoinTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var from = transaction.Inputs.Select(input => new WatchLeg(
            input.Address is { } address ? new ChainAddress(Chain.Bitcoin, address.Value) : null,
            AssetAmount.Of(input.Amount ?? Satoshi.Zero)));
        var to = transaction.Outputs.Select(output => new WatchLeg(
            output.Address is { } address ? new ChainAddress(Chain.Bitcoin, address.Value) : null,
            AssetAmount.Of(output.Amount)));
        var block = transaction.Block is { } at ? new WatchBlock(at.Height, at.Hash.Value, at.Time) : null;
        return new WatchTransaction(Chain.Bitcoin, transaction.Txid.Value, block, [new WatchFlow([.. from], [.. to])]);
    }
}

/// <summary>
/// Reads an account chain's address's newest movements for a check (Ethereum's or TRON's) through its
/// <see cref="IAccountSource"/> with <see cref="LookupMode.InStep"/>: the histories the watched asset needs (the coin in
/// the transactions and the internal ones, tokens in the token transfers), each sealed in the case; a block whose hash
/// a history lacks is looked up.
/// </summary>
/// <param name="source">The chain's source.</param>
/// <param name="case">The open case.</param>
internal sealed class EthereumCaseFeed(IAccountSource source, ICase @case) : IWatchFeed
{
    /// <inheritdoc/>
    public async Task<WatchFeedPage> ReadAsync(
        ChainAddress address, Asset? asset, long downTo, CancellationToken cancellationToken = default)
    {
        if (address.Chain != source.Chain || !EthereumAddress.TryParse(source.Chain, address.Value, out var ethereum))
        {
            return new WatchFeedPage(
                WatchFeedStatus.Failed, [], [], false, FeedTexts.Format(FeedTexts.NotAnAddress, address.Value));
        }

        var transfers = new Dictionary<(string, EthereumTransferKind, long), EthereumTransfer>();
        var origins = new List<long>();
        var problems = new List<string>();
        var status = WatchFeedStatus.Read;
        long? completeAbove = null;
        var unknown = false;
        foreach (var history in HistoriesFor(asset))
        {
            var (read, above) = await ReadHistoryAsync(ethereum, history, downTo, transfers, origins, problems,
                cancellationToken).ConfigureAwait(false);
            status = (WatchFeedStatus)Math.Max((int)status, (int)read);
            if (above is { } height)
            {
                completeAbove = Math.Max(completeAbove ?? height, height);
            }

            // A partial history with no block it is complete above keeps the whole check where it was.
            unknown |= read == WatchFeedStatus.Partial && above is null;
        }

        var hashes = await BlockHashesAsync(transfers.Values, downTo, origins, problems, cancellationToken)
            .ConfigureAwait(false);

        // A block whose hash could not be had is read again: the check moves on no further than the lowest of them, so
        // that the next check reads back to every one.
        var hashless = transfers.Values.Select(transfer => transfer.Block).OfType<EthereumBlockReference>()
            .Where(block => block.Height > downTo && !hashes.ContainsKey(block.Height))
            .Select(block => (long?)block.Height)
            .Min();
        if (hashless is { } missing)
        {
            completeAbove = Math.Min(completeAbove ?? missing, missing - 1);
        }

        if (completeAbove is not null && status == WatchFeedStatus.Read)
        {
            status = WatchFeedStatus.Partial;
        }

        if (unknown)
        {
            completeAbove = null;
        }

        var transactions = transfers.Values
            .GroupBy(transfer => transfer.Transaction.Value, StringComparer.Ordinal)
            .Select(group => Of(
                source.Chain, group.Key, [.. group.OrderBy(t => t.Kind).ThenBy(t => t.Position)], hashes))
            .ToList();
        return new WatchFeedPage(
            status, transactions, origins, completeAbove is not null,
            problems.Count == 0 ? null : string.Join(' ', problems.Distinct()))
        {
            CompleteAbove = completeAbove,
        };
    }

    /// <summary>Gives the histories that hold the watched asset.</summary>
    /// <param name="asset">The asset, or <see langword="null"/> for all.</param>
    /// <returns>The histories.</returns>
    private static IReadOnlyList<EthereumHistory> HistoriesFor(Asset? asset) => asset switch
    {
        null => [EthereumHistory.Transactions, EthereumHistory.Internal, EthereumHistory.Tokens],
        { Kind: AssetKind.Native } => [EthereumHistory.Transactions, EthereumHistory.Internal],
        _ => [EthereumHistory.Tokens],
    };

    /// <summary>
    /// Reads one history back to a block, page by page. Tokens the source has no decimals for are not counted and do
    /// not stop the paging.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="history">The history.</param>
    /// <param name="downTo">The block below which nothing is needed.</param>
    /// <param name="transfers">The movements so far, each once.</param>
    /// <param name="origins">The sealed responses so far.</param>
    /// <param name="problems">What went wrong so far.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <returns>
    /// How it went, and for a partial read the block above which it is complete (<see langword="null"/> when it
    /// reached back to <paramref name="downTo"/>).
    /// </returns>
    private async Task<(WatchFeedStatus Status, long? CompleteAbove)> ReadHistoryAsync(
        EthereumAddress address,
        EthereumHistory history,
        long downTo,
        Dictionary<(string, EthereumTransferKind, long), EthereumTransfer> transfers,
        List<long> origins,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        long? atOrBelow = null;
        var lowest = long.MaxValue;
        for (var page = 0; page < WatchRules.MaximumPages; page++)
        {
            var lookup = await source.GetAddressHistoryAsync(
                @case, address, history, atOrBelow, LookupMode.InStep, cancellationToken).ConfigureAwait(false);
            var status = FeedTexts.StatusOf(lookup);

            // Only an answer the check rests on is cited; a failed or stood-in response is not (principle 1).
            if (status == WatchFeedStatus.Read)
            {
                origins.AddRange(lookup.Origins.Select(origin => origin.Sequence));
            }

            if (status != WatchFeedStatus.Read || lookup.Value is not { } read)
            {
                if (lookup.Problem is { } problem && status != WatchFeedStatus.Read)
                {
                    problems.Add(problem);
                }

                return (status, null);
            }

            foreach (var transfer in read.Transfers)
            {
                transfers.TryAdd((transfer.Transaction.Value, transfer.Kind, transfer.Position), transfer);
                lowest = Math.Min(lowest, transfer.Block?.Height ?? lowest);
            }

            if (read.IsIncomplete)
            {
                // The source has not finished reading the history: nothing below its newest page is trusted yet.
                problems.Add(FeedTexts.Format(FeedTexts.Incomplete, address.Value, downTo + 1));
                return (WatchFeedStatus.Partial, lowest == long.MaxValue ? null : lowest);
            }

            if (read.IsCutShort)
            {
                // A full page in one block: the rest of that block and everything older cannot be paged to.
                problems.Add(FeedTexts.Format(FeedTexts.Incomplete, address.Value, downTo + 1));
                return (WatchFeedStatus.Partial, read.Transfers.Select(t => t.Block?.Height).Max() ?? lowest);
            }

            if (read.NextAtOrBelow is not { } next || ReachedBelow(read, next, downTo))
            {
                return (WatchFeedStatus.Read, null);
            }

            atOrBelow = next;
        }

        var reached = lowest == long.MaxValue ? downTo : lowest;
        problems.Add(FeedTexts.Format(FeedTexts.CutShort, address.Value, WatchRules.MaximumPages, downTo + 1, reached));
        return (WatchFeedStatus.Partial, lowest == long.MaxValue ? null : lowest);
    }

    /// <summary>
    /// Says whether a page has gone back to the block the check reads to. Ethereum's next page starts at a block, so
    /// its start says it; TRON's starts at a time, so the oldest block among the page's movements and fees says it (a
    /// page without one pages on, as far as <see cref="WatchRules.MaximumPages"/> reach).
    /// </summary>
    /// <param name="read">The page.</param>
    /// <param name="next">Where the next page starts, in the source's unit.</param>
    /// <param name="downTo">The block the check reads to.</param>
    /// <returns><see langword="true"/> when nothing older is needed.</returns>
    private bool ReachedBelow(EthereumAddressPage read, long next, long downTo)
    {
        if (source.Chain == Chain.Ethereum)
        {
            return next <= downTo;
        }

        var oldest = read.Transfers.Select(transfer => transfer.Block?.Height)
            .Concat(read.Fees.Select(fee => fee.Block?.Height))
            .Min();
        return oldest is { } height && height <= downTo;
    }

    /// <summary>Looks up the hash of every block a movement lacks it for, in step with the chain.</summary>
    /// <param name="transfers">The movements.</param>
    /// <param name="downTo">The block at or below which nothing counts, so that it is never looked up.</param>
    /// <param name="origins">The sealed responses so far.</param>
    /// <param name="problems">What went wrong so far.</param>
    /// <param name="cancellationToken">Stops the lookups.</param>
    /// <returns>The hashes found, per height.</returns>
    private async Task<IReadOnlyDictionary<long, string>> BlockHashesAsync(
        IEnumerable<EthereumTransfer> transfers,
        long downTo,
        List<long> origins,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        var hashes = new Dictionary<long, string>();
        foreach (var block in transfers.Select(transfer => transfer.Block).OfType<EthereumBlockReference>())
        {
            if (block.Hash is { } known)
            {
                hashes[block.Height] = known;
            }
        }

        var missing = transfers.Select(transfer => transfer.Block).OfType<EthereumBlockReference>()
            .Where(block => block.Hash is null && block.Height > downTo && !hashes.ContainsKey(block.Height))
            .Select(block => block.Height)
            .Distinct()
            .ToList();
        foreach (var height in missing)
        {
            var lookup = await source.GetBlockAsync(@case, height, LookupMode.InStep, cancellationToken)
                .ConfigureAwait(false);
            if (lookup.Value?.Hash is { } hash)
            {
                origins.AddRange(lookup.Origins.Select(origin => origin.Sequence));
                hashes[height] = hash;
            }
            else
            {
                problems.Add(FeedTexts.Format(FeedTexts.NoBlockHash, height));
            }
        }

        return hashes;
    }

    /// <summary>
    /// Gives an account chain's transaction's movements in the check's form: one flow per movement. A block whose hash
    /// is not known is left out, so that the transaction waits for a check that knows it (a hit is sealed with its
    /// hash).
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="hash">The transaction's hash.</param>
    /// <param name="transfers">Its movements.</param>
    /// <param name="hashes">The blocks' hashes.</param>
    /// <returns>The check's form.</returns>
    private static WatchTransaction Of(
        Chain chain, string hash, IReadOnlyList<EthereumTransfer> transfers, IReadOnlyDictionary<long, string> hashes)
    {
        var at = transfers.Select(transfer => transfer.Block).OfType<EthereumBlockReference>().FirstOrDefault();
        var block = at is not null && hashes.TryGetValue(at.Height, out var blockHash)
            ? new WatchBlock(at.Height, blockHash, at.Time)
            : null;
        var flows = transfers.Select(transfer => new WatchFlow(
            [new WatchLeg(transfer.From.ToChainAddress(), transfer.Amount)],
            [new WatchLeg(transfer.To.ToChainAddress(), transfer.Amount)]));
        return new WatchTransaction(chain, hash, block, [.. flows]);

    }
}
