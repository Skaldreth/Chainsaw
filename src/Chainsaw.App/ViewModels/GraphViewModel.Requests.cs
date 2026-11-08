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
