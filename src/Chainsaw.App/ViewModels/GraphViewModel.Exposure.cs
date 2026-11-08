namespace Chainsaw.App.ViewModels;

/// <summary>
/// The graph's side of the view Exponering (sketch EP1b): <em>Visa vägen i grafen</em> chooses the service, lights the
/// way to it while the rest is drawn faint, and fits the way to the screen. Choosing anything else ends it.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>Gets the way shown from the view Exponering, or <see langword="null"/>.</summary>
    public (IReadOnlySet<string> Nodes, IReadOnlySet<string> Edges)? PathFocus { get; private set; }

    /// <summary>Shows a way in the graph: its last actor chosen, the way lit and fitted, and the note said.</summary>
    /// <param name="path">The way.</param>
    public void ShowPath(ExposurePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var drawn = path.Nodes.Where(key => Scene?.Node(key) is not null).ToList();
        Choose(drawn.LastOrDefault(), null);
        PathFocus = (
            path.Nodes.ToHashSet(StringComparer.Ordinal),
            path.Edges.ToHashSet(StringComparer.Ordinal));
        Say(path.Note);
        ShowNodesWanted?.Invoke(this, drawn);
        Redraw?.Invoke(this, EventArgs.Empty);
    }
}
