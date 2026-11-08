using Chainsaw.App.Graph;
using Chainsaw.App.Navigation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The graph's side of the view Exponering (sketch EP1b): <em>Visa vägen i grafen</em> chooses the service, lights the
/// way to it while the rest is drawn faint, and fits the way to the screen. Choosing anything else ends it. A button in
/// the graph's head, <em>‹ Exponering Esc</em>, goes back to the view the way came from, until the graph is left.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>Gets the way shown from the view Exponering, or <see langword="null"/>.</summary>
    public (IReadOnlySet<string> Nodes, IReadOnlySet<string> Edges)? PathFocus { get; private set; }

    /// <summary>Gets the view the graph was shown from, for the way back; <see langword="null"/> when none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasReturn), nameof(ReturnText))]
    public partial AppView? ReturnTo { get; private set; }

    /// <summary>Gets whether the head has a way back.</summary>
    public bool HasReturn => ReturnTo is not null;

    /// <summary>
    /// Gets whether Esc in the graph goes back: only when nothing is chosen, since Esc first lets go of what is.
    /// </summary>
    public bool ReturnsOnEscape => HasReturn && SelectedNode is null && SelectedEdge is null;

    /// <summary>Gets the way back's text: the view's name, for example <c>Exponering</c>.</summary>
    public string ReturnText => ReturnTo is { } view ? ViewTable.For(view).Name : string.Empty;

    /// <summary>Raised when the user goes back (the button or Esc) to the view the graph was shown from.</summary>
    public event EventHandler<AppView>? ReturnWanted;

    /// <summary>Raised when the user wants the view Exponering with a node in the centre (the node's menu).</summary>
    public event EventHandler<string>? ExposureWanted;

    /// <summary>
    /// Says whether a node can be the centre of the view Exponering: a cluster or address that paid or got.
    /// </summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    public bool HasExposure(string? nodeKey) =>
        nodeKey is not null && Scene is { } scene && ExposureMap.Subjects(scene).Any(subject => subject.Key == nodeKey);

    /// <summary>Asks for the view Exponering with a node in the centre, when it can be.</summary>
    /// <param name="nodeKey">The node.</param>
    internal void WantExposure(string? nodeKey)
    {
        if (HasExposure(nodeKey))
        {
            ExposureWanted?.Invoke(this, nodeKey!);
        }
    }

    /// <summary>
    /// Gives the menu's row <em>Exponering …</em> for a cluster or address that paid or got, and none for any other
    /// node, since a row that cannot be used is noise (principle 7). The view's own key opens it without a node, so
    /// the row has none.
    /// </summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns>The row, or none.</returns>
    private IReadOnlyList<NodeMenuRow> ExposureRow(string nodeKey) =>
        HasExposure(nodeKey)
            ? [new(NodeMenuAction.Exposure, "Exponering …", string.Empty, "Icon.Exposure", true)]
            : [];

    /// <summary>
    /// Shows a way in the graph: its last actor chosen, the way lit and fitted, the note said, and a way back to the
    /// view Exponering.
    /// </summary>
    /// <param name="path">The way.</param>
    public void ShowPath(ExposurePath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var drawn = path.Nodes.Where(key => Scene?.Node(key) is not null).ToList();
        Choose(drawn.LastOrDefault(), null);
        PathFocus = (
            path.Nodes.ToHashSet(StringComparer.Ordinal),
            path.Edges.ToHashSet(StringComparer.Ordinal));
        ReturnTo = AppView.Exposure;
        Say(path.Note);
        ShowNodesWanted?.Invoke(this, drawn);
        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Goes back to the view the graph was shown from (<em>Tillbaka · Esc</em>).</summary>
    [RelayCommand]
    public void Return()
    {
        if (ReturnTo is not { } view)
        {
            return;
        }

        ForgetReturn();
        ReturnWanted?.Invoke(this, view);
    }

    /// <summary>Forgets the way back when the graph is left, so that it never points to an old place.</summary>
    public void ForgetReturn()
    {
        if (ReturnTo is null)
        {
            return;
        }

        ReturnTo = null;
        if (PathFocus is not null)
        {
            PathFocus = null;
            Redraw?.Invoke(this, EventArgs.Empty);
        }
    }
}
