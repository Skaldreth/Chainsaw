using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// The clusters as they stand: built from the stored proposals in the order they first appeared, with the user's splits
/// applied to every one of them, whenever the splits were made. A proposal that would bring two split addresses into
/// the same cluster merges nothing: it is refused when it comes from the heuristic the split names, and shown as a
/// suggestion when it comes from another.
/// </summary>
/// <remarks>
/// A merge proposal joins all its addresses or none, so the result depends only on the stored records and their
/// order, never on when the clusters are asked for.
/// </remarks>
internal sealed class ClusterGraph
{
    /// <summary>Each address's parent in the union-find forest; a root is its own parent.</summary>
    private readonly Dictionary<BitcoinAddress, BitcoinAddress> _parent = [];

    /// <summary>Every link, in sequence order.</summary>
    private readonly List<ClusterLink> _links = [];

    /// <summary>The links that merged, by each address they contain, in sequence order.</summary>
    private readonly Dictionary<BitcoinAddress, List<ClusterLink>> _merged = [];

    /// <summary>The splits, in sequence order.</summary>
    private readonly IReadOnlyList<ClusterSplit> _splits;

    /// <summary>Builds the clusters.</summary>
    /// <param name="evidence">
    /// The proposals that count now, in the order their results first appeared; one with fewer than two different
    /// addresses links nothing and is left out.
    /// </param>
    /// <param name="splits">The user's splits, in sequence order.</param>
    public ClusterGraph(IEnumerable<ClusterEvidence> evidence, IReadOnlyList<ClusterSplit> splits)
    {
        _splits = splits;
        foreach (var item in evidence.Where(item => item.Addresses.Distinct().Count() >= 2))
        {
            var link = Place(item);
            _links.Add(link);
            if (link.State != LinkState.Merged)
            {
                continue;
            }

            foreach (var address in item.Addresses.Distinct())
            {
                if (!_merged.TryGetValue(address, out var list))
                {
                    list = [];
                    _merged[address] = list;
                }

                list.Add(link);
            }
        }
    }

    /// <summary>Builds the cluster of an address, explained from that address.</summary>
    /// <param name="start">The address.</param>
    /// <returns>The cluster.</returns>
    public Cluster ClusterOf(BitcoinAddress start)
    {
        var (members, _) = Walk(start);
        var addresses = members.Select(member => member.Address).ToHashSet();
        var links = _links.Where(link => link.State == LinkState.Merged && Touches(link, addresses)).ToList();
        var suggestions = _links.Where(link =>
            (link.State is LinkState.Suggested or LinkState.SuggestedAfterSplit) && Touches(link, addresses)).ToList();
        var refused = _links.Where(link =>
            (link.State is LinkState.Stopped or LinkState.RefusedBySplit) && Touches(link, addresses)).ToList();
        var splits = _splits.Where(split => addresses.Contains(split.First) || addresses.Contains(split.Second))
            .ToList();
        return new Cluster(start, members, links, suggestions, refused, splits);
    }

    /// <summary>Gives the links on the way from one address to another in the same cluster.</summary>
    /// <param name="from">One address.</param>
    /// <param name="to">The other address.</param>
    /// <returns>The links in order from <paramref name="from"/>, or <see langword="null"/> if not connected.</returns>
    public IReadOnlyList<ClusterLink>? Path(BitcoinAddress from, BitcoinAddress to)
    {
        var (_, byAddress) = Walk(from);
        if (!byAddress.TryGetValue(to, out var member))
        {
            return null;
        }

        var path = new List<ClusterLink>();
        while (member is { JoinedBy: { } link, Via: { } via })
        {
            path.Add(link);
            member = byAddress[via];
        }

        path.Reverse();
        return path;
    }

    /// <summary>Decides what a stored proposal does, given what has been merged before it and the splits.</summary>
    /// <param name="evidence">The proposal.</param>
    /// <returns>The link with its state.</returns>
    private ClusterLink Place(ClusterEvidence evidence)
    {
        switch (evidence.Strength)
        {
            case EvidenceStrength.Stopped:
                return new ClusterLink(evidence, LinkState.Stopped);
            case EvidenceStrength.Suggestion:
                return new ClusterLink(evidence, LinkState.Suggested);
        }

        var roots = evidence.Addresses.Select(Find).ToHashSet();
        var crossed = _splits.Where(split => Crosses(split, roots)).ToList();
        if (crossed.Count > 0)
        {
            var own = crossed.FirstOrDefault(split =>
                string.Equals(split.Heuristic, evidence.Heuristic.Name, StringComparison.Ordinal));
            return own is not null
                ? new ClusterLink(evidence, LinkState.RefusedBySplit, own)
                : new ClusterLink(evidence, LinkState.SuggestedAfterSplit, crossed[0]);
        }

        var first = evidence.Addresses[0];
        foreach (var address in evidence.Addresses.Skip(1))
        {
            Union(first, address);
        }

        return new ClusterLink(evidence, LinkState.Merged);
    }

    /// <summary>
    /// Says whether joining the clusters with the given roots would bring a split's two addresses together.
    /// </summary>
    /// <param name="split">The split.</param>
    /// <param name="roots">The roots of the clusters the proposal would join.</param>
    /// <returns><see langword="true"/> when it would.</returns>
    private bool Crosses(ClusterSplit split, HashSet<BitcoinAddress> roots)
    {
        var first = Find(split.First);
        var second = Find(split.Second);
        return first != second && roots.Contains(first) && roots.Contains(second);
    }

    /// <summary>Walks the merged links breadth first from an address, in sequence order.</summary>
    /// <param name="start">The address.</param>
    /// <returns>The members in the order reached, and the same by address.</returns>
    private (List<ClusterMember> Members, Dictionary<BitcoinAddress, ClusterMember> ByAddress) Walk(
        BitcoinAddress start)
    {
        var first = new ClusterMember(start, null, null);
        var members = new List<ClusterMember> { first };
        var byAddress = new Dictionary<BitcoinAddress, ClusterMember> { [start] = first };
        var queue = new Queue<BitcoinAddress>([start]);
        while (queue.TryDequeue(out var current))
        {
            foreach (var link in _merged.GetValueOrDefault(current) ?? [])
            {
                foreach (var address in link.Evidence.Addresses.Where(address => !byAddress.ContainsKey(address)))
                {
                    var member = new ClusterMember(address, link, current);
                    members.Add(member);
                    byAddress[address] = member;
                    queue.Enqueue(address);
                }
            }
        }

        return (members, byAddress);
    }

    /// <summary>Says whether a link contains any of the addresses.</summary>
    /// <param name="link">The link.</param>
    /// <param name="addresses">The addresses.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool Touches(ClusterLink link, HashSet<BitcoinAddress> addresses) =>
        link.Evidence.Addresses.Any(addresses.Contains);

    /// <summary>Finds an address's root, halving the path on the way.</summary>
    /// <param name="address">The address; one never seen is its own root.</param>
    /// <returns>The root.</returns>
    private BitcoinAddress Find(BitcoinAddress address)
    {
        var current = address;
        while (_parent.TryGetValue(current, out var parent) && parent != current)
        {
            var grandparent = _parent.GetValueOrDefault(parent, parent);
            _parent[current] = grandparent;
            current = grandparent;
        }

        return current;
    }

    /// <summary>Joins two addresses' clusters.</summary>
    /// <param name="first">One address.</param>
    /// <param name="second">The other address.</param>
    private void Union(BitcoinAddress first, BitcoinAddress second)
    {
        var a = Find(first);
        var b = Find(second);
        _parent.TryAdd(a, a);
        if (a != b)
        {
            _parent[b] = a;
        }
    }
}
