using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// The addresses Chainsaw concludes have the same owner as a given address, and why each one is there. A cluster is
/// a conclusion, never a fact (principle 2).
/// </summary>
/// <param name="Start">The address the cluster was asked for; its members are explained from here.</param>
/// <param name="Members">
/// Every address in the cluster, the start first; each other member says which link brought it in and from which
/// member, so that every address can answer <em>why?</em> (principle 3).
/// </param>
/// <param name="Links">The links that merged the cluster's addresses.</param>
/// <param name="Suggestions">Links that touch the cluster but only propose, also after a split.</param>
/// <param name="Refused">Links that touch the cluster but were stopped or refused by a split.</param>
/// <param name="Splits">The user's splits that involve a member.</param>
public sealed record Cluster(
    BitcoinAddress Start,
    IReadOnlyList<ClusterMember> Members,
    IReadOnlyList<ClusterLink> Links,
    IReadOnlyList<ClusterLink> Suggestions,
    IReadOnlyList<ClusterLink> Refused,
    IReadOnlyList<ClusterSplit> Splits)
{
    /// <summary>Gets the cluster's addresses, the start first.</summary>
    public IEnumerable<BitcoinAddress> Addresses => Members.Select(member => member.Address);

    /// <summary>Says whether an address is in the cluster.</summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public bool Contains(BitcoinAddress address) => Members.Any(member => member.Address == address);
}

/// <summary>An address in a cluster and why it is there.</summary>
/// <param name="Address">The address.</param>
/// <param name="JoinedBy">
/// The link that brought it into the cluster, or <see langword="null"/> for the address the cluster was asked for.
/// </param>
/// <param name="Via">
/// The member the link joined it to, one step nearer the start, or <see langword="null"/> for the start.
/// </param>
public sealed record ClusterMember(BitcoinAddress Address, ClusterLink? JoinedBy, BitcoinAddress? Via);
