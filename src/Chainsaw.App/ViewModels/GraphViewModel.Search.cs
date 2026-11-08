namespace Chainsaw.App.ViewModels;

/// <summary>
/// The command bar's search in the graph (sketch KR1a): the row chosen in the bar is shown at once, as the graph glides
/// so that its node lands under the bar, with a ring that pulses around it and the rest toned down. Enter chooses the
/// node and the ring goes.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>
    /// Gets the node the search shows, ringed while the rest is toned down; <see langword="null"/> when none is.
    /// </summary>
    public string? FoundNode { get; private set; }

    /// <summary>Raised when the surface should glide so that a node lands under the command bar.</summary>
    public event EventHandler<string>? FoundWanted;

    /// <summary>
    /// Shows the node a search row stands for, or stops showing one. A node the graph does not place shows nothing.
    /// </summary>
    /// <param name="nodeKey">The node, or <see langword="null"/>.</param>
    public void ShowFound(string? nodeKey)
    {
        var shown = nodeKey is not null && Places.ContainsKey(nodeKey) ? nodeKey : null;
        if (shown == FoundNode)
        {
            return;
        }

        FoundNode = shown;
        if (shown is not null)
        {
            FoundWanted?.Invoke(this, shown);
        }

        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Takes a search row (Enter): the node is chosen, the graph glides to it, and the ring goes.</summary>
    /// <param name="nodeKey">The node.</param>
    public void ChooseFound(string nodeKey)
    {
        ArgumentNullException.ThrowIfNull(nodeKey);
        FoundNode = null;
        if (!Places.ContainsKey(nodeKey))
        {
            Redraw?.Invoke(this, EventArgs.Empty);
            return;
        }

        Choose(nodeKey, null);
        FoundWanted?.Invoke(this, nodeKey);
        Redraw?.Invoke(this, EventArgs.Empty);
    }
}
