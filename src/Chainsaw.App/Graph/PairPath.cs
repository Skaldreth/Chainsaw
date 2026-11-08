using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// One link on the way between two addresses (sketch BU1a): the address it starts at and what joins it on.
/// </summary>
/// <param name="From">The address on the way the link starts at.</param>
/// <param name="To">The next address on the way.</param>
/// <param name="Link">The stored proposal that joins them.</param>
public sealed record PathStep(BitcoinAddress From, BitcoinAddress To, ClusterLink Link);

/// <summary>
/// The way between two addresses in one cluster, as the form Bryt upp draws it (sketch BU1a): the steps, the weakest
/// link to cut, and what a cut breaks out. Pure, so that it is tested without WPF.
/// </summary>
public static class PairPath
{
    /// <summary>Gives the steps of a way from <see cref="IClusteringService.ExplainConnection"/>.</summary>
    /// <param name="from">The first address.</param>
    /// <param name="to">The second address.</param>
    /// <param name="links">The links in order from <paramref name="from"/>.</param>
    /// <returns>The steps, each with the address it starts and ends at.</returns>
    public static IReadOnlyList<PathStep> Steps(
        BitcoinAddress from, BitcoinAddress to, IReadOnlyList<ClusterLink> links)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(links);
        var steps = new List<PathStep>();
        var at = from;
        for (var index = 0; index < links.Count; index++)
        {
            var next = index == links.Count - 1
                ? to
                : links[index].Evidence.Addresses.Intersect(links[index + 1].Evidence.Addresses)
                    .FirstOrDefault(address => address != at) ?? to;
            steps.Add(new PathStep(at, next, links[index]));
            at = next;
        }

        return steps;
    }

    /// <summary>
    /// Says how weak a link is, so that the form puts the scissors on the weakest: a sign of change or of a peel
    /// chain is weaker than common inputs, and the user's own merge is the strongest.
    /// </summary>
    /// <param name="heuristic">The link's analysis.</param>
    /// <returns>The weakness; lower is weaker.</returns>
    public static int Strength(HeuristicId heuristic)
    {
        ArgumentNullException.ThrowIfNull(heuristic);
        return heuristic.Name switch
        {
            HeuristicNames.ChangeAddress => 0,
            HeuristicNames.PeelChain => 1,
            HeuristicNames.PayJoin => 2,
            UserMerges.Name => 4,
            _ => 3,
        };
    }

    /// <summary>
    /// Gives the step the form suggests cutting: the weakest, and the one nearest the second address on a tie.
    /// </summary>
    /// <param name="steps">The steps.</param>
    /// <returns>Its index, or -1 when there are none.</returns>
    public static int Weakest(IReadOnlyList<PathStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var weakest = -1;
        for (var index = 0; index < steps.Count; index++)
        {
            if (weakest < 0 || Strength(steps[index].Link.Evidence.Heuristic)
                <= Strength(steps[weakest].Link.Evidence.Heuristic))
            {
                weakest = index;
            }
        }

        return weakest;
    }

    /// <summary>
    /// Gives the addresses that leave the cluster when a link is cut: those still joined to the second address
    /// without it, or none when other links still join the two.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="from">The address that stays.</param>
    /// <param name="to">The address that is not the same owner.</param>
    /// <param name="cut">The link to cut.</param>
    /// <returns>The addresses that leave, or empty when the cut does not part them.</returns>
    public static IReadOnlyList<BitcoinAddress> Leaving(
        Cluster cluster, BitcoinAddress from, BitcoinAddress to, ClusterLink cut)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(cut);
        var links = cluster.Links
            .Where(link => link.State == LinkState.Merged && link.Evidence.Sequence != cut.Evidence.Sequence)
            .ToList();
        var reached = new HashSet<BitcoinAddress> { to };
        var queue = new Queue<BitcoinAddress>([to]);
        while (queue.TryDequeue(out var address))
        {
            foreach (var next in links.Where(link => link.Evidence.Addresses.Contains(address))
                .SelectMany(link => link.Evidence.Addresses)
                .Where(reached.Add))
            {
                queue.Enqueue(next);
            }
        }

        return reached.Contains(from) ? [] : [.. cluster.Addresses.Where(reached.Contains)];
    }
}
