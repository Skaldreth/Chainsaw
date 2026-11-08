using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Compares the clusters before and after a change in what counts, cluster by cluster, for a preview the user sees
/// before the change is made (sketch KV1a).
/// </summary>
internal static class ClusterChanges
{
    /// <summary>
    /// Gives the clusters that change: every cluster before that holds an address of a proposal that counts
    /// differently, seen from the address whose cluster after keeps the most of it.
    /// </summary>
    /// <param name="before">The clusters before.</param>
    /// <param name="after">The clusters after.</param>
    /// <param name="counting">The proposals that count before.</param>
    /// <param name="countingAfter">The proposals that count after.</param>
    /// <returns>The changes, the largest cluster first.</returns>
    public static IReadOnlyList<ClusterChange> Between(
        ClusterGraph before,
        ClusterGraph after,
        IReadOnlyList<ClusterEvidence> counting,
        IReadOnlyList<ClusterEvidence> countingAfter)
    {
        var touched = Differing(counting, countingAfter);
        var seen = new HashSet<BitcoinAddress>();
        var changes = new List<ClusterChange>();
        foreach (var address in touched.Where(address => !seen.Contains(address)))
        {
            var members = Sorted(before.ClusterOf(address).Addresses);
            var anchor = members.MaxBy(member => after.ClusterOf(member).Addresses.Count(members.Contains))!;
            var then = Sorted(after.ClusterOf(anchor).Addresses);
            seen.UnionWith(members);
            seen.UnionWith(then);
            if (!members.SequenceEqual(then))
            {
                changes.Add(new ClusterChange(anchor, members, then));
            }
        }

        return [.. changes.OrderByDescending(change => change.Before.Count)];
    }

    /// <summary>
    /// Gives the addresses of the proposals that count in one but not the other, or with another strength.
    /// </summary>
    /// <param name="counting">The proposals before.</param>
    /// <param name="countingAfter">The proposals after.</param>
    /// <returns>The addresses, in the order the proposals came.</returns>
    private static IEnumerable<BitcoinAddress> Differing(
        IReadOnlyList<ClusterEvidence> counting, IReadOnlyList<ClusterEvidence> countingAfter)
    {
        var then = countingAfter.Select(evidence => (evidence.Sequence, evidence.Strength)).ToHashSet();
        var now = counting.Select(evidence => (evidence.Sequence, evidence.Strength)).ToHashSet();
        return counting.Where(evidence => !then.Contains((evidence.Sequence, evidence.Strength)))
            .Concat(countingAfter.Where(evidence => !now.Contains((evidence.Sequence, evidence.Strength))))
            .SelectMany(evidence => evidence.Addresses)
            .Distinct();
    }

    /// <summary>Gives addresses in a fixed order, so that two sets can be compared.</summary>
    /// <param name="addresses">The addresses.</param>
    /// <returns>The addresses, sorted.</returns>
    private static List<BitcoinAddress> Sorted(IEnumerable<BitcoinAddress> addresses) =>
        [.. addresses.Distinct().OrderBy(address => address.Value, StringComparer.Ordinal)];
}
