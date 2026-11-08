using System.Globalization;
using Chainsaw.App.Graph;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>How the part of the graph on screen is asked to move, from the menu on the empty surface.</summary>
public enum ViewMove
{
    /// <summary>Passa in (F): the whole graph fitted to the screen.</summary>
    Fit,

    /// <summary>Zooma in (+).</summary>
    ZoomIn,

    /// <summary>Zooma ut (−).</summary>
    ZoomOut,
}

/// <summary>
/// The graph's frames at each node's own width (sketch GR5), the guide on from a start point (sketch GR6c), the mark
/// for the hidden outputs in the corner, and the abilities given a visible way (TODO, <em>Förmågor utan synlig
/// väg</em>): a double click on a node expands it, and the empty surface's menu fits, zooms and begins a comment.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>The menu row that fits the graph to the screen.</summary>
    public const string FitText = "Passa in";

    /// <summary>The menu row that zooms in.</summary>
    public const string ZoomInText = "Zooma in";

    /// <summary>The menu row that zooms out.</summary>
    public const string ZoomOutText = "Zooma ut";

    /// <summary>Raised when the menu asks the part on screen to move; the canvas owns the viewport.</summary>
    public event EventHandler<ViewMove>? ViewMoveWanted;

    /// <summary>
    /// Gets where on the surface the empty surface's menu was opened (right click), or <see langword="null"/> when it
    /// is not open; a comment begun from it stands there.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMenuOpen))]
    public partial GraphPoint? SurfaceMenuAt { get; private set; }

    /// <summary>
    /// Gets the guide on from the start point (sketch GR6c), while the start point stands alone and is not expanded;
    /// otherwise <see langword="null"/>.
    /// </summary>
    public StartGuide? StartPointGuide => HasCase ? StartGuide.For(Scene, Places) : null;

    /// <summary>
    /// Gets what the line under the guide's <em>Expandera</em> says: what the expansion does, or honestly that it
    /// cannot fetch anything now (principle 9).
    /// </summary>
    public string StartGuideText => ShowsOffline ? StartGuide.OfflineText : StartGuide.OnlineText;

    /// <summary>Gets <em>Expandera</em>'s second line: its key, or that it runs.</summary>
    public string StartGuideHint => IsExpanding ? ExpandText : StartGuide.ExpandHint;

    /// <summary>
    /// Gets whether the graph has outputs that can never be spent and carry nothing (OP_RETURN, 0 BTC), so that the
    /// corner marks them, hidden or shown, and they can always be shown again.
    /// </summary>
    public bool HasUnspendable => Scene?.Unspendable > 0;

    /// <summary>Gets what the corner's mark for those outputs says on hovering.</summary>
    public string UnspendableToolTip
    {
        get
        {
            var count = Scene?.Unspendable ?? 0;
            var what = count == 1
                ? "1 utgång utan mottagare (OP_RETURN, 0 BTC)"
                : string.Create(CultureInfo.InvariantCulture, $"{count} utgångar utan mottagare (OP_RETURN, 0 BTC)");
            return ShowsUnspendable
                ? $"{what} visas i grafen. Klicka för att dölja dem."
                : $"{what} döljs. Klicka för att visa dem.";
        }
    }

    /// <summary>Shows the hidden outputs that can never be spent, or hides them again (the corner's mark).</summary>
    [RelayCommand]
    public void ToggleUnspendable() => ShowUnspendable(!ShowsUnspendable);

    /// <summary>Gives a node's frame at a place, as wide as the node is drawn (sketch GR5).</summary>
    /// <param name="key">The node's key.</param>
    /// <param name="place">Its top-left corner.</param>
    /// <returns>The frame.</returns>
    public GraphRect FrameAt(string key, GraphPoint place) =>
        Scene?.FrameOf(key, place) ?? GraphSizes.NodeFrame(place);

    /// <summary>Gives how wide a node is drawn (sketch GR5); the graph's width for one not drawn as a node.</summary>
    /// <param name="key">The node's key.</param>
    /// <returns>The width.</returns>
    public double WidthOf(string key) => Scene?.WidthOf(key) ?? GraphSizes.NodeWidth;

    /// <summary>
    /// Gives a line between two placed nodes that is not one of the graph's own, as a watch line or a candidate's
    /// line, from the one's port to the other's at their own widths.
    /// </summary>
    /// <param name="from">The one node's key.</param>
    /// <param name="to">The other node's key.</param>
    /// <returns>The curve, or <see langword="null"/> when either is not placed.</returns>
    public EdgeCurve? CurveBetween(string from, string to) =>
        Places.TryGetValue(from, out var start) && Places.TryGetValue(to, out var end)
            ? EdgeCurve.Between(FrameAt(from, start), FrameAt(to, end), IsHopColumns)
            : null;

    /// <summary>
    /// Expands a node at once, as a double click on it or a click on the guide's <em>Expandera</em> asks: it is chosen
    /// and expanded by one step, when it can be. A pile or a clue has its own click and is left alone.
    /// </summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns><see langword="true"/> when the node was one that expands.</returns>
    public bool ExpandAt(string? nodeKey)
    {
        if (Scene?.Node(nodeKey) is not { } node || node.Look.Sort is NodeSort.Pile or NodeSort.HeldBack)
        {
            return false;
        }

        if (SelectedNode != node.Key)
        {
            Choose(node.Key, null);
        }

        ExpandCommand.Execute(null);
        return true;
    }

    /// <summary>
    /// Opens the menu of the empty surface (a right click beside the nodes): a comment there, <em>Passa in</em> and
    /// the zoom, so that what F, + and − do can also be found without the keys.
    /// </summary>
    /// <param name="at">Where on the surface it was opened.</param>
    public void OpenSurfaceMenu(GraphPoint at)
    {
        if (Scene is null)
        {
            CloseMenu();
            return;
        }

        MenuNode = null;
        MenuRows =
        [
            new(NodeMenuAction.Comment, CommentToolText + " …", CommentKey, "Icon.Note", true),
            new(NodeMenuAction.Fit, FitText, "F", "Icon.Fit", Places.Count > 0, HasGapBefore: true),
            new(NodeMenuAction.ZoomIn, ZoomInText, "+", "Icon.Plus", true),
            new(NodeMenuAction.ZoomOut, ZoomOutText, "−", "Icon.Minus", true),
        ];
        SurfaceMenuAt = at;
    }

    /// <summary>Does what a row of the empty surface's menu says.</summary>
    /// <param name="action">The row's action.</param>
    /// <param name="at">Where the menu was opened.</param>
    /// <returns><see langword="true"/> when it was one of the surface's rows.</returns>
    private bool RunSurfaceRow(NodeMenuAction action, GraphPoint? at)
    {
        switch (action)
        {
            case NodeMenuAction.Fit:
                ViewMoveWanted?.Invoke(this, ViewMove.Fit);
                return true;
            case NodeMenuAction.ZoomIn:
                ViewMoveWanted?.Invoke(this, ViewMove.ZoomIn);
                return true;
            case NodeMenuAction.ZoomOut:
                ViewMoveWanted?.Invoke(this, ViewMove.ZoomOut);
                return true;
            case NodeMenuAction.Comment when at is { } point:
                BeginLooseComment(point);
                return true;
            default:
                return false;
        }
    }
}
