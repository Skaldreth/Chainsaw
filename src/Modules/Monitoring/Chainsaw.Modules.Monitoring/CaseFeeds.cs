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
        "{0} har fler nya tx än en kontroll läser ({1} sidor); de äldsta kan saknas tills nästa kontroll.";

    /// <summary>When an address on a watch is not one the chain knows.</summary>
    public const string NotAnAddress = "{0} är ingen adress på kedjan.";

    /// <summary>When a block's hash could not be had, so that its transactions wait for the next check.</summary>
    public const string NoBlockHash =
        "Blockets hash för block {0} kunde inte hämtas; träffen tas med nästa kontroll.";

    /// <summary>Gives the status of a lookup as a feed reports it.</summary>
    /// <param name="status">The lookup's status.</param>
    /// <returns>Read when it was fetched now, failed when it failed, otherwise not asked.</returns>
    public static WatchFeedStatus StatusOf(LookupStatus status) => status switch
    {
        LookupStatus.Fetched or LookupStatus.FromCase or LookupStatus.NotFound => WatchFeedStatus.Read,
        LookupStatus.Failed => WatchFeedStatus.Failed,
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
            origins.AddRange(lookup.Origins.Select(origin => origin.Sequence));
            var status = FeedTexts.StatusOf(lookup.Status);
            if (status != WatchFeedStatus.Read || lookup.Value is not { } read)
            {
                return new WatchFeedPage(status == WatchFeedStatus.Read ? WatchFeedStatus.Read : status,
                    transactions, origins, false, status == WatchFeedStatus.Read ? null : lookup.Problem);
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

        return new WatchFeedPage(WatchFeedStatus.Read, transactions, origins, true,
            FeedTexts.Format(FeedTexts.CutShort, address.Value, WatchRules.MaximumPages));
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
/// Reads an Ethereum address's newest movements for a check through <see cref="IEthereumSource"/> with
/// <see cref="LookupMode.InStep"/>: the histories the watched asset needs (ether in the transactions and the internal
/// ones, tokens in the ERC-20 transfers), each sealed in the case; a block whose hash a history lacks is looked up.
/// </summary>
/// <param name="source">Ethereum's source.</param>
/// <param name="case">The open case.</param>
internal sealed class EthereumCaseFeed(IEthereumSource source, ICase @case) : IWatchFeed
{
    /// <inheritdoc/>
    public async Task<WatchFeedPage> ReadAsync(
        ChainAddress address, Asset? asset, long downTo, CancellationToken cancellationToken = default)
    {
        if (!EthereumAddress.TryParse(address.Value, out var ethereum))
        {
            return new WatchFeedPage(
                WatchFeedStatus.Failed, [], [], false, FeedTexts.Format(FeedTexts.NotAnAddress, address.Value));
        }

        var transfers = new Dictionary<(string, EthereumTransferKind, long), EthereumTransfer>();
        var origins = new List<long>();
        var problems = new List<string>();
        var status = WatchFeedStatus.Read;
        var cut = false;
        foreach (var history in HistoriesFor(asset))
        {
            var (read, cutShort) = await ReadHistoryAsync(ethereum, history, downTo, transfers, origins, problems,
                cancellationToken).ConfigureAwait(false);
            status = (WatchFeedStatus)Math.Max((int)status, (int)read);
            cut |= cutShort;
        }

        if (cut)
        {
            problems.Add(FeedTexts.Format(FeedTexts.CutShort, address.Value, WatchRules.MaximumPages));
        }

        var hashes = await BlockHashesAsync(transfers.Values, origins, problems, cancellationToken)
            .ConfigureAwait(false);

        // A block whose hash could not be had keeps the check from moving on, so that the next check reads its
        // transactions again however long it takes.
        var hashless = transfers.Values.Any(transfer => transfer.Block is { } block && !hashes.ContainsKey(block.Height));
        if (hashless)
        {
            status = WatchFeedStatus.Failed;
        }
        var transactions = transfers.Values
            .GroupBy(transfer => transfer.Transaction.Value, StringComparer.Ordinal)
            .Select(group => Of(group.Key, [.. group.OrderBy(t => t.Kind).ThenBy(t => t.Position)], hashes))
            .ToList();
        return new WatchFeedPage(
            status, transactions, origins, cut, problems.Count == 0 ? null : string.Join(' ', problems.Distinct()));
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

    /// <summary>Reads one history back to a block, page by page.</summary>
    /// <param name="address">The address.</param>
    /// <param name="history">The history.</param>
    /// <param name="downTo">The block below which nothing is needed.</param>
    /// <param name="transfers">The movements so far, each once.</param>
    /// <param name="origins">The sealed responses so far.</param>
    /// <param name="problems">What went wrong so far.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <returns>How it went, and whether it stopped before reaching back.</returns>
    private async Task<(WatchFeedStatus Status, bool CutShort)> ReadHistoryAsync(
        EthereumAddress address,
        EthereumHistory history,
        long downTo,
        Dictionary<(string, EthereumTransferKind, long), EthereumTransfer> transfers,
        List<long> origins,
        List<string> problems,
        CancellationToken cancellationToken)
    {
        long? atOrBelow = null;
        for (var page = 0; page < WatchRules.MaximumPages; page++)
        {
            var lookup = await source.GetAddressHistoryAsync(
                @case, address, history, atOrBelow, LookupMode.InStep, cancellationToken).ConfigureAwait(false);
            origins.AddRange(lookup.Origins.Select(origin => origin.Sequence));
            var status = FeedTexts.StatusOf(lookup.Status);
            if (status != WatchFeedStatus.Read || lookup.Value is not { } read)
            {
                if (lookup.Problem is { } problem && status != WatchFeedStatus.Read)
                {
                    problems.Add(problem);
                }

                return (status, false);
            }

            foreach (var transfer in read.Transfers)
            {
                transfers.TryAdd((transfer.Transaction.Value, transfer.Kind, transfer.Position), transfer);
            }

            if (read.NextAtOrBelow is not { } next || next <= downTo)
            {
                return (WatchFeedStatus.Read, false);
            }

            atOrBelow = next;
        }

        return (WatchFeedStatus.Read, true);
    }

    /// <summary>Looks up the hash of every block a movement lacks it for, in step with the chain.</summary>
    /// <param name="transfers">The movements.</param>
    /// <param name="origins">The sealed responses so far.</param>
    /// <param name="problems">What went wrong so far.</param>
    /// <param name="cancellationToken">Stops the lookups.</param>
    /// <returns>The hashes found, per height.</returns>
    private async Task<IReadOnlyDictionary<long, string>> BlockHashesAsync(
        IEnumerable<EthereumTransfer> transfers,
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
            .Where(block => block.Hash is null && !hashes.ContainsKey(block.Height))
            .Select(block => block.Height)
            .Distinct()
            .ToList();
        foreach (var height in missing)
        {
            var lookup = await source.GetBlockAsync(@case, height, LookupMode.InStep, cancellationToken)
                .ConfigureAwait(false);
            origins.AddRange(lookup.Origins.Select(origin => origin.Sequence));
            if (lookup.Value?.Hash is { } hash)
            {
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
    /// Gives an Ethereum transaction's movements in the check's form: one flow per movement. A block whose hash is not
    /// known is left out, so that the transaction waits for a check that knows it (a hit is sealed with its hash).
    /// </summary>
    /// <param name="hash">The transaction's hash.</param>
    /// <param name="transfers">Its movements.</param>
    /// <param name="hashes">The blocks' hashes.</param>
    /// <returns>The check's form.</returns>
    private static WatchTransaction Of(
        string hash, IReadOnlyList<EthereumTransfer> transfers, IReadOnlyDictionary<long, string> hashes)
    {
        var at = transfers.Select(transfer => transfer.Block).OfType<EthereumBlockReference>().FirstOrDefault();
        var block = at is not null && hashes.TryGetValue(at.Height, out var blockHash)
            ? new WatchBlock(at.Height, blockHash, at.Time)
            : null;
        var flows = transfers.Select(transfer => new WatchFlow(
            [new WatchLeg(new ChainAddress(Chain.Ethereum, transfer.From.Value), transfer.Amount)],
            [new WatchLeg(new ChainAddress(Chain.Ethereum, transfer.To.Value), transfer.Amount)]));
        return new WatchTransaction(Chain.Ethereum, hash, block, [.. flows]);
    }
}
