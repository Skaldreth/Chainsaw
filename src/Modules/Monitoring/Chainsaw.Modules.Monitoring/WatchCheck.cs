using System.Globalization;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// The check of a watch without the case (see <see cref="IWatchCheck"/>). What counts as money in and out:
/// <list type="bullet">
/// <item>Out: a movement that spends from a watched address. The amount is what goes to addresses outside the watched
/// ones (change back to the node is not money out), or with receivers only what goes to them.</item>
/// <item>In: a movement that pays a watched address and spends from none (otherwise it is the node's own change). The
/// amount is what the watched addresses get; with senders it counts only when one of them is among the senders.</item>
/// </list>
/// Amounts are summed per transaction, direction and asset, and the minimum is tested on the sum.
/// </summary>
internal sealed class WatchCheck : IWatchCheck
{
    /// <summary>What a check says when a feed failed for an address.</summary>
    public const string FailedText = "Uppslaget för {0} misslyckades.";

    /// <inheritdoc/>
    public IReadOnlyList<WatchMatch> Match(
        IReadOnlySet<ChainAddress> addresses, WatchFilter filter, WatchTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.Block is not { } block || transaction.Chain != filter.Chain)
        {
            return [];
        }

        var sums = new Dictionary<(WatchDirection, Asset), (AssetAmount Amount, HashSet<ChainAddress> Parties)>();
        foreach (var flow in transaction.Flows)
        {
            var spends = flow.From.Any(leg => Watched(addresses, leg));
            if (spends && filter.WatchesOut)
            {
                var receivers = new HashSet<ChainAddress>(filter.Receivers);
                foreach (var leg in flow.To.Where(leg => !Watched(addresses, leg)))
                {
                    if (receivers.Count == 0 || (leg.Address is { } to && receivers.Contains(to)))
                    {
                        Count(sums, WatchDirection.Out, leg, leg.Address is { } receiver ? [receiver] : [], filter);
                    }
                }
            }
            else if (!spends && filter.WatchesIn && FromWanted(flow, filter))
            {
                var senders = flow.From.Select(leg => leg.Address).OfType<ChainAddress>().Distinct().ToList();
                foreach (var leg in flow.To.Where(leg => Watched(addresses, leg)))
                {
                    Count(sums, WatchDirection.In, leg, senders, filter);
                }
            }
        }

        return [.. sums
            .Where(sum => !sum.Value.Amount.IsZero && Enough(filter, sum.Value.Amount))
            .OrderBy(sum => sum.Key.Item1)
            .ThenBy(sum => sum.Key.Item2.ToString(), StringComparer.Ordinal)
            .Select(sum => new WatchMatch(
                transaction.Id,
                block,
                sum.Key.Item1,
                sum.Value.Amount,
                [.. sum.Value.Parties.OrderBy(party => party.Value, StringComparer.Ordinal)]))];
    }

    /// <inheritdoc/>
    public async Task<WatchCheckResult> CheckAsync(
        IReadOnlyCollection<ChainAddress> addresses,
        WatchFilter filter,
        IWatchFeed feed,
        long after,
        long downTo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(feed);
        var watched = addresses.ToHashSet();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var matches = new List<WatchMatch>();
        var origins = new List<long>();
        var problems = new List<string>();
        var status = WatchFeedStatus.Read;
        long? highest = null;
        foreach (var address in addresses)
        {
            var page = await feed.ReadAsync(address, filter.Asset, downTo, cancellationToken).ConfigureAwait(false);
            origins.AddRange(page.Origins);
            status = Worse(status, page.Status);
            if (page.Problem is { } problem)
            {
                problems.Add(problem);
            }
            else if (page.Status == WatchFeedStatus.Failed)
            {
                problems.Add(string.Format(CultureInfo.InvariantCulture, FailedText, address.Value));
            }

            foreach (var transaction in page.Transactions)
            {
                if (transaction.Block is { } block)
                {
                    highest = Math.Max(highest ?? block.Height, block.Height);
                }

                if (transaction.Block?.Height > after && seen.Add(transaction.Id))
                {
                    matches.AddRange(Match(watched, filter, transaction));
                }
            }
        }

        return new WatchCheckResult(
            [.. matches.OrderBy(match => match.Block.Height)
                .ThenBy(match => match.Transaction, StringComparer.Ordinal)],
            status,
            [.. origins.Distinct().Order()],
            highest,
            [.. problems.Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>Gives the worse of two statuses: failed before not asked before read.</summary>
    /// <param name="left">One status.</param>
    /// <param name="right">The other.</param>
    /// <returns>The worse.</returns>
    private static WatchFeedStatus Worse(WatchFeedStatus left, WatchFeedStatus right) =>
        (WatchFeedStatus)Math.Max((int)left, (int)right);

    /// <summary>Says whether a leg is one of the watched addresses.</summary>
    /// <param name="addresses">The watched addresses.</param>
    /// <param name="leg">The leg.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool Watched(IReadOnlySet<ChainAddress> addresses, WatchLeg leg) =>
        leg.Address is { } address && addresses.Contains(address);

    /// <summary>Says whether money coming in through a flow comes from a wanted sender.</summary>
    /// <param name="flow">The flow.</param>
    /// <param name="filter">The filter.</param>
    /// <returns><see langword="true"/> when there are no senders to match, or one of them sent.</returns>
    private static bool FromWanted(WatchFlow flow, WatchFilter filter) =>
        filter.Senders.Count == 0
        || flow.From.Any(leg => leg.Address is { } from && filter.Senders.Contains(from));

    /// <summary>
    /// Adds a leg to its sum, when the watched asset allows it, with the parties it goes to or comes from.
    /// </summary>
    /// <param name="sums">The sums so far.</param>
    /// <param name="direction">In or out.</param>
    /// <param name="leg">The leg.</param>
    /// <param name="parties">The counterparties it goes to or comes from.</param>
    /// <param name="filter">The filter.</param>
    private static void Count(
        Dictionary<(WatchDirection, Asset), (AssetAmount Amount, HashSet<ChainAddress> Parties)> sums,
        WatchDirection direction,
        WatchLeg leg,
        IEnumerable<ChainAddress> parties,
        WatchFilter filter)
    {
        var asset = leg.Amount.Asset;
        if (filter.Asset is { } wanted && wanted != asset)
        {
            return;
        }

        var key = (direction, asset);
        var (amount, known) = sums.TryGetValue(key, out var sum) ? sum : (AssetAmount.Zero(asset), []);
        known.UnionWith(parties);
        sums[key] = (amount + leg.Amount, known);
    }

    /// <summary>Says whether a sum reaches the filter's minimum.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="amount">The sum.</param>
    /// <returns><see langword="true"/> without a minimum, or when it is reached.</returns>
    private static bool Enough(WatchFilter filter, AssetAmount amount) =>
        filter.Minimum is not { } minimum || minimum.Asset != amount.Asset || amount.Units >= minimum.Units;
}
