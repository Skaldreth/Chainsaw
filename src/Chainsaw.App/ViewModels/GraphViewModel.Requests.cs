using Chainsaw.App.Graph;
using Chainsaw.Modules.Requests.Contracts;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>That the user wants the view Begäran for a node (sketches BG1b and TK1a).</summary>
/// <param name="NodeKey">The node.</param>
/// <param name="Kind">What the request asks for.</param>
public sealed record RequestWanted(string NodeKey, RequestKind Kind);

/// <summary>
/// The graph's way to the view Begäran (sketch BG1b): <em>Begäran …</em> in the menu of a cash-out traced money
/// reached, and <em>Begäran om frysning …</em> in the inspector where a token can be frozen (sketch TK1a). Only nodes
/// that have such a request get the row or the button, since one that cannot be used is noise (principle 7).
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>Raised when the user wants the view Begäran with a node's request chosen.</summary>
    public event EventHandler<RequestWanted>? RequestWanted;

    /// <summary>Gets the cash-outs a request for information has been exported to, by node key (sketch BG1b).</summary>
    public IReadOnlySet<string> RequestedNodes { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Shows which cash-outs a request has been exported to, as the view Begäran reads them from the case, and draws
    /// again when that changed.
    /// </summary>
    /// <param name="nodes">The nodes.</param>
    public void ShowRequested(IReadOnlySet<string> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);
        if (RequestedNodes.SetEquals(nodes))
        {
            return;
        }

        RequestedNodes = nodes;
        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gives the marks in a node's top-right corner, in drawing order: the scene's, then the blue mark of an exported
    /// request. The watch's eye comes after these.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns>The marks.</returns>
    public IReadOnlyList<NodeBadge> MarksOf(NodeView node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return RequestedNodes.Contains(node.Key) ? [.. node.Badges, NodeBadge.RequestExported] : node.Badges;
    }

    /// <summary>
    /// Gets whether the chosen node has a freeze to ask its token's issuer for, so that the inspector's
    /// <em>Begäran om frysning …</em> shows only then (principle 7).
    /// </summary>
    public bool CanAskFreeze => HasRequest(SelectedNode, RequestKind.Freeze);

    /// <summary>Says whether a node has a request of a kind in the graph shown.</summary>
    /// <param name="nodeKey">The node.</param>
    /// <param name="kind">The kind.</param>
    /// <returns><see langword="true"/> when it has.</returns>
    public bool HasRequest(string? nodeKey, RequestKind kind) =>
        nodeKey is not null && Scene is { } scene
        && RequestExits.Find(scene).Any(exit => exit.NodeKey == nodeKey && exit.Kind == kind);

    /// <summary>
    /// Gives the menu's row <em>Begäran …</em> for a cash-out that traced money reached, and none for any other node.
    /// R is the suppliers' key, so the row has none of its own.
    /// </summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns>The row, or none.</returns>
    private IReadOnlyList<NodeMenuRow> RequestRow(string nodeKey) =>
        HasRequest(nodeKey, RequestKind.Information)
            ? [new(NodeMenuAction.Request, "Begäran …", string.Empty, "Icon.Request", true)]
            : [];

    /// <summary><em>Begäran om frysning …</em> in the inspector: the view Begäran with the node's freeze.</summary>
    [RelayCommand]
    public void AskFreeze() => WantRequest(SelectedNode, RequestKind.Freeze);

    /// <summary>Says again whether the freeze can be asked for, since the inspector follows the node and the graph.</summary>
    /// <param name="value">The inspector's content.</param>
    partial void OnInspectionChanged(InspectorContent? value) => OnPropertyChanged(nameof(CanAskFreeze));

    /// <summary>Asks for the view Begäran with a node's request, when it has one.</summary>
    /// <param name="nodeKey">The node.</param>
    /// <param name="kind">What the request asks for.</param>
    internal void WantRequest(string? nodeKey, RequestKind kind)
    {
        if (nodeKey is not null && HasRequest(nodeKey, kind))
        {
            RequestWanted?.Invoke(this, new RequestWanted(nodeKey, kind));
        }
    }
}
