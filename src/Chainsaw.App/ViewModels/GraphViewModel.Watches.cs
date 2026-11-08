using Chainsaw.App.Graph;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>What a row of the node's menu does (sketch BV2a: right click on a node).</summary>
public enum NodeMenuAction
{
    /// <summary>Etikett … (L).</summary>
    Label,

    /// <summary>Bryt upp … (U).</summary>
    Split,

    /// <summary>Annan kedja … (K).</summary>
    CrossChain,

    /// <summary>Bevaka … (B).</summary>
    Watch,

    /// <summary>Expandera (E).</summary>
    Expand,

    /// <summary>Kopiera adress (Ctrl+C).</summary>
    Copy,

    /// <summary>Lägg tillbaka i högen: an actor picked out of a pile goes back into it (sketch EX2b).</summary>
    ReturnToPile,

    /// <summary>
    /// Fråga underleverantörerna (R): asks the suppliers of attribution that are on, Chainabuse first, about the
    /// node's address (the user's decision 2026-11-08: a button for now, automatic later).
    /// </summary>
    AskSuppliers,
}

/// <summary>A row of the node's menu.</summary>
/// <param name="Action">What it does.</param>
/// <param name="Label">What it says.</param>
/// <param name="Key">Its key.</param>
/// <param name="Icon">Its icon's resource key.</param>
/// <param name="IsEnabled">Whether it can be chosen for the node.</param>
/// <param name="HasGapBefore">Whether a line stands above it.</param>
public sealed record NodeMenuRow(
    NodeMenuAction Action, string Label, string Key, string Icon, bool IsEnabled, bool HasGapBefore = false);

/// <summary>
/// The graph's part of the watches (sketch BV2a): the node's menu, the form, the eyes and watch lines.
/// </summary>
public sealed partial class GraphViewModel
{
    /// <summary>The watches, or <see langword="null"/> when the graph is built without them.</summary>
    private WatchBoard? _watches;

    /// <summary>Raised when the user wants a node's address copied (the view owns the clipboard).</summary>
    public event EventHandler<string>? CopyWanted;

    /// <summary>Raised when the graph has something to say as a toast (sketch TO1a).</summary>
    public event EventHandler<ToastRequest>? ToastWanted;

    /// <summary>Gets the watch form, shared with the view Bevakning; <see langword="null"/> without watches.</summary>
    public WatchFormViewModel? WatchForm { get; private set; }

    /// <summary>Gets what the graph draws for the watches.</summary>
    public GraphWatchOverlay WatchOverlay { get; private set; } = GraphWatchOverlay.Empty;

    /// <summary>Gets the node whose menu is open, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMenuOpen))]
    public partial string? MenuNode { get; private set; }

    /// <summary>Gets the rows of the open menu.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<NodeMenuRow> MenuRows { get; private set; } = [];

    /// <summary>
    /// Gets the chosen node's actions as icons over it (sketch IN2b), in the sketch's order: only those that apply to the
    /// node, since an action that cannot be used is noise (principle 7); empty when no node is chosen.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNodeTools))]
    public partial IReadOnlyList<NodeMenuRow> NodeTools { get; private set; } = [];

    /// <summary>Gets whether the chosen node has its actions over it.</summary>
    public bool HasNodeTools => NodeTools.Count > 0;

    /// <summary>Gives the chosen node's actions over it again, after what they depend on changed.</summary>
    internal void ShowNodeTools()
    {
        if (Scene?.Node(SelectedNode) is not { } node || _session.Current is null)
        {
            NodeTools = [];
            return;
        }

        var tools = new List<NodeMenuRow>();
        if (Inspection?.CanExpand == true)
        {
            tools.Add(new(NodeMenuAction.Expand, "Expandera", "E", "Icon.Plus", CanExpand));
        }

        Add(Forms.CanLabel, NodeMenuAction.Label, "Etikett", "L", "Icon.Tag");
        Add(CanWatch(node), NodeMenuAction.Watch, "Bevaka", "B", "Icon.Eye");
        Add(Forms.CanSplit, NodeMenuAction.Split, "Bryt upp", "U", "Icon.Split");
        Add(Forms.CanCrossChain, NodeMenuAction.CrossChain, "Annan kedja", "K", "Icon.Link");
        Add(CanAskSuppliers(node), NodeMenuAction.AskSuppliers, SuppliersToolText, "R", "Icon.Shield");
        Add(node.Node.Addresses.Count > 0, NodeMenuAction.Copy, "Kopiera adress", "Ctrl+C", "Icon.Copy");
        Add(CanReturnToPile, NodeMenuAction.ReturnToPile, "Lägg tillbaka i högen", "", "Icon.Layers");
        NodeTools = tools;

        void Add(bool applies, NodeMenuAction action, string label, string key, string icon)
        {
            if (applies)
            {
                tools.Add(new(action, label, key, icon, true));
            }
        }
    }

    /// <summary>Gets whether the node's menu is open.</summary>
    public bool IsMenuOpen => MenuNode is not null;

    /// <summary>Gets whether the watch form is open beside the graph.</summary>
    public bool IsWatchFormOpen => WatchForm?.IsOpen == true;

    /// <summary>Connects the graph to the watches: the form, and the overlay that follows them.</summary>
    /// <param name="watches">The watches.</param>
    /// <param name="form">The form, shared with the view Bevakning.</param>
    public void FollowWatches(WatchBoard watches, WatchFormViewModel form)
    {
        ArgumentNullException.ThrowIfNull(watches);
        ArgumentNullException.ThrowIfNull(form);
        _watches = watches;
        WatchForm = form;
        History.Watches = watches;
        watches.Changed += (_, _) => ShowWatches();
        form.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(WatchFormViewModel.IsOpen))
            {
                OnPropertyChanged(nameof(IsWatchFormOpen));
            }
        };
        OnPropertyChanged(nameof(WatchForm));
        ShowWatches();
    }

    /// <summary>
    /// Opens the node's menu (right click): the node is chosen, and the menu's rows follow what it is.
    /// </summary>
    /// <param name="nodeKey">The node.</param>
    public void OpenMenu(string? nodeKey)
    {
        if (Scene?.Node(nodeKey) is not { } node)
        {
            CloseMenu();
            return;
        }

        Choose(node.Key, null);
        var hasAddress = node.Node.Addresses.Count > 0;
        MenuRows =
        [
            new(NodeMenuAction.Label, "Etikett …", "L", "Icon.Tag", Forms.CanLabel),
            new(NodeMenuAction.Split, "Bryt upp …", "U", "Icon.Split", Forms.CanSplit),
            new(NodeMenuAction.CrossChain, "Annan kedja …", "K", "Icon.Link", Forms.CanCrossChain),
            new(NodeMenuAction.Watch, "Bevaka …", "B", "Icon.Eye", CanWatch(node)),
            new(NodeMenuAction.Expand, "Expandera", "E", "Icon.Plus", CanExpand, HasGapBefore: true),
            new(NodeMenuAction.Copy, "Kopiera adress", "Ctrl+C", "Icon.Copy", hasAddress),
        ];
        MenuNode = node.Key;
    }

    /// <summary>Closes the node's menu.</summary>
    [RelayCommand]
    public void CloseMenu() => MenuNode = null;

    /// <summary>Does what a row of the menu says, for the chosen node, and closes the menu.</summary>
    /// <param name="row">The row.</param>
    [RelayCommand]
    public void RunMenu(NodeMenuRow? row)
    {
        CloseMenu();
        if (row is not { IsEnabled: true })
        {
            return;
        }

        switch (row.Action)
        {
            case NodeMenuAction.Label:
                Forms.Open(InspectorForm.Label);
                break;
            case NodeMenuAction.Split:
                Forms.Open(InspectorForm.Split);
                break;
            case NodeMenuAction.CrossChain:
                Forms.Open(InspectorForm.CrossChain);
                break;
            case NodeMenuAction.Watch:
                OpenWatch();
                break;
            case NodeMenuAction.Expand:
                ExpandCommand.Execute(null);
                break;
            case NodeMenuAction.ReturnToPile:
                _ = ReturnToPileAsync();
                break;
            case NodeMenuAction.AskSuppliers:
                _ = AskSuppliersAsync();
                break;
            default:
                CopyAddress();
                break;
        }
    }

    /// <summary>Copies a text from the inspector: an address, a txid or a block, in full.</summary>
    /// <param name="text">The text.</param>
    [RelayCommand]
    private void CopyText(string? text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            CopyWanted?.Invoke(this, text);
        }
    }

    /// <summary>Copies the chosen node's address (Ctrl+C).</summary>
    public void CopyAddress()
    {
        if (Scene?.Node(SelectedNode) is { } node && node.Node.Addresses.Count > 0)
        {
            CopyWanted?.Invoke(this, node.Node.Addresses[0].Value);
        }
    }

    /// <summary>Opens <em>Bevaka …</em> for the chosen node (B), when it can be watched.</summary>
    public void OpenWatch()
    {
        if (WatchForm is not { } form || Scene?.Node(SelectedNode) is not { } node || !CanWatch(node))
        {
            return;
        }

        Forms.Cancel();
        var kind = node.Node.Kind == GraphNodeKind.Cluster ? WatchSubjectKind.Cluster : WatchSubjectKind.Address;
        form.OpenFor(new WatchSubject(kind, node.Node.Addresses[0], node.Name), NameOf, node.Node.Addresses);
    }

    /// <summary>
    /// Adds a node to the open watch form's counterparties (Ctrl+click in the graph); the form takes the click before
    /// the own areas do.
    /// </summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns><see langword="true"/> when the form took it.</returns>
    public bool PickForWatch(string? nodeKey)
    {
        if (WatchForm is not { IsOpen: true } form || Scene?.Node(nodeKey) is not { } node)
        {
            return false;
        }

        // The click is the form's either way; a node without an address on the chain (a tx, a coin) is refused there.
        form.Add(node.Node.Addresses, node.Name);
        return true;
    }

    /// <summary>Shows a watch's node in the graph (Öppna i grafen): it is chosen and fitted to the screen.</summary>
    /// <param name="watch">The watch.</param>
    /// <returns><see langword="true"/> when its node is in the graph.</returns>
    public bool ShowWatch(Watch watch)
    {
        ArgumentNullException.ThrowIfNull(watch);
        var node = Scene?.Nodes.FirstOrDefault(each => each.Node.Addresses.Contains(watch.Subject.Anchor));
        if (node is null)
        {
            Say("Bevakningens nod finns inte i grafen.");
            return false;
        }

        var parties = watch.Filter.Receivers.Concat(watch.Filter.Senders).ToHashSet();
        var keys = Scene!.Nodes.Where(each => each.Node.Addresses.Any(parties.Contains)).Select(each => each.Key)
            .Append(node.Key).ToList();
        Choose(node.Key, null);
        ShowNodesWanted?.Invoke(this, keys);
        return true;
    }

    /// <summary>Says in the graph's notice that a watch found money moving (sketch BV2a's toast).</summary>
    /// <param name="hits">The new hits.</param>
    public void TellHits(IReadOnlyList<WatchHit> hits)
    {
        ArgumentNullException.ThrowIfNull(hits);
        if (hits.Count == 0)
        {
            return;
        }

        var first = WatchWords.Hit(hits[0], NameOf);
        Say(hits.Count == 1
            ? $"Bevakning slog till · {first}"
            : $"Bevakning slog till · {first} och {hits.Count - 1} till");
    }

    /// <summary>Names an address by its node in the graph, or shortened.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The name.</returns>
    public string NameOf(ChainAddress address) => NameIn(address) ?? WatchWords.Short(address);

    /// <summary>Names an address by its node in the graph.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The node's name, or <see langword="null"/> when it is not in the graph.</returns>
    public string? NameIn(ChainAddress address) => NodeIn(address)?.Name;

    /// <summary>Gives the look of an address's node in the graph: its sort's icon, colour and shape (sketch GR3c).</summary>
    /// <param name="address">The address.</param>
    /// <returns>The node's look, or <see langword="null"/> when it is not in the graph.</returns>
    public SortLook? LookIn(ChainAddress address) => NodeIn(address)?.Look;

    /// <summary>Finds the node in the graph that holds an address.</summary>
    /// <param name="address">The address.</param>
    /// <returns>The node, or <see langword="null"/> when it is not in the graph.</returns>
    private NodeView? NodeIn(ChainAddress address) =>
        Scene?.Nodes.FirstOrDefault(node => node.Node.Addresses.Contains(address));

    /// <summary>
    /// Says whether a node can be watched: an address or a cluster on Bitcoin or Ethereum, with a case.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    private bool CanWatch(NodeView node) =>
        WatchForm is not null && _session.Current is not null
        && node.Node.Kind is GraphNodeKind.Address or GraphNodeKind.Cluster
        && node.Node.Addresses.Count > 0
        && node.Node.Chain is Chain.Bitcoin or Chain.Ethereum;

    /// <summary>
    /// Closes the node's menu and the watch form when another case opens, so that nothing is saved there.
    /// </summary>
    private void ForgetWatchForms()
    {
        MenuNode = null;
        WatchForm?.Cancel();
    }

    /// <summary>Works out the overlay anew and draws.</summary>
    private void ShowWatches()
    {
        WatchOverlay = _watches is { } board
            ? GraphWatches.Build(Scene, board.Snapshot, board.Gate)
            : GraphWatchOverlay.Empty;
        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Notes the hits of a chosen node's watches as seen, so that their rings stand still.</summary>
    /// <param name="nodeKey">The chosen node.</param>
    private void SeeWatches(string? nodeKey)
    {
        if (_watches is not { } board || nodeKey is null)
        {
            return;
        }

        var touched = WatchOverlay.Lines.Where(line => line.IsNew && (line.From == nodeKey || line.To == nodeKey));
        foreach (var line in touched)
        {
            _ = board.MarkSeenAsync(line.WatchId);
        }

        if (WatchOverlay.MarkOf(nodeKey) is { HasNew: true } && Scene?.Node(nodeKey) is { } node)
        {
            var mine = board.Snapshot.Watches.Where(watch => node.Node.Addresses.Contains(watch.Subject.Anchor));
            foreach (var watch in mine)
            {
                _ = board.MarkSeenAsync(watch.Id);
            }
        }
    }
}
