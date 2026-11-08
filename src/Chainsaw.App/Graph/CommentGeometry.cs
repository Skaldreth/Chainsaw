using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// Where a comment (sketch KN1a) stands and what its thread ties: the node a stored thread goes to now, the place of a
/// new comment, and the thread's two ends. Without WPF, so that it is tested.
/// </summary>
public static class CommentGeometry
{
    /// <summary>A comment's width while it grows with its text (sketch KN1a: 270 to 300).</summary>
    public const double NaturalWidth = 280;

    /// <summary>A comment's height before its text is laid out, for its frame until it is drawn.</summary>
    public const double NaturalHeight = 110;

    /// <summary>How far to the right of its node a new comment stands.</summary>
    private const double BesideNode = 56;

    /// <summary>How far above its node's top a new comment stands.</summary>
    private const double AboveNode = 96;

    /// <summary>
    /// Gives the key a thread to a node is stored under: the node's key, or for an address or a cluster on Bitcoin
    /// <c>address:</c> and its first address, so that the thread finds the node again when the cluster grows, is
    /// split or is represented by another address.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The key to store.</returns>
    public static string AnchorOf(GraphNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Kind is GraphNodeKind.Address or GraphNodeKind.Cluster && node.GraphAddresses.Count > 0
            ? GraphPile.MemberKey(node.GraphAddresses[0])
            : node.Key;
    }

    /// <summary>
    /// Finds the node a stored thread goes to in the graph shown: the node by its key, the actor that holds its address
    /// on any chain, or the pile (sketch EX2b) that actor lies in, so that a comment on a member keeps its thread.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="anchor">The stored key, or <see langword="null"/>.</param>
    /// <returns>The node's key, or <see langword="null"/> when there is no thread or its node is not drawn.</returns>
    public static string? Resolve(GraphScene? scene, string? anchor)
    {
        if (scene is null || anchor is null)
        {
            return null;
        }

        if (scene.Node(anchor) is { } drawn)
        {
            return drawn.Key;
        }

        if (ModelNodeOf(scene, anchor) is not { } holder)
        {
            return null;
        }

        if (scene.Node(holder.Key) is not null)
        {
            return holder.Key;
        }

        return PileHolding(scene, holder.Key);
    }

    /// <summary>
    /// Says whether a stored thread goes to a node that lies in the trash (sketch SO1a): the node by its key, the pile or
    /// clue by its key, or the actor that holds its address.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="anchor">The stored key, or <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the thread's node is thrown.</returns>
    public static bool IsThrown(GraphScene? scene, string? anchor)
    {
        if (scene is null || anchor is null || scene.Trash.IsEmpty)
        {
            return false;
        }

        if (scene.Trash.Hides(anchor))
        {
            return true;
        }

        return ModelNodeOf(scene, anchor) is { } node && scene.Trash.Hides(node);
    }

    /// <summary>
    /// Reads the address a stored key names, on any chain: <c>address:</c> and a Bitcoin address, or <c>account:</c>,
    /// the chain and an address on an account chain.
    /// </summary>
    /// <param name="anchor">The stored key.</param>
    /// <returns>The address, or <see langword="null"/> when the key names none.</returns>
    public static ChainAddress? AddressOf(string anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);
        if (anchor.StartsWith(GraphPile.AddressKeyPrefix, StringComparison.Ordinal))
        {
            return BitcoinAddress.TryParse(anchor[GraphPile.AddressKeyPrefix.Length..], out var bitcoin)
                ? bitcoin.ToChainAddress()
                : null;
        }

        if (!anchor.StartsWith(GraphPile.AccountKeyPrefix, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = anchor[GraphPile.AccountKeyPrefix.Length..];
        var colon = rest.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0 || colon == rest.Length - 1
            || !Enum.TryParse<Chain>(rest[..colon], ignoreCase: false, out var chain) || !Enum.IsDefined(chain))
        {
            return null;
        }

        var value = rest[(colon + 1)..];
        return value.Any(char.IsWhiteSpace) ? null : new ChainAddress(chain, value);
    }

    /// <summary>Finds the model's node a stored key names: by its key, or the actor that holds its address.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="anchor">The stored key.</param>
    /// <returns>The node, or <see langword="null"/> when the graph has none.</returns>
    private static GraphNode? ModelNodeOf(GraphScene scene, string anchor)
    {
        var graph = scene.Snapshot.Graph;
        if (graph.Node(anchor) is { } node)
        {
            return node;
        }

        return AddressOf(anchor) is { } address ? graph.NodeOf(address) : null;
    }

    /// <summary>Finds the drawn pile an actor lies in as one of its cards.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="key">The actor's key.</param>
    /// <returns>The pile's key, or <see langword="null"/> when no drawn pile holds it.</returns>
    private static string? PileHolding(GraphScene scene, string key) =>
        scene.Piles.Values
            .Where(pile => scene.Node(pile.Key) is not null)
            .FirstOrDefault(pile => pile.Cards.Any(card => string.Equals(card.Key, key, StringComparison.Ordinal)))
            ?.Key;

    /// <summary>Gives where a new comment stands: up and to the right of its node, or where it was asked for.</summary>
    /// <param name="nodePlace">The node's place, when the comment is about a node.</param>
    /// <param name="at">Where the comment was asked for on the surface (a double click, the pointer).</param>
    /// <returns>The comment's top left corner.</returns>
    public static GraphPoint PlaceOfNew(GraphPoint? nodePlace, GraphPoint at)
    {
        if (nodePlace is not { } place)
        {
            return at;
        }

        var frame = GraphSizes.NodeFrame(place);
        return new GraphPoint(frame.Right + BesideNode, frame.Y - AboveNode);
    }

    /// <summary>
    /// Gives a thread's two ends: on the comment's edge and on the node's frame, each where the line between their
    /// middles crosses it, so that the thread never runs under either.
    /// </summary>
    /// <param name="note">The comment's frame.</param>
    /// <param name="node">The node's frame.</param>
    /// <returns>The end at the comment and the end at the node, where the dot is drawn.</returns>
    public static (GraphPoint AtNote, GraphPoint AtNode) Thread(GraphRect note, GraphRect node) =>
        (Edge(note, node.Centre), Edge(node, note.Centre));

    /// <summary>Gives where the line from a frame's middle toward a point leaves the frame.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="toward">The point.</param>
    /// <returns>The point on the frame's edge; the middle when the point is the middle.</returns>
    public static GraphPoint Edge(GraphRect frame, GraphPoint toward)
    {
        var centre = frame.Centre;
        var dx = toward.X - centre.X;
        var dy = toward.Y - centre.Y;
        if (dx == 0 && dy == 0)
        {
            return centre;
        }

        var scaleX = dx == 0 ? double.PositiveInfinity : (frame.Width / 2) / Math.Abs(dx);
        var scaleY = dy == 0 ? double.PositiveInfinity : (frame.Height / 2) / Math.Abs(dy);
        var scale = Math.Min(Math.Min(scaleX, scaleY), 1);
        return new GraphPoint(centre.X + (dx * scale), centre.Y + (dy * scale));
    }
}
