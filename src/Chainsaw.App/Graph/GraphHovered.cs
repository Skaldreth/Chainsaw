namespace Chainsaw.App.Graph;

/// <summary>
/// What a move of the pointer onto another node or line changed in the drawing (principle 6): the frames of the node
/// it left and the node it came to, and the amount of a line it left or came to, which shows while it is hovered.
/// </summary>
/// <param name="Nodes">The nodes whose look changed.</param>
/// <param name="EdgeChanged">Whether the hovered line changed.</param>
public sealed record GraphHovered(IReadOnlySet<string> Nodes, bool EdgeChanged)
{
    /// <summary>Gives what changed between the hover before and after.</summary>
    /// <param name="nodeBefore">The node hovered before, or <see langword="null"/>.</param>
    /// <param name="nodeAfter">The node hovered now, or <see langword="null"/>.</param>
    /// <param name="edgeBefore">The line hovered before, or <see langword="null"/>.</param>
    /// <param name="edgeAfter">The line hovered now, or <see langword="null"/>.</param>
    /// <returns>What changed.</returns>
    public static GraphHovered Between(string? nodeBefore, string? nodeAfter, string? edgeBefore, string? edgeAfter)
    {
        var nodes = new HashSet<string>(StringComparer.Ordinal);
        if (!string.Equals(nodeBefore, nodeAfter, StringComparison.Ordinal))
        {
            foreach (var node in new[] { nodeBefore, nodeAfter })
            {
                if (node is not null)
                {
                    nodes.Add(node);
                }
            }
        }

        return new GraphHovered(nodes, !string.Equals(edgeBefore, edgeAfter, StringComparison.Ordinal));
    }
}
