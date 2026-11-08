using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// Something the user threw in the trash (sketch SO1a): a node of the graph as it was drawn, and where it hangs in its
/// throw's tree. Throwing is only a filter: nothing leaves the case (principle 1), the thrown is hidden in the graph
/// and can be put back.
/// </summary>
/// <param name="Key">The drawn node's key: an actor's, a pile's or another node's the graph draws.</param>
/// <param name="Parent">
/// The member it followed with, or <see langword="null"/> for what the user threw; a parent always stands before its
/// children in a throw.
/// </param>
/// <param name="Name">The node's name as the graph wrote it, so that the trash can name it when the graph no longer
/// has it the same way (a cluster that grew).</param>
/// <param name="Sort">
/// The sort the graph drew it as, so that the trash draws it in the same form and colour; the application's own word,
/// which this module never reads.
/// </param>
public sealed record TrashMember(string Key, string? Parent, string Name, string Sort)
{
    /// <summary>
    /// Gets every key the node is known by, so that it is found and hidden again when its key changes: every address of
    /// an actor on Bitcoin (a cluster's key follows its representative, which changes when it grows), else the key.
    /// Empty means the key alone.
    /// </summary>
    public IReadOnlyList<string> Keys { get; init; } = [];

    /// <summary>Gets the keys it is found by: <see cref="Keys"/>, or the key alone.</summary>
    public IReadOnlyList<string> FoundBy => Keys.Count > 0 ? Keys : [Key];

    /// <summary>Gets whether the user threw it, rather than that it followed with something.</summary>
    public bool IsRoot => Parent is null;

    /// <summary>
    /// Says whether a node is hidden by what lies in the trash: its key lies there, or every key it is known by does.
    /// One shared address is not enough, so that a start point or a watched cluster that grows into a thrown address
    /// never disappears without a word.
    /// </summary>
    /// <param name="key">The node's key.</param>
    /// <param name="keys">The keys it is known by; empty means the key alone.</param>
    /// <param name="lying">Every key the trash's members are found by, and their keys.</param>
    /// <returns><see langword="true"/> when it is hidden.</returns>
    public static bool IsHidden(string key, IReadOnlyCollection<string> keys, IReadOnlySet<string> lying)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(keys);
        ArgumentNullException.ThrowIfNull(lying);
        return lying.Contains(key) || (keys.Count > 0 && keys.All(lying.Contains));
    }
}

/// <summary>One throw in the trash: what the user threw, what followed with it, who and when (sketch SO1a).</summary>
/// <param name="Sequence">The throw's record in the sealed chain.</param>
/// <param name="Members">What still lies in the trash from it, as a tree in depth-first order.</param>
/// <param name="By">Who threw it.</param>
/// <param name="Utc">When.</param>
/// <param name="JournalSequence">The line in the journal that records it.</param>
public sealed record TrashThrow(
    long Sequence, IReadOnlyList<TrashMember> Members, OperatorProfile By, DateTimeOffset Utc, long JournalSequence)
{
    /// <summary>Gets what the user threw, in the order they were thrown.</summary>
    public IReadOnlyList<TrashMember> Roots => [.. Members.Where(member => member.IsRoot)];

    /// <summary>Gives how deep a member hangs: 0 for what was thrown, 1 for what followed straight with it.</summary>
    /// <param name="key">The member's key.</param>
    /// <returns>The depth, or -1 when it is not in the throw.</returns>
    public int DepthOf(string key)
    {
        var depth = -1;
        for (var member = Find(key); member is not null; member = Find(member.Parent))
        {
            depth++;
        }

        return depth;
    }

    /// <summary>Gives a member and everything that hangs under it, in the throw's order.</summary>
    /// <param name="key">The member's key.</param>
    /// <returns>The branch; empty when the member is not in the throw.</returns>
    public IReadOnlyList<TrashMember> Branch(string key)
    {
        if (Find(key) is null)
        {
            return [];
        }

        var keys = new HashSet<string>(StringComparer.Ordinal) { key };
        var branch = new List<TrashMember>();
        foreach (var member in Members)
        {
            if (keys.Contains(member.Key) || (member.Parent is not null && keys.Contains(member.Parent)))
            {
                keys.Add(member.Key);
                branch.Add(member);
            }
        }

        return branch;
    }

    /// <summary>
    /// Gives what putting a member back brings back: the members it hangs under that still lie in the trash, so that it
    /// never stands in the graph cut off from where it was reached, and its branch; in the throw's order.
    /// </summary>
    /// <param name="key">The member's key.</param>
    /// <returns>What comes back; empty when the member is not in the throw.</returns>
    public IReadOnlyList<TrashMember> Restoring(string key)
    {
        var back = Branch(key).Select(member => member.Key).ToHashSet(StringComparer.Ordinal);
        for (var parent = Find(Find(key)?.Parent); parent is not null; parent = Find(parent.Parent))
        {
            back.Add(parent.Key);
        }

        return [.. Members.Where(member => back.Contains(member.Key))];
    }

    /// <summary>Gives a member by its key.</summary>
    /// <param name="key">The key, or <see langword="null"/>.</param>
    /// <returns>The member, or <see langword="null"/>.</returns>
    public TrashMember? Find(string? key) =>
        key is null ? null : Members.FirstOrDefault(member => member.Key == key);
}

/// <summary>What a record in the trash did.</summary>
public enum TrashAction
{
    /// <summary>Threw something in the trash.</summary>
    Thrown,

    /// <summary>Put something back from the trash.</summary>
    Restored,
}

/// <summary>
/// One sealed record of the trash about one thing in it, for its history (sketch AN1b): that it was thrown, or put
/// back, by whom and when.
/// </summary>
/// <param name="Sequence">The record's place in the sealed chain.</param>
/// <param name="Action">What it did.</param>
/// <param name="ThrowSequence">The throw it belongs to: its own sequence for a throw, the throw put back from.</param>
/// <param name="By">Who.</param>
/// <param name="Utc">When.</param>
/// <param name="JournalSequence">The line in the journal that records it.</param>
public sealed record TrashRecord(
    long Sequence, TrashAction Action, long ThrowSequence, OperatorProfile By, DateTimeOffset Utc, long JournalSequence);

/// <summary>One step of what follows with a throw: a node and the node it hangs under.</summary>
/// <param name="Key">The node.</param>
/// <param name="Parent">The node it is reached through, or <see langword="null"/> for what is thrown.</param>
public sealed record TrashBranch(string Key, string? Parent);

/// <summary>
/// The rule of what follows with a throw (sketch SO1a): what is reached only through what is thrown. A node that can
/// still be reached from a start point without passing through what is thrown stays; the rest hangs under the thrown
/// node it is reached through, as a tree. The lines count in both directions, so that a payer seen only at the thrown
/// actor follows it too and is never left alone in the graph.
/// </summary>
public static class TrashTree
{
    /// <summary>Gives what follows with a throw, as a tree in depth-first order with the thrown first.</summary>
    /// <param name="thrown">What the user throws, in order; at least one.</param>
    /// <param name="lines">The lines drawn between the nodes, from the payer to the payee.</param>
    /// <param name="anchors">
    /// The nodes the graph starts in (the start points). When none is left beside what is thrown, every node no line
    /// reaches stands in for them, so that throwing the only start point never empties the graph without a warning
    /// tree to show it.
    /// </param>
    /// <returns>The thrown and what follows, each once.</returns>
    /// <exception cref="ArgumentException">Nothing is thrown, or a key is empty.</exception>
    public static IReadOnlyList<TrashBranch> Follow(
        IReadOnlyList<string> thrown, IEnumerable<(string From, string To)> lines, IEnumerable<string> anchors)
    {
        ArgumentNullException.ThrowIfNull(thrown);
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(anchors);
        if (thrown.Count == 0 || thrown.Any(string.IsNullOrEmpty))
        {
            throw new ArgumentException("A throw needs at least one node, each with a key.", nameof(thrown));
        }

        var roots = new HashSet<string>(thrown, StringComparer.Ordinal);
        var links = lines.Where(line => line.From != line.To).ToList();
        var next = Neighbours(links);
        var reached = Reach(Starts(roots, links, anchors), next, roots);
        var tree = new List<TrashBranch>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in thrown.Where(seen.Add))
        {
            Walk(root, next, reached, roots, seen, tree);
        }

        return tree;
    }

    /// <summary>Gives the nodes reachability starts from: the anchors not thrown, else the nodes no line reaches.</summary>
    /// <param name="roots">What is thrown.</param>
    /// <param name="links">The lines.</param>
    /// <param name="anchors">The start points.</param>
    /// <returns>The starts.</returns>
    private static List<string> Starts(
        HashSet<string> roots, List<(string From, string To)> links, IEnumerable<string> anchors)
    {
        var starts = anchors.Where(anchor => !roots.Contains(anchor)).ToList();
        if (starts.Count > 0)
        {
            return starts;
        }

        var reachedByLine = links.Select(line => line.To).ToHashSet(StringComparer.Ordinal);
        return
        [
            .. links.Select(line => line.From)
                .Where(node => !reachedByLine.Contains(node) && !roots.Contains(node))
                .Distinct(StringComparer.Ordinal),
        ];
    }

    /// <summary>Gives each node's neighbours along the lines, both ways, in the lines' order.</summary>
    /// <param name="links">The lines.</param>
    /// <returns>The neighbours by node.</returns>
    private static Dictionary<string, List<string>> Neighbours(List<(string From, string To)> links)
    {
        var next = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (from, to) in links)
        {
            Add(from, to);
            Add(to, from);
        }

        return next;

        void Add(string node, string other)
        {
            if (!next.TryGetValue(node, out var list))
            {
                list = [];
                next.Add(node, list);
            }

            if (!list.Contains(other, StringComparer.Ordinal))
            {
                list.Add(other);
            }
        }
    }

    /// <summary>Gives every node reached from the starts without passing through what is thrown.</summary>
    /// <param name="starts">The starts.</param>
    /// <param name="next">The neighbours.</param>
    /// <param name="roots">What is thrown, which blocks the way.</param>
    /// <returns>The reached nodes.</returns>
    private static HashSet<string> Reach(
        List<string> starts, Dictionary<string, List<string>> next, HashSet<string> roots)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(starts);
        while (queue.TryDequeue(out var node))
        {
            if (roots.Contains(node) || !reached.Add(node))
            {
                continue;
            }

            foreach (var other in next.GetValueOrDefault(node) ?? [])
            {
                queue.Enqueue(other);
            }
        }

        return reached;
    }

    /// <summary>
    /// Hangs a thrown node and what is reached only through it under it, depth first, with a stack of its own rather
    /// than the call stack, so that a peel chain of thousands of hops is walked as safely as a short one.
    /// </summary>
    /// <param name="root">The thrown node.</param>
    /// <param name="next">The neighbours.</param>
    /// <param name="reached">What stays.</param>
    /// <param name="roots">What is thrown, each its own top.</param>
    /// <param name="seen">What already hangs in the tree.</param>
    /// <param name="tree">The tree, added to.</param>
    private static void Walk(
        string root,
        Dictionary<string, List<string>> next,
        HashSet<string> reached,
        HashSet<string> roots,
        HashSet<string> seen,
        List<TrashBranch> tree)
    {
        var stack = new Stack<TrashBranch>();
        stack.Push(new TrashBranch(root, null));
        while (stack.TryPop(out var branch))
        {
            tree.Add(branch);
            var children = (next.GetValueOrDefault(branch.Key) ?? [])
                .Where(other => !reached.Contains(other) && !roots.Contains(other) && !seen.Contains(other))
                .ToList();
            seen.UnionWith(children);

            // Pushed in reverse, so that they are taken in the lines' order, as a recursive walk would.
            for (var index = children.Count - 1; index >= 0; index--)
            {
                stack.Push(new TrashBranch(children[index], branch.Key));
            }
        }
    }
}

/// <summary>
/// The trash of the graph (sketch SO1a): throwing hides nodes in the graph and putting back shows them again. Both are
/// sealed records with a line in the journal, so that what is hidden stays hidden when the case is opened again and
/// every change can be followed (principle 1); nothing the graph rests on is touched, and a throw is never removed.
/// </summary>
/// <remarks>The methods are synchronous; call them off the UI thread.</remarks>
public interface IGraphTrash
{
    /// <summary>Reads what lies in the trash: every throw with what is still in it, oldest first.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The throws; one whose every member has been put back is left out.</returns>
    IReadOnlyList<TrashThrow> GetThrows(ICase @case);

    /// <summary>Throws nodes in the trash, sealed with a line in the journal.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="members">
    /// What is thrown and what follows with it, as a tree (see <see cref="TrashTree.Follow"/>): the first a root, each
    /// parent before its children, each key once.
    /// </param>
    /// <returns>The throw.</returns>
    /// <exception cref="ArgumentException">The members do not make such a tree, or a name or sort is empty.</exception>
    /// <exception cref="InvalidOperationException">A node already lies in the trash.</exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    TrashThrow Throw(ICase @case, IReadOnlyList<TrashMember> members);

    /// <summary>
    /// Puts back a member with everything under it and the members it hangs under (see
    /// <see cref="TrashThrow.Restoring"/>), or a whole throw, sealed with a line in the journal. What is put back
    /// stands where it stood, since its places were never touched.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="throwSequence">The throw.</param>
    /// <param name="key">The member to put back with its branch, or <see langword="null"/> for the whole throw.</param>
    /// <returns>What was put back; empty when nothing of it was in the trash.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> or <see cref="CaseError.Closed"/>.
    /// </exception>
    IReadOnlyList<TrashMember> Restore(ICase @case, long throwSequence, string? key);

    /// <summary>
    /// Reads every throw and putting back of one thing, also those long put back, oldest first, so that its history
    /// can show them (sketch AN1b).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="key">The thing's key, as it was thrown (<see cref="TrashMember.Key"/>).</param>
    /// <returns>The records; none when it was never thrown.</returns>
    IReadOnlyList<TrashRecord> GetHistory(ICase @case, string key);
}
