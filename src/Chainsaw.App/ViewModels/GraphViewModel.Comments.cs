using Chainsaw.App.Graph;
using Chainsaw.App.Links;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The graph's comments (sketch KN1a): notes on the surface, begun with N, a double click on the surface or the icon
/// over the chosen node, which then ties the note's thread to that node.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>The tip of the node's comment icon (sketch KN1a).</summary>
    public const string CommentToolText = "Kommentar";

    /// <summary>The key that begins a comment.</summary>
    public const string CommentKey = "N";

    /// <summary>What the graph says when a link in a comment names what is not in the graph.</summary>
    public const string LinkNotInGraphText = "Det länken pekar på finns inte i grafen än.";

    /// <summary>Gets the comments on the surface.</summary>
    public CommentsViewModel Comments { get; }

    /// <summary>
    /// Gets whether a stored comment is chosen, which the inspector shows with its history (sketches KN1a and AN1b); a
    /// draft has nothing to show until it is sealed.
    /// </summary>
    public bool HasChosenComment => Comments.Selected is { IsDraft: false, IsInTrash: false };

    /// <summary>Whether choosing nothing in the graph keeps the chosen comment, because the comment was just chosen.</summary>
    private bool _keepComment;

    /// <summary>The comment the inspector shows, so that it is shown again only when the comment changed.</summary>
    private Chainsaw.Modules.Tracing.Contracts.GraphComment? _inspectedComment;

    /// <summary>
    /// Shows a comment chosen on the surface in the inspector, in place of a chosen node or line, or lets the inspector
    /// go when it is let go.
    /// </summary>
    private void OnCommentChosen()
    {
        if (HasChosenComment && (SelectedNode is not null || SelectedEdge is not null))
        {
            _keepComment = true;
            try
            {
                Choose(null, null);
            }
            finally
            {
                _keepComment = false;
            }
        }
        else
        {
            Inspect();
        }

        OnPropertyChanged(nameof(IsInspectorShown));
    }

    /// <summary>Shows the chosen comment again when what the case says about it changed (a new text or thread).</summary>
    private void FollowChosenComment()
    {
        if (Comments.Selected?.Comment is { } comment && !ReferenceEquals(comment, _inspectedComment))
        {
            Inspect();
        }
    }

    /// <summary>Gives the name of the node a note's thread goes to in the graph shown.</summary>
    /// <param name="note">The note.</param>
    /// <returns>The name, or <see langword="null"/> when the thread goes to no node shown.</returns>
    private string? ThreadName(CommentNote note) => Scene?.Node(note.AnchorNode)?.Name;

    /// <summary>
    /// Begins a comment: about the chosen node, beside it with its thread tied to it, or else on its own where it was
    /// asked for.
    /// </summary>
    /// <param name="at">Where on the surface it was asked for: the double click, or the pointer for N.</param>
    /// <returns>The new note, or <see langword="null"/> with no case or no graph.</returns>
    public CommentNote? BeginComment(GraphPoint at)
    {
        if (Scene is null)
        {
            return null;
        }

        var node = Scene.Node(SelectedNode)?.Node;
        GraphPoint? nodePlace = SelectedNode is { } key && Places.TryGetValue(key, out var place) ? place : null;
        return Comments.Begin(CommentGeometry.PlaceOfNew(nodePlace, at), node);
    }

    /// <summary>Begins a comment on its own at a point of the empty surface (a double click on it).</summary>
    /// <param name="at">The point.</param>
    /// <returns>The new note, or <see langword="null"/> with no case or no graph.</returns>
    public CommentNote? BeginLooseComment(GraphPoint at) =>
        Scene is null ? null : Comments.Begin(at, null);

    /// <summary>
    /// Follows a <c>chainsaw:</c> link in a comment: the node it names is chosen and shown. Nothing is looked up, so
    /// what is not in the graph yet is only said (principle 4).
    /// </summary>
    /// <param name="target">The link.</param>
    [RelayCommand]
    public void FollowCommentLink(string? target)
    {
        if (!ChainsawLink.TryParse(target, out var startPoint) || Scene is null)
        {
            return;
        }

        if (Scene.Nodes.FirstOrDefault(node => Names(node, startPoint)) is not { } found)
        {
            Tell(ToastKind.Information, LinkNotInGraphText);
            return;
        }

        Choose(found.Key, null);
        ShowNodesWanted?.Invoke(this, [found.Key]);
    }

    /// <summary>
    /// Reads whether a node is the address or transaction a link names, on the same chain and with exactly the same
    /// value as the link reads it.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="named">What the link names.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool Names(NodeView node, Chainsaw.Modules.Chains.Contracts.ChainStartPoint named) =>
        named.Kind == Chainsaw.Modules.Chains.Contracts.StartPointKind.Address
            ? node.Node.Addresses.Any(address => address.Chain == named.Chain
                && string.Equals(address.Value, named.Value, StringComparison.Ordinal))
            : named.Chain == Chainsaw.SharedKernel.Chain.Bitcoin
                && string.Equals(node.Node.Transaction?.Value, named.Value, StringComparison.Ordinal);

    /// <summary>Connects the comments to the graph: drawing, toasts, the case and the threads' nodes.</summary>
    private void WireComments()
    {
        Comments.Changed += (_, _) =>
        {
            FollowChosenComment();
            Redraw?.Invoke(this, EventArgs.Empty);
        };
        Comments.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CommentsViewModel.Selected))
            {
                OnCommentChosen();
            }
        };
        Comments.Failed += (_, text) => Tell(ToastKind.Error, text);
        CaseChanged += (_, _) => _ = Comments.LoadAsync();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Scene))
            {
                Comments.Follow(Scene);
            }
        };
    }
}
