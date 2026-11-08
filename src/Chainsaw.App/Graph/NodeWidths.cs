namespace Chainsaw.App.Graph;

/// <summary>
/// How wide a node is drawn (sketch GR5): as wide as the graph's nodes (<see cref="GraphSizes.NodeWidth"/>), and wider
/// when its name needs it, up to <see cref="Widest"/>, so that a name is read whole and not cut. The address under a
/// name, or the address that is the title of a node without one, is shortened in the middle only as much as the room
/// asks (CLAUDE.md, <em>Nodernas namn</em>) and never widens the node, so that a graph of unnamed addresses keeps the
/// airy columns of GR4.
/// </summary>
/// <remarks>
/// The width is worked out from the name's characters, a little on the large side, so that the layout and the hit
/// test can be worked out and tested without WPF; the canvas measures the real text and trims it only when it is
/// longer than this room.
/// </remarks>
public static class NodeWidths
{
    /// <summary>The widest a node grows with its name: sketch GR5's <em>Misstänktes plånbok</em> and its count.</summary>
    public const double Widest = 214;

    /// <summary>The average width of a character of the name at 12,5 px semibold, as sketch GR5 measures it.</summary>
    public const double NameCharacter = 7.4;

    /// <summary>The width of a character of the cluster's count at 11 px.</summary>
    public const double CountCharacter = 6.5;

    /// <summary>
    /// The room a node takes beside its text: the padding on both sides (9 px), the icon's square (24 px) and the gap
    /// after it (8 px), as the canvas draws them.
    /// </summary>
    public const double Chrome = (2 * 9) + 24 + 8;

    /// <summary>The gap between the name and the cluster's count.</summary>
    public const double CountGap = 8;

    /// <summary>Gives how wide a node is drawn.</summary>
    /// <param name="look">The sort's look; a pile and a clue keep their own shape and the graph's width.</param>
    /// <param name="name">The node's name.</param>
    /// <param name="isNamed">Whether a label gives it a name; without one its title is the address, shortened.</param>
    /// <param name="count">The cluster's count, or an empty text.</param>
    /// <returns>The width, from <see cref="GraphSizes.NodeWidth"/> to <see cref="Widest"/>.</returns>
    public static double For(SortLook look, string name, bool isNamed, string count)
    {
        ArgumentNullException.ThrowIfNull(look);
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(count);
        if (!isNamed || look.Sort is NodeSort.Pile or NodeSort.HeldBack)
        {
            return GraphSizes.NodeWidth;
        }

        var countRoom = count.Length > 0 ? (count.Length * CountCharacter) + CountGap : 0;
        var wanted = Math.Ceiling(Chrome + (name.Length * NameCharacter) + countRoom);
        return Math.Clamp(wanted, GraphSizes.NodeWidth, Widest);
    }
}
