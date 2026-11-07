using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The graph (sketch GR4) for the open case: Tracing's model drawn as a free surface with a switch to hop columns,
/// the visual language of GR3a and GR3c, the amounts on the lines, the chosen tracing method, expansion one step at a
/// time and the inspector with <em>Varför?</em>. It knows nothing of WPF, so that it is tested in Chainsaw.App.Tests;
/// the view draws what it says and tells it what the pointer does.
/// </summary>
/// <remarks>
/// Everything that reads or writes the case runs off the UI thread through <see cref="IGraphSource"/> (principle 6).
/// An answer that comes back after the case has changed is dropped.
/// </remarks>
public sealed partial class GraphViewModel : ObservableObject
{
    /// <summary>What the graph says while no case is open.</summary>
    public const string NoCaseText = "Inget ärende öppet. Ctrl+N skapar ett, och /open öppnar ett.";

    /// <summary>What the graph says while the case has no start point.</summary>
    public const string NoStartPointsText =
        "Inga startpunkter än. Skriv en adress eller en tx i kommandoraden (Ctrl+K) och tryck Enter.";

    /// <summary>What the graph says while it is read for the first time.</summary>
    public const string LoadingText = "Läser grafen ur ärendet …";

    /// <summary>What the graph says when it could not be read.</summary>
    public const string LoadFailedText = "Grafen kunde inte läsas ur ärendet.";

    /// <summary>What the graph says when a place could not be stored.</summary>
    public const string SaveFailedText = "Platsen kunde inte sparas i ärendet; den gäller tills ärendet stängs.";

    /// <summary>What the graph says when an expansion failed outright.</summary>
    public const string ExpandFailedText = "Expansionen misslyckades.";

    /// <summary>What an expansion adds while no exit is connected (principle 9).</summary>
    public const string OfflineText = "Offline: ingen utgång ansluten, så bara det ärendet redan har kan visas.";

    /// <summary>The open case.</summary>
    private readonly CaseSession _session;

    /// <summary>The modules, off the UI thread.</summary>
    private readonly IGraphSource _source;

    /// <summary>The places the user has dragged nodes to in this session, per layout.</summary>
    private readonly Dictionary<(string Key, GraphLayout Layout), GraphPoint> _moved = [];

    /// <summary>Counts the graph's readings, so that an answer to an older one is dropped.</summary>
    private int _version;

    /// <summary>Stops the reading or expansion under way when the case changes.</summary>
    private CancellationTokenSource _work = new();

    /// <summary>Creates the graph for the window's open case.</summary>
    /// <param name="session">The open case.</param>
    /// <param name="workflow">The cases, which say when a start point is added.</param>
    /// <param name="source">The modules.</param>
    /// <param name="exit">The connection to the exit, for an honest offline.</param>
    /// <param name="ui">The UI thread.</param>
    public GraphViewModel(
        CaseSession session, CaseWorkflow workflow, IGraphSource source, IExitConnection exit, IUiThread ui)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(exit);
        ArgumentNullException.ThrowIfNull(ui);
        _session = session;
        _source = source;
        session.Changed += (_, _) => OnCaseChanged();
        workflow.StartPointAdded += (_, _) => ui.Post(() => _ = ReloadAsync(fit: false));
        exit.StatusChanged += (_, status) => ui.Post(() => IsOffline = status.State != ExitState.Connected);
        IsOffline = exit.Status.State != ExitState.Connected;
        OnCaseChanged();
    }

    /// <summary>Raised when a new graph is shown and the view should fit it to the screen.</summary>
    public event EventHandler? FitWanted;

    /// <summary>Raised when the places or the drawing change, so that the view draws again.</summary>
    public event EventHandler? Redraw;

    /// <summary>Raised when a <em>Varför?</em> asks for a section of the Guide.</summary>
    public event EventHandler<GuideTopic>? GuideWanted;

    /// <summary>Gets the graph ready to draw, or <see langword="null"/> when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MethodText), nameof(HasGraph), nameof(TraceText))]
    public partial GraphScene? Scene { get; private set; }

    /// <summary>Gets whether a graph with at least one actor is shown.</summary>
    public bool HasGraph => Scene is { Nodes.Count: > 0 };

    /// <summary>Gets what the page says in place of the graph; empty while a graph is shown.</summary>
    [ObservableProperty]
    public partial string EmptyText { get; private set; } = NoCaseText;

    /// <summary>Gets the layout: the free surface or the hop columns.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFree), nameof(IsHopColumns))]
    public partial GraphLayout Layout { get; private set; } = GraphLayout.Free;

    /// <summary>Gets whether the free surface is shown.</summary>
    public bool IsFree => Layout == GraphLayout.Free;

    /// <summary>Gets whether the hop columns are shown.</summary>
    public bool IsHopColumns => Layout == GraphLayout.HopColumns;

    /// <summary>Gets the tracing method the amounts are counted by.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MethodText), nameof(MethodToolTip))]
    public partial TracingMethodKind Method { get; private set; } = TracingMethodKind.Fifo;

    /// <summary>Gets the method as the button always shows it, for example <c>FIFO</c>.</summary>
    public string MethodText => GraphScene.MethodName(Method);

    /// <summary>Gets the method button's tooltip: the version, and what a click does.</summary>
    public string MethodToolTip
    {
        get
        {
            var other = GraphScene.MethodName(Other(Method));
            var used = Scene?.Snapshot.Trace?.Method.ToString() ?? MethodText;
            return $"Spårningsmetod: {used}. Klicka för {other}. Ett spårat belopp är en bedömning enligt metoden.";
        }
    }

    /// <summary>Gets what the trace started from and how much, by which method; empty without a trace.</summary>
    public string TraceText => Scene?.Snapshot.Trace is { } trace
        ? $"Spårat {AmountText.BtcWithUnit(trace.Traced)} från {trace.Seeds.Count} mynt · {trace.Method}"
        : Scene?.Snapshot.TraceProblem ?? string.Empty;

    /// <summary>Gets which amounts the lines carry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LabelsText))]
    public partial AmountLabelMode Labels { get; private set; } = AmountLabelMode.All;

    /// <summary>Gets the amounts button's text.</summary>
    public string LabelsText => Labels.Describe();

    /// <summary>Gets each node's place in the layout shown.</summary>
    public IReadOnlyDictionary<string, GraphPoint> Places { get; private set; } = new Dictionary<string, GraphPoint>();

    /// <summary>Gets each line's curve between its nodes' places.</summary>
    public IReadOnlyDictionary<string, EdgeCurve> Curves { get; private set; } = new Dictionary<string, EdgeCurve>();

    /// <summary>Gets the chosen node's key, or <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial string? SelectedNode { get; private set; }

    /// <summary>Gets the chosen line's key, or <see langword="null"/>.</summary>
    [ObservableProperty]
    public partial string? SelectedEdge { get; private set; }

    /// <summary>Gets the line under the pointer, whose amount is always written, or <see langword="null"/>.</summary>
    public string? HoveredEdge { get; private set; }

    /// <summary>Gets the node under the pointer, drawn with a lighter frame, or <see langword="null"/>.</summary>
    public string? HoveredNode { get; private set; }

    /// <summary>Gets what the inspector shows, or <see langword="null"/> when nothing is chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasInspection), nameof(CanExpand))]
    public partial InspectorContent? Inspection { get; private set; }

    /// <summary>Gets whether something is chosen.</summary>
    public bool HasInspection => Inspection is not null;

    /// <summary>Gets the answers to <em>Varför?</em> for what is chosen, the cluster's added when they come.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<WhyLine> Why { get; private set; } = [];

    /// <summary>Gets whether the answers to <em>Varför?</em> are shown.</summary>
    [ObservableProperty]
    public partial bool IsWhyOpen { get; private set; }

    /// <summary>Gets whether the legend behind the <em>?</em> is shown.</summary>
    [ObservableProperty]
    public partial bool IsLegendOpen { get; private set; }

    /// <summary>Gets whether the graph is being read.</summary>
    [ObservableProperty]
    public partial bool IsLoading { get; private set; }

    /// <summary>Gets whether an expansion runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExpand), nameof(ExpandText))]
    public partial bool IsExpanding { get; private set; }

    /// <summary>Gets whether the chosen node can be expanded now.</summary>
    public bool CanExpand => Inspection?.CanExpand == true && !IsExpanding && _session.Current is not null;

    /// <summary>Gets the expand button's text.</summary>
    public string ExpandText => IsExpanding ? "Expanderar …" : "Expandera";

    /// <summary>Gets whether no exit is connected, so that nothing new can be fetched.</summary>
    [ObservableProperty]
    public partial bool IsOffline { get; private set; }

    /// <summary>Gets a short notice: what an expansion brought, or what could not be read; empty when none.</summary>
    [ObservableProperty]
    public partial string Notice { get; private set; } = string.Empty;

    /// <summary>Gets the sorts of the legend behind the <em>?</em>.</summary>
    public IReadOnlyList<SortLook> LegendSorts => VisualLanguage.Sorts;

    /// <summary>Gets the marks of the legend behind the <em>?</em>.</summary>
    public IReadOnlyList<BadgeLook> LegendBadges => VisualLanguage.Badges;

    /// <summary>Gets what is near what is chosen, which the view draws clearly while the rest is faint.</summary>
    public (IReadOnlySet<string> Nodes, IReadOnlySet<string> Edges) Near =>
        Scene?.Near(SelectedNode, SelectedEdge) ?? (new HashSet<string>(), new HashSet<string>());

    /// <summary>Gets whether something is chosen, so that the rest is drawn faint.</summary>
    public bool IsFocused => SelectedNode is not null || SelectedEdge is not null;

    /// <summary>Shows a layout; each keeps its own places (sketch GR4).</summary>
    /// <param name="layout">The layout.</param>
    [RelayCommand]
    public void ShowLayout(GraphLayout layout)
    {
        if (Layout == layout)
        {
            return;
        }

        Layout = layout;
        Arrange();
    }

    /// <summary>Changes the tracing method between FIFO and haircut and counts the amounts again.</summary>
    /// <returns>A task that completes when the graph is shown with the new amounts.</returns>
    [RelayCommand]
    public Task ToggleMethodAsync()
    {
        Method = Other(Method);
        return ReloadAsync(fit: false);
    }

    /// <summary>Steps which amounts the lines carry: all, the traced, none.</summary>
    [RelayCommand]
    public void CycleLabels()
    {
        Labels = Labels.Next();
        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows or hides the legend behind the <em>?</em>.</summary>
    [RelayCommand]
    public void ToggleLegend() => IsLegendOpen = !IsLegendOpen;

    /// <summary>Shows or hides the answers to <em>Varför?</em>.</summary>
    [RelayCommand]
    public void ToggleWhy() => IsWhyOpen = !IsWhyOpen;

    /// <summary>Asks for a section of the Guide, from a <em>Varför?</em>.</summary>
    /// <param name="line">The answer whose section is wanted.</param>
    [RelayCommand]
    public void OpenGuide(WhyLine? line)
    {
        if (line?.Topic is { } topic)
        {
            GuideWanted?.Invoke(this, topic);
        }
    }

    /// <summary>Chooses what is under a point on the surface, or nothing over empty surface (a click).</summary>
    /// <param name="point">The point on the surface.</param>
    /// <param name="tolerance">How close to a line counts, in the surface's units.</param>
    public void ChooseAt(GraphPoint point, double tolerance)
    {
        var (node, edge) = HitTest(point, tolerance);
        Choose(node, edge);
    }

    /// <summary>Chooses a node or a line, or nothing; the inspector shows it and the rest is drawn faint.</summary>
    /// <param name="nodeKey">The node, or <see langword="null"/>.</param>
    /// <param name="edgeKey">The line, or <see langword="null"/>.</param>
    public void Choose(string? nodeKey, string? edgeKey)
    {
        var node = Scene?.Node(nodeKey);
        var edge = node is null ? Scene?.Edge(edgeKey) : null;
        SelectedNode = node?.Key;
        SelectedEdge = edge?.Key;
        IsLegendOpen = false;
        Inspect();
        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Says what is under the pointer, so that its frame lights and a line's amount shows.</summary>
    /// <param name="point">The point on the surface, or <see langword="null"/> when the pointer left the graph.</param>
    /// <param name="tolerance">How close to a line counts, in the surface's units.</param>
    public void HoverAt(GraphPoint? point, double tolerance)
    {
        var (node, edge) = point is { } at ? HitTest(at, tolerance) : (null, null);
        if (node == HoveredNode && edge == HoveredEdge)
        {
            return;
        }

        (HoveredNode, HoveredEdge) = (node, edge);
        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Gives the node or line under a point on the surface.</summary>
    /// <param name="point">The point.</param>
    /// <param name="tolerance">How close to a line counts, in the surface's units.</param>
    /// <returns>The node's or the line's key; both <see langword="null"/> over empty surface.</returns>
    public (string? Node, string? Edge) HitTest(GraphPoint point, double tolerance) =>
        Scene is { } scene ? GraphHitTest.At(scene, Places, Curves, point, tolerance) : (null, null);

    /// <summary>Moves a node while it is dragged: anywhere on the free surface, up and down in its column.</summary>
    /// <param name="nodeKey">The node.</param>
    /// <param name="start">Where it stood when the drag began.</param>
    /// <param name="wanted">Where the pointer wants it.</param>
    public void MoveNode(string nodeKey, GraphPoint start, GraphPoint wanted)
    {
        if (Scene?.Node(nodeKey) is null)
        {
            return;
        }

        _moved[(nodeKey, Layout)] = GraphPlacement.Drag(Layout, start, wanted);
        Arrange();
    }

    /// <summary>Stores where a dragged node was left, in the case, off the UI thread.</summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns>A task that completes when it is stored or could not be.</returns>
    public async Task DropNodeAsync(string nodeKey)
    {
        if (_session.Current is not { } openCase || !_moved.TryGetValue((nodeKey, Layout), out var place))
        {
            return;
        }

        try
        {
            await _source.SavePositionAsync(openCase, nodeKey, Layout, place);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The case may have closed meanwhile; the place still holds in this session.
            Notice = SaveFailedText;
        }
    }

    /// <summary>
    /// Expands the chosen node by one step through Tracing and Chains, off the UI thread; offline, the case answers
    /// for what it has and the notice says what could not be fetched.
    /// </summary>
    /// <returns>A task that completes when the graph is shown after the expansion.</returns>
    [RelayCommand]
    public async Task ExpandAsync()
    {
        if (!CanExpand || _session.Current is not { } openCase || Inspection is not { } inspection)
        {
            return;
        }

        var version = _version;
        var token = _work.Token;
        IsExpanding = true;
        try
        {
            var result = await _source.ExpandAsync(openCase, inspection.Expansion, Method, token);
            if (version == _version)
            {
                Show(result.Scene, fit: false);
                Notice = ExpansionNotice(result, IsOffline);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (version == _version)
            {
                Notice = ExpandFailedText;
            }
        }
        finally
        {
            IsExpanding = false;
        }
    }

    /// <summary>Reads the graph anew from the case, off the UI thread.</summary>
    /// <param name="fit">Whether the view should fit the new graph to the screen.</param>
    /// <returns>A task that completes when the graph is shown or could not be read.</returns>
    public async Task ReloadAsync(bool fit)
    {
        _work.Cancel();
        _work.Dispose();
        _work = new CancellationTokenSource();
        var version = ++_version;
        if (_session.Current is not { } openCase)
        {
            Clear(NoCaseText);
            return;
        }

        IsLoading = true;
        if (Scene is null)
        {
            EmptyText = LoadingText;
        }

        try
        {
            var scene = await _source.LoadAsync(openCase, Method, _work.Token);
            if (version == _version)
            {
                Show(scene, fit);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (version == _version && exception is not OperationCanceledException)
            {
                Clear(LoadFailedText);
            }
        }
        finally
        {
            if (version == _version)
            {
                IsLoading = false;
            }
        }
    }

    /// <summary>Says what an expansion brought, honestly, also offline.</summary>
    /// <param name="result">The expansion.</param>
    /// <param name="offline">Whether no exit is connected.</param>
    /// <returns>The notice.</returns>
    public static string ExpansionNotice(GraphExpansion result, bool offline)
    {
        ArgumentNullException.ThrowIfNull(result);
        var problems = string.Join(" ", result.Problems);
        var text = result.Outcome switch
        {
            ExpansionOutcome.Expanded when result.NewTransactions == 0 => "Inget nytt att hämta.",
            ExpansionOutcome.Expanded => $"{result.NewTransactions} nya tx i grafen.",
            ExpansionOutcome.Partly => $"Delvis expanderad: {result.NewTransactions} nya tx. {problems}",
            _ => $"Inte expanderad. {problems}",
        };
        return (offline && result.Outcome != ExpansionOutcome.Expanded ? $"{text} {OfflineText}" : text).Trim();
    }

    /// <summary>Gives the other of the two methods.</summary>
    /// <param name="method">The method.</param>
    /// <returns>Haircut for FIFO and FIFO for haircut.</returns>
    private static TracingMethodKind Other(TracingMethodKind method) =>
        method == TracingMethodKind.Fifo ? TracingMethodKind.Haircut : TracingMethodKind.Fifo;

    /// <summary>Starts over for the case now open: nothing chosen, nothing moved, and the graph read anew.</summary>
    private void OnCaseChanged()
    {
        _moved.Clear();
        Scene = null;
        SelectedNode = null;
        SelectedEdge = null;
        Inspect();
        Notice = string.Empty;
        _ = ReloadAsync(fit: true);
    }

    /// <summary>Shows a new graph, keeping what is chosen when it is still there.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="fit">Whether the view should fit it to the screen.</param>
    private void Show(GraphScene scene, bool fit)
    {
        Scene = scene;
        EmptyText = scene.Snapshot.Graph.StartPoints.Count == 0 && scene.Nodes.Count == 0 ? NoStartPointsText : "";
        Notice = string.Join(" ", scene.Snapshot.Graph.Problems);
        SelectedNode = scene.Node(SelectedNode)?.Key;
        SelectedEdge = scene.Edge(SelectedEdge)?.Key;
        Arrange();
        Inspect();
        OnPropertyChanged(nameof(MethodToolTip));
        if (fit)
        {
            FitWanted?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Shows no graph, with a text in its place.</summary>
    /// <param name="text">The text.</param>
    private void Clear(string text)
    {
        Scene = null;
        EmptyText = text;
        IsLoading = false;
        Arrange();
        Inspect();
    }

    /// <summary>Works out every node's place and every line's curve in the layout shown, and draws again.</summary>
    private void Arrange()
    {
        if (Scene is { } scene)
        {
            Places = GraphPlacement.Place(scene.Snapshot.Graph.Nodes, Layout, scene.Columns, _moved);
            Curves = GraphHitTest.Curves(scene, Places, IsHopColumns);
        }
        else
        {
            Places = new Dictionary<string, GraphPoint>();
            Curves = new Dictionary<string, EdgeCurve>();
        }

        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Fills the inspector for what is chosen, and asks Clustering about a chosen cluster.</summary>
    private void Inspect()
    {
        var scene = Scene;
        var node = scene?.Node(SelectedNode);
        var edge = scene?.Edge(SelectedEdge);
        Inspection = (scene, node, edge) switch
        {
            ({ } s, { } n, _) => InspectorContent.ForNode(s, n),
            ({ } s, null, { } e) => InspectorContent.ForEdge(s, e),
            _ => null,
        };
        Why = Inspection?.Why ?? [];
        if (node is { Node.Kind: GraphNodeKind.Cluster } && _session.Current is { } openCase)
        {
            _ = ExplainClusterAsync(openCase, node);
        }
    }

    /// <summary>Adds Clustering's answers for a chosen cluster when they come, if it is still chosen.</summary>
    /// <param name="openCase">The open case.</param>
    /// <param name="node">The cluster.</param>
    /// <returns>A task that completes when the answers are shown or could not be read.</returns>
    private async Task ExplainClusterAsync(ICase openCase, NodeView node)
    {
        var version = _version;
        try
        {
            var lines = await _source.ExplainClusterAsync(openCase, node.Node, _work.Token);
            if (version == _version && SelectedNode == node.Key && Inspection is { } shown)
            {
                Why = [.. shown.Why, .. lines];
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The case may have closed; the inspector keeps what it has.
        }
    }
}
