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
    /// Works out what cutting a link breaks out, as the clustering will once the splits are stored: the splits refuse
    /// every proposal of their heuristic that holds both their addresses (all of it, not only the step on the way),
    /// so the cluster is split again until nothing more falls away. Everything that no longer reaches the address
    /// that stays leaves, in the wallets it falls into.
    /// </summary>
    /// <param name="cluster">The cluster.</param>
    /// <param name="from">The address that stays.</param>
    /// <param name="to">The address that is not the same owner.</param>
    /// <param name="cut">The link to cut.</param>
    /// <returns>What leaves; nothing when other links still join the two.</returns>
    public static PairCut Cut(Cluster cluster, BitcoinAddress from, BitcoinAddress to, ClusterLink cut)
    {
        ArgumentNullException.ThrowIfNull(cluster);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentNullException.ThrowIfNull(cut);
        var merged = cluster.Links.Where(link => link.State == LinkState.Merged).ToList();
        var links = merged.Where(link => !Same(link, cut)).ToList();
        while (true)
        {
            var staying = Reach(links, from);
            if (staying.Contains(to))
            {
                return new PairCut([], []);
            }

            var leaving = cluster.Addresses.Where(address => !staying.Contains(address)).ToList();
            var splits = InspectorForms.BreakOut(cluster, leaving, string.Empty);
            var kept = merged.Where(link => !Same(link, cut) && !splits.Any(split => Refuses(split, link))).ToList();
            if (kept.Count == links.Count)
            {
                return new PairCut(leaving, Groups(links, leaving));
            }

            links = kept;
        }
    }

    /// <summary>Says whether two links are the same stored proposal.</summary>
    /// <param name="link">One link.</param>
    /// <param name="other">The other.</param>
    /// <returns><see langword="true"/> when they are.</returns>
    private static bool Same(ClusterLink link, ClusterLink other) =>
        link.Evidence.Sequence == other.Evidence.Sequence
        && link.Evidence.Heuristic.Name == other.Evidence.Heuristic.Name;

    /// <summary>Says whether a split refuses a link: the same heuristic, and both its addresses in the link.</summary>
    /// <param name="split">The split.</param>
    /// <param name="link">The link.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool Refuses(SplitRequest split, ClusterLink link) =>
        split.Heuristic == link.Evidence.Heuristic.Name
        && link.Evidence.Addresses.Contains(split.First)
        && link.Evidence.Addresses.Contains(split.Second);

    /// <summary>Gives the addresses a set of links joins to one.</summary>
    /// <param name="links">The links.</param>
    /// <param name="start">The address.</param>
    /// <returns>The addresses reached, the start among them.</returns>
    private static HashSet<BitcoinAddress> Reach(IReadOnlyList<ClusterLink> links, BitcoinAddress start)
    {
        var reached = new HashSet<BitcoinAddress> { start };
        var queue = new Queue<BitcoinAddress>([start]);
        while (queue.TryDequeue(out var address))
        {
            foreach (var next in links.Where(link => link.Evidence.Addresses.Contains(address))
                .SelectMany(link => link.Evidence.Addresses)
                .Where(reached.Add))
            {
                queue.Enqueue(next);
            }
        }

        return reached;
    }

    /// <summary>Gives the wallets the leaving addresses fall into.</summary>
    /// <param name="links">The links that still hold.</param>
    /// <param name="leaving">The addresses that leave.</param>
    /// <returns>The wallets, in the order of their first address.</returns>
    private static IReadOnlyList<IReadOnlyList<BitcoinAddress>> Groups(
        IReadOnlyList<ClusterLink> links, IReadOnlyList<BitcoinAddress> leaving)
    {
        var placed = new HashSet<BitcoinAddress>();
        var groups = new List<IReadOnlyList<BitcoinAddress>>();
        foreach (var address in leaving.Where(address => !placed.Contains(address)))
        {
            var group = Reach(links, address);
            placed.UnionWith(group);
            groups.Add([.. leaving.Where(group.Contains)]);
        }

        return groups;
    }
}

/// <summary>What cutting a link breaks out (sketch BU1a).</summary>
/// <param name="Leaving">The addresses that leave the cluster, in the cluster's order.</param>
/// <param name="Groups">The wallets they fall into: an address alone becomes a node of its own.</param>
public sealed record PairCut(IReadOnlyList<BitcoinAddress> Leaving, IReadOnlyList<IReadOnlyList<BitcoinAddress>> Groups);
