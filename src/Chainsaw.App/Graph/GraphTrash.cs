using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// A short dashed stub at a node's port where a line went to something in the trash (sketch SO1a): the line is not
/// drawn, but it is never silent either.
/// </summary>
/// <param name="NodeKey">The node the stub stands at.</param>
/// <param name="IsOut">Whether it stands at the out port (right), where money went; otherwise the in port (left).</param>
public sealed record TrashStub(string NodeKey, bool IsOut)
{
    /// <summary>What the stub's small label says.</summary>
    public const string Label = "i soptunnan";

    /// <summary>
    /// What the stub says at a watched node: the watch's hits to what lies in the trash are not drawn, so the stub
    /// never hides them without a word.
    /// </summary>
    public const string WatchedLabel = "i soptunnan · bevakad";
}

/// <summary>Where a node lies in the trash: the throw and the member it is put back by.</summary>
/// <param name="Throw">The throw's record in the sealed chain.</param>
/// <param name="Key">The member's key in the throw.</param>
public sealed record TrashPlace(long Throw, string Key);

/// <summary>
/// What the trash hides in the graph (sketch SO1a): every key its throws are found by. A node is hidden when its key
/// lies in the trash, or every key it is known by does (<see cref="TrashMember.IsHidden"/>), so that a cluster that got
/// another representative stays hidden, while one that grew into a thrown address is never hidden without a word.
/// No WPF.
/// </summary>
public sealed class TrashFilter
{
    /// <summary>The keys the trash's members are found by.</summary>
    private readonly HashSet<string> _keys;

    /// <summary>Creates the filter of the throws.</summary>
    /// <param name="throws">What lies in the trash.</param>
    public TrashFilter(IReadOnlyList<TrashThrow> throws)
    {
        ArgumentNullException.ThrowIfNull(throws);
        Throws = throws;
        _keys = throws.SelectMany(each => each.Members)
            .SelectMany(member => member.FoundBy.Append(member.Key))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Gets a filter that hides nothing.</summary>
    public static TrashFilter Empty { get; } = new([]);

    /// <summary>Gets what lies in the trash.</summary>
    public IReadOnlyList<TrashThrow> Throws { get; }

    /// <summary>Gets whether the trash hides anything.</summary>
    public bool IsEmpty => _keys.Count == 0;

    /// <summary>Says whether a node is hidden.</summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> when it lies in the trash.</returns>
    public bool Hides(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return TrashMember.IsHidden(node.Key, KeysOf(node), _keys);
    }

    /// <summary>Says whether a key the graph draws (a pile's or a clue's) is hidden.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when it lies in the trash.</returns>
    public bool Hides(string key) => _keys.Contains(key);

    /// <summary>
    /// Says whether an address lies in the trash: on Bitcoin by its address key, which every thrown actor is found by,
    /// on an account chain by its node key.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool HidesAddress(ChainAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        return Hides(address.Chain == Chain.Bitcoin
            ? GraphPile.AddressKeyPrefix + address.Value
            : GraphPile.AccountKey(address));
    }

    /// <summary>
    /// Finds where in the trash a hidden node lies: the newest throw with a member the node is known by, and that
    /// member's key, so that it can be put back from elsewhere than the trash's view (TODO, <em>Lägg tillbaka direkt
    /// på raden I soptunnan</em>).
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The throw and the member, or <see langword="null"/> when no throw holds it.</returns>
    public TrashPlace? PlaceOf(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var known = KeysOf(node).Append(node.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var thrown in Throws.OrderByDescending(each => each.Sequence))
        {
            if (thrown.Members.FirstOrDefault(member => member.FoundBy.Append(member.Key).Any(known.Contains)) is
                { } found)
            {
                return new TrashPlace(thrown.Sequence, found.Key);
            }
        }

        return null;
    }

    /// <summary>
    /// Gives the keys a node is known by in the trash: every address of an actor on Bitcoin, since a cluster's key
    /// follows its representative; otherwise its key.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The keys.</returns>
    public static IReadOnlyList<string> KeysOf(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Kind is GraphNodeKind.Address or GraphNodeKind.Cluster && node.Accounts.Count == 0
            ? [.. node.GraphAddresses.Select(GraphPile.MemberKey)]
            : [node.Key];
    }
}
