using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.App.Navigation;
using Chainsaw.App.Settings;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
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
        $"Inga startpunkter än. Skriv en adress eller en tx i kommandoraden ({ViewTable.CommandGesture}) och tryck Enter.";

    /// <summary>What the graph says while it is read for the first time.</summary>
    public const string LoadingText = "Läser grafen ur ärendet …";

    /// <summary>What the graph says when it could not be read.</summary>
    public const string LoadFailedText = "Grafen kunde inte läsas ur ärendet.";

    /// <summary>What the graph says when the method, layout, amounts or zoom could not be stored.</summary>
    public const string ViewSaveFailedText =
        "Grafens läge kunde inte sparas i ärendet; det gäller tills ärendet stängs.";

    /// <summary>What the graph says when a place could not be stored.</summary>
    public const string SaveFailedText = "Platsen kunde inte sparas i ärendet; den gäller tills ärendet stängs.";

    /// <summary>What the graph says when an expansion failed outright.</summary>
    public const string ExpandFailedText = "Expansionen misslyckades.";

    /// <summary>The action on a toast that tries a failed step again.</summary>
    public const string RetryText = "Försök igen";

    /// <summary>What an expansion adds while no exit is connected (principle 9): the shell's one offline text.</summary>
    public const string OfflineText = OfflineWords.NoExitText;

    /// <summary>What the graph says when Bitcoin's sources are off in the settings (principle 9, sketch KL1a).</summary>
    public const string SourcesOffText =
        "Av: källorna för Bitcoin är avstängda i inställningarna, så bara det ärendet redan har kan visas.";

    /// <summary>The open case.</summary>
    private readonly CaseSession _session;

    /// <summary>The modules, off the UI thread.</summary>
    private readonly IGraphSource _source;

    /// <summary>The clock, which says when <em>now</em> is for what is not confirmed yet.</summary>
    private readonly IClock _clock;

    /// <summary>The places the user has dragged nodes to in this session, per layout.</summary>
    private readonly Dictionary<(string Key, GraphLayout Layout), GraphPoint> _moved = [];

    /// <summary>
    /// Counts the graph's readings and expansions, so that an answer to an older one is dropped.
    /// </summary>
    private int _version;

    /// <summary>The graph's problems as last told by a toast, so that the same are not told at every reading.</summary>
    private string _toldProblems = string.Empty;

    /// <summary>Whether a reading was asked for while an expansion ran, to be made when it is done.</summary>
    private bool _refreshAfterExpansion;

    /// <summary>
    /// Where each node stood on the free surface when it first appeared, so that it stays there when the graph grows
    /// (sketch GR4: laid out once, then where the user leaves it).
    /// </summary>
    private readonly Dictionary<string, GraphPoint> _firstFree = new(StringComparer.Ordinal);

    /// <summary>Stops the reading or expansion under way when the case changes.</summary>
    private CancellationTokenSource _work = new();

    /// <summary>The part of the surface on screen, once the user has panned or zoomed; stored with the view.</summary>
    private Viewport? _viewport;

    /// <summary>The storing of the view under way, so that stores are made one at a time.</summary>
    private Task _savingView = Task.CompletedTask;

    /// <summary>Whether the view changed while it was being stored, so that it is stored once more.</summary>
    private bool _viewChangedMeanwhile;

    /// <summary>Whether the open case's stored view has been read and shown, so that changes may be stored.</summary>
    private bool _viewLoaded;

    /// <summary>
    /// Whether something is being chosen, while the inspector is filled: the choice tells the Guide's panel itself, as
    /// a choice, rather than as changed answers.
    /// </summary>
    private bool _choosing;

    /// <summary>Creates the graph for the window's open case.</summary>
    /// <param name="session">The open case.</param>
    /// <param name="workflow">The cases, which say when a start point is added.</param>
    /// <param name="source">The modules.</param>
    /// <param name="exit">The way out (VPN or Windows) and the exit, for an honest offline.</param>
    /// <param name="ui">The UI thread.</param>
    /// <param name="editor">Where the inspector's forms write in the case (sketch IN1a).</param>
    /// <param name="clock">The clock.</param>
    /// <param name="checks">
    /// The checks, suggestions and own areas (sketches KF1a and KF1b); none when the graph is built without them.
    /// </param>
    /// <param name="suppliers">The suppliers of attribution (Chainabuse first); none where they are not wired.</param>
    /// <param name="switches">Which suppliers are on, from the page Källor; none where they are not wired.</param>
    /// <param name="comments">The graph's comments (sketch KN1a); none where they are not wired.</param>
    /// <param name="trash">The graph's trash (sketch SO1a); none where it is not wired, and nothing can be thrown.</param>
    /// <param name="caseTrash">
    /// The cases thrown on the start page, shared with the window; a trash in memory when none is given.
    /// </param>
    public GraphViewModel(
        CaseSession session,
        CaseWorkflow workflow,
        IGraphSource source,
        ExitWay exit,
        IUiThread ui,
        IGraphEditor editor,
        IClock clock,
        ICheckSource? checks = null,
        IAttributionProviders? suppliers = null,
        AttributionSwitches? switches = null,
        IGraphComments? comments = null,
        ITrashSource? trash = null,
        CaseTrash? caseTrash = null)
    {
        _suppliers = suppliers;
        _switches = switches;
        if (switches is not null)
        {
            // A supplier switched on or off on the page Källor shows or hides its icon over the chosen node.
            switches.Changed += (_, _) => ui.Post(ShowNodeTools);
        }
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(exit);
        ArgumentNullException.ThrowIfNull(ui);
        _session = session;
        _source = source;
        _clock = clock;
        Start = new GraphStartViewModel(session, workflow, ui, clock);
        Start.GuideWanted += (_, request) => GuideWanted?.Invoke(this, request);
        OnEmptyTextChanged(EmptyText);
        Checks = new ChecksViewModel(session, checks ?? NoCheckSource.Instance);
        Checks.Changed += (_, _) => Redraw?.Invoke(this, EventArgs.Empty);
        Checks.GuideWanted += (_, request) => GuideWanted?.Invoke(this, request);
        Checks.ShowWanted += (_, nodes) => ShowNodesWanted?.Invoke(this, nodes);
        Checks.ToastWanted += (_, toast) => ToastWanted?.Invoke(this, toast);
        Checks.ClustersChanged += (_, _) => _ = RefreshAsync();
        Comments = new CommentsViewModel(session, comments ?? NoGraphComments.Instance);
        WireComments();
        WireTidy();
        Forms = new InspectorFormsViewModel(session, editor, clock);
        Forms.Saved += (_, _) => _ = RefreshAsync();
        Forms.Failed += (_, text) => Tell(ToastKind.Error, text);
        History = new InspectorHistoryViewModel(session, editor);
        History.Saved += (_, notice) => _ = RefreshThenSayAsync(notice);
        History.Failed += (_, text) => Tell(ToastKind.Error, text);

        // The node's actions over it follow the chosen node, its facts, the expansion and the forms (sketch IN2b).
        Forms.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(InspectorFormsViewModel.CanLabel) or nameof(InspectorFormsViewModel.CanSplit)
                or nameof(InspectorFormsViewModel.CanCrossChain))
            {
                ShowNodeTools();
            }
        };
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(SelectedNode) or nameof(Inspection) or nameof(IsExpanding) or nameof(Scene)
                or nameof(WatchOverlay))
            {
                ShowNodeTools();
            }
        };
        UseTrash(trash, caseTrash ?? new CaseTrash(clock));
        session.Changed += (_, _) => OnCaseChanged();
        workflow.StartPointAdded += (_, _) => ui.Post(() =>
        {
            // A start point added again may be one in the trash; it is told again, with Lägg tillbaka.
            _toldThrownStarts = string.Empty;
            _ = RefreshAsync();
        });
        exit.Changed += (_, _) => ShowWay(exit);
        ShowWay(exit);
        OnCaseChanged();
    }

    /// <summary>Raised when a new graph is shown and the view should fit it to the screen.</summary>
    public event EventHandler? FitWanted;

    /// <summary>Raised when a check asks the view to fit its actors to the screen.</summary>
    public event EventHandler<IReadOnlyList<string>>? ShowNodesWanted;

    /// <summary>Raised when the places or the drawing change, so that the view draws again.</summary>
    public event EventHandler? Redraw;

    /// <summary>
    /// Raised when <em>Varför?</em>, the <em>?</em> or the method asks for the Guide (sketch GD1b); a request about
    /// what is chosen carries its <see cref="GuideRequest.Subject"/>.
    /// </summary>
    public event EventHandler<GuideRequest>? GuideWanted;

    /// <summary>
    /// Raised when the answers to <em>Varför?</em> change: something else is chosen, or a cluster's answers came. The
    /// request is <see langword="null"/> when nothing is chosen. The open Guide panel follows it.
    /// </summary>
    public event EventHandler<GuideRequest?>? WhyChanged;

    /// <summary>
    /// Raised when another case, or none, is shown, so that the Guide's panel closes and no answer about the former
    /// case stays on screen (principle 4).
    /// </summary>
    public event EventHandler? CaseChanged;

    /// <summary>Gets the graph ready to draw, or <see langword="null"/> when there is none.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(MethodText), nameof(TraceText), nameof(HasUnspendable), nameof(UnspendableToolTip),
        nameof(ShowsUnspendable))]
    public partial GraphScene? Scene { get; private set; }

    /// <summary>
    /// Gets whether a case is open. Without one there is nothing to inspect, so the inspector is not shown.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInspectorShown), nameof(ShowsCorner))]
    public partial bool HasCase { get; private set; }

    /// <summary>Gets what the page says in place of the graph; empty while a graph is shown.</summary>
    [ObservableProperty]
    public partial string EmptyText { get; private set; } = NoCaseText;

    /// <summary>
    /// Gets the empty graph that guides (sketch GR6c): with no case and in a case without a start point it stands in
    /// place of <see cref="EmptyText"/>.
    /// </summary>
    public GraphStartViewModel Start { get; }

    /// <summary>
    /// Gets the part of the surface on screen that the case had stored, to be shown instead of fitting the graph the
    /// first time it is shown after the case opened; <see langword="null"/> when there is none.
    /// </summary>
    public Viewport? StoredViewport { get; private set; }

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

    /// <summary>The key that opens the Guide at the method, besides a right click on it (Windows' Shift+F1).</summary>
    public const string MethodGuideGesture = "Shift+F1";

    /// <summary>Gets the method button's tooltip: the version, and what a click does.</summary>
    public string MethodToolTip
    {
        get
        {
            var other = GraphScene.MethodName(Other(Method));
            var used = Scene?.Snapshot.Trace?.Method.ToString() ?? MethodText;
            return $"Spårningsmetod: {used}. Klicka för {other}, högerklicka eller {MethodGuideGesture} för "
                + "Guiden. Ett spårat belopp är en bedömning enligt metoden.";
        }
    }

    /// <summary>Gets what the trace started from and how much, by which method; empty without a trace.</summary>
    public string TraceText => Scene?.Snapshot.Trace is { } trace
        ? $"Spårat {AmountText.WithUnit(trace.Traced)} från {trace.Seeds.Count} mynt · {trace.Method}"
        : Scene?.Snapshot.TraceProblem ?? string.Empty;

    /// <summary>Gets which amounts the lines carry.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LabelsText), nameof(LabelsToolTip), nameof(ShowsNoAmounts))]
    public partial AmountLabelMode Labels { get; private set; } = AmountLabelMode.All;

    /// <summary>Gets the amounts icon's tip: which amounts the lines carry, and that a click changes it.</summary>
    public string LabelsToolTip => $"Belopp på linjerna: {Labels.Describe()} (klicka för att byta)";

    /// <summary>Gets whether the lines carry no amounts, so that the amounts icon is toned down.</summary>
    public bool ShowsNoAmounts => Labels == AmountLabelMode.None;

    /// <summary>Gets the amounts button's text.</summary>
    public string LabelsText => Labels.Describe();

    /// <summary>Gets each node's place in the layout shown.</summary>
    public IReadOnlyDictionary<string, GraphPoint> Places { get; private set; } = new Dictionary<string, GraphPoint>();

    /// <summary>Gets each line's curve between its nodes' places.</summary>
    public IReadOnlyDictionary<string, EdgeCurve> Curves { get; private set; } = new Dictionary<string, EdgeCurve>();

    /// <summary>
    /// Gets where each line's amount is written (<see cref="AmountLayout"/>); a line that is missing has no free place
    /// and shows its amount on hovering.
    /// </summary>
    public IReadOnlyDictionary<string, AmountSpot> Amounts { get; private set; } =
        new Dictionary<string, AmountSpot>();

    /// <summary>Gets the chosen node's key, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInspectorShown))]
    public partial string? SelectedNode { get; private set; }

    /// <summary>Gets the chosen line's key, or <see langword="null"/>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInspectorShown))]
    public partial string? SelectedEdge { get; private set; }

    /// <summary>
    /// Gets whether the inspector's column is shown: only when there is something in it, a chosen actor or line, or
    /// the checks and suggestions that open where it stands (sketch KF1a). An empty panel is removed (principle 7,
    /// the user's decision 2026-11-08).
    /// </summary>
    public bool IsInspectorShown => HasCase && (SelectedNode is not null || SelectedEdge is not null || Checks.IsOpen);

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

    /// <summary>
    /// Gets the inspector's forms (sketch IN1a): <em>Etikett</em>, <em>Bryt upp</em> and <em>Annan kedja</em> for the
    /// chosen node; what they store makes the graph be read again from the case.
    /// </summary>
    public InspectorFormsViewModel Forms { get; }

    /// <summary>Gets the inspector's tabs and its history, with taking back and undoing (sketch AN1b).</summary>
    public InspectorHistoryViewModel History { get; }

    /// <summary>
    /// Gets the checks and suggestions (sketch KF1a, Ctrl+J) and the areas behind the actors with the own areas
    /// (sketch KF1b, Z), run in the background on the graph shown.
    /// </summary>
    public ChecksViewModel Checks { get; }

    /// <summary>Gets the answers to <em>Varför?</em> for what is chosen, the cluster's added when they come.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<WhyLine> Why { get; private set; } = [];

    /// <summary>Gets whether the legend behind the <em>?</em> is shown.</summary>
    [ObservableProperty]
    public partial bool IsLegendOpen { get; private set; }

    /// <summary>Gets whether an expansion runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanExpand), nameof(ExpandText))]
    [NotifyCanExecuteChangedFor(nameof(ToggleMethodCommand))]
    public partial bool IsExpanding { get; private set; }

    /// <summary>Gets whether the chosen node can be expanded now.</summary>
    public bool CanExpand => Inspection?.CanExpand == true && !IsExpanding && _session.Current is not null;

    /// <summary>Gets the expand button's text.</summary>
    public string ExpandText => IsExpanding ? "Expanderar …" : "Expandera";

    /// <summary>Gets whether no exit is connected, so that nothing new can be fetched.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsOffline))]
    public partial bool IsOffline { get; private set; }

    /// <summary>Gets whether Bitcoin's sources are off in the settings (Av, sketch KL1a).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowsOffline), nameof(OfflineWord), nameof(OfflineTip))]
    public partial bool IsSourcesOff { get; private set; }

    /// <summary>Gets whether the graph's corner says that nothing new can be fetched.</summary>
    public bool ShowsOffline => IsOffline || IsSourcesOff;

    /// <summary>Gets the corner's word: <em>Av</em> when the sources are off, else <em>Offline</em>.</summary>
    public string OfflineWord => IsSourcesOff ? "Av" : "Offline";

    /// <summary>Gets what the corner's word means, on hovering.</summary>
    public string OfflineTip => IsSourcesOff ? SourcesOffText : OfflineNote;

    /// <summary>
    /// Gets what Chainsaw says while offline, as the way out says it (<see cref="ExitWay.Offline"/>): no exit, no
    /// network through Windows, or the tunnel being connected again.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OfflineTip))]
    public partial string OfflineNote { get; private set; } = OfflineText;

    /// <summary>Gets a short notice: what an expansion brought, or what could not be read; empty when none.</summary>
    [ObservableProperty]
    public partial string Notice { get; private set; } = string.Empty;

    /// <summary>Gets the legend behind the <em>?</em> (sketch GR7a).</summary>
    public LegendViewModel Legend { get; } = new();

    /// <summary>Gets what is near what is chosen, which the view draws clearly while the rest is faint.</summary>
    /// <remarks>While the list of checks is open, the chosen row's actors are near instead (sketch KF1a).</remarks>
    public (IReadOnlySet<string> Nodes, IReadOnlySet<string> Edges) Near
    {
        get
        {
            if (TrashWarning is { } warning && Scene is { } shown)
            {
                // What would follow with a throw stands out while the rest is toned down (sketch SO1a).
                var keys = warning.Keys;
                return (keys, shown.Edges
                    .Where(edge => keys.Contains(edge.Edge.From) && keys.Contains(edge.Edge.To))
                    .Select(edge => edge.Key)
                    .ToHashSet(StringComparer.Ordinal));
            }

            if (Checks.Focus is { } focus && Scene is { } scene)
            {
                return (focus, scene.Edges
                    .Where(edge => focus.Contains(edge.Edge.From) && focus.Contains(edge.Edge.To))
                    .Select(edge => edge.Key)
                    .ToHashSet(StringComparer.Ordinal));
            }

            return Scene?.Near(SelectedNode, SelectedEdge) ?? (new HashSet<string>(), new HashSet<string>());
        }
    }

    /// <summary>Gets whether something is chosen, so that the rest is drawn faint.</summary>
    public bool IsFocused =>
        SelectedNode is not null || SelectedEdge is not null || Checks.Focus is not null || TrashWarning is not null;

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
        SaveView();
    }

    /// <summary>
    /// Changes the tracing method between FIFO and haircut and counts the amounts again; not while an expansion runs,
    /// which would otherwise be cut short.
    /// </summary>
    /// <returns>A task that completes when the graph is shown with the new amounts.</returns>
    [RelayCommand(CanExecute = nameof(CanToggleMethod))]
    public Task ToggleMethodAsync()
    {
        if (IsExpanding)
        {
            return Task.CompletedTask;
        }

        Method = Other(Method);
        SaveView();
        return ReloadAsync(fit: false);
    }

    /// <summary>
    /// Reads the graph anew for the same case, for example after a start point was added; while an expansion runs,
    /// the reading waits for it instead of cutting it short.
    /// </summary>
    /// <returns>A task that completes when the graph is shown, or at once when the reading waits.</returns>
    public Task RefreshAsync()
    {
        if (IsExpanding)
        {
            _refreshAfterExpansion = true;
            return Task.CompletedTask;
        }

        return ReloadAsync(fit: false);
    }

    /// <summary>
    /// Reads the graph anew and then says what a taking back has to tell (sketch AN1b), so that the reading does not
    /// replace it.
    /// </summary>
    /// <param name="notice">What to say, or <see langword="null"/>.</param>
    /// <returns>A task that completes when it is said.</returns>
    private async Task RefreshThenSayAsync(string? notice)
    {
        await RefreshAsync();
        if (notice is not null)
        {
            Say(notice);
        }
    }

    /// <summary>Puts a text first in the graph's notice, keeping what it already says.</summary>
    /// <param name="text">The text.</param>
    private void Say(string text) => Tell(ToastKind.Information, text);

    /// <summary>
    /// Says something: it goes before the notice, and to the toaster (sketch TO1a) as a toast of its sort.
    /// </summary>
    /// <param name="kind">The toast's sort.</param>
    /// <param name="text">What is said.</param>
    /// <param name="detail">A line under it, such as a fault's own message; empty for none.</param>
    /// <param name="retry">What <em>Försök igen</em> does; <see langword="null"/> for no action.</param>
    private void Tell(ToastKind kind, string text, string detail = "", Action? retry = null)
    {
        var said = detail.Length == 0 ? text : $"{text} {detail}";
        Notice = Notice.Length == 0 ? said : $"{said} {Notice}";
        ToastWanted?.Invoke(this, new ToastRequest(kind, text, detail, retry is null ? null : RetryText, retry));
    }

    /// <summary>Steps which amounts the lines carry: all, the traced, none.</summary>
    [RelayCommand]
    public void CycleLabels()
    {
        Labels = Labels.Next();
        Redraw?.Invoke(this, EventArgs.Empty);
        SaveView();
    }

    /// <summary>
    /// Gives the stored part of the surface on screen once, to be shown instead of fitting the graph when the case
    /// opens; afterwards a new graph is fitted as before.
    /// </summary>
    /// <returns>The stored viewport, or <see langword="null"/> when the graph should be fitted.</returns>
    public Viewport? TakeStoredViewport()
    {
        var stored = StoredViewport;
        StoredViewport = null;
        return stored;
    }

    /// <summary>Remembers where the user panned or zoomed to, and stores it with the view in the case.</summary>
    /// <param name="viewport">The part of the surface on screen.</param>
    public void ViewportChanged(Viewport viewport)
    {
        ArgumentNullException.ThrowIfNull(viewport);
        if (viewport == _viewport)
        {
            return;
        }

        _viewport = viewport;
        SaveView();
    }

    /// <summary>Shows or hides the legend behind the <em>?</em>.</summary>
    [RelayCommand]
    public void ToggleLegend() => IsLegendOpen = !IsLegendOpen;

    /// <summary>Lets a closed legend open with its rule, not with what was hovered last.</summary>
    /// <param name="value">Whether the legend is shown.</param>
    partial void OnIsLegendOpenChanged(bool value)
    {
        if (!value)
        {
            Legend.Reset();
        }
    }

    /// <summary>
    /// Closes the inspector by choosing nothing, as Esc and a click on the empty surface do; the cross in the
    /// inspector's heading, so that the way out is seen and not only known.
    /// </summary>
    [RelayCommand]
    public void Deselect() => Choose(null, null);

    /// <summary>
    /// <em>Varför?</em>: asks for the Guide with the answers for what is chosen at the top, opened at the section and
    /// heading of the most telling answer (sketch GD1b).
    /// </summary>
    [RelayCommand]
    public void AskWhy()
    {
        if (WhyRequest() is { } request)
        {
            GuideWanted?.Invoke(this, request);
        }
    }

    /// <summary>Asks for a section of the Guide, for example the visual language from the legend.</summary>
    /// <param name="topic">The section.</param>
    [RelayCommand]
    public void OpenGuideTopic(GuideTopic topic)
    {
        IsLegendOpen = false;
        GuideWanted?.Invoke(this, GuideRequest.ForTopic(topic));
    }

    /// <summary>
    /// Asks for the Guide on the method the graph traces with, at the method's own heading, with what it traced.
    /// </summary>
    [RelayCommand]
    public void OpenMethodGuide()
    {
        var answer = TraceText.Length > 0 ? TraceText : $"Grafen spårar med {MethodText}. Inget är spårat än.";
        GuideWanted?.Invoke(this, GuideRequest.ForMethod(Method, answer));
    }

    /// <summary>Gives the Guide's request for what is chosen, or <see langword="null"/> when nothing is.</summary>
    /// <returns>The request.</returns>
    private GuideRequest? WhyRequest() => Inspection is { } shown && (SelectedNode ?? SelectedEdge) is { } key
        ? GuideRequest.ForWhy(key, shown.Title, Why, Method)
        : null;

    /// <summary>
    /// Tells the Guide's panel that the answers changed, so that it can follow; a choice tells it itself, once.
    /// </summary>
    /// <param name="value">The new answers.</param>
    partial void OnWhyChanged(IReadOnlyList<WhyLine> value)
    {
        if (!_choosing)
        {
            WhyChanged?.Invoke(this, WhyRequest());
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
        if (ChooseFolded(nodeKey, edgeKey))
        {
            nodeKey = null;
            edgeKey = null;
        }

        var node = Scene?.Node(nodeKey);
        var edge = node is null ? Scene?.Edge(edgeKey) : null;
        SelectedNode = node?.Key;
        SelectedEdge = edge?.Key;
        IsLegendOpen = false;
        _choosing = true;
        try
        {
            Inspect();
        }
        finally
        {
            _choosing = false;
        }

        // Told as a choice also when the answers are the same, so that choosing a node again after following a link
        // in the Guide's panel shows its answer there (sketch GD1b).
        WhyChanged?.Invoke(this, WhyRequest() is { } request ? request with { IsChoice = true } : null);
        SeeWatches(SelectedNode);
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

    /// <summary>
    /// Moves a node while it is dragged: anywhere on the free surface, up and down in its column. A picked node takes
    /// the other picked nodes with it (<see cref="MoveGroup"/>).
    /// </summary>
    /// <param name="nodeKey">The node.</param>
    /// <param name="start">Where it stood when the drag began.</param>
    /// <param name="wanted">Where the pointer wants it.</param>
    public void MoveNode(string nodeKey, GraphPoint start, GraphPoint wanted)
    {
        if (Scene?.Node(nodeKey) is null)
        {
            return;
        }

        var at = GraphPlacement.Drag(Layout, start, wanted);
        if (_group is not null || MovesGroup(nodeKey))
        {
            MoveGroup(nodeKey, start, at);
        }
        else
        {
            _moved[(nodeKey, Layout)] = at;
        }

        Arrange();
    }

    /// <summary>
    /// Stores where a dragged node was left, in the case, off the UI thread; a dragged group is stored in one write.
    /// </summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns>A task that completes when it is stored or could not be.</returns>
    public async Task DropNodeAsync(string nodeKey)
    {
        if (TakeGroup() is { } group)
        {
            await DropGroupAsync(group.Actors.Keys, group.Notes.Keys);
            return;
        }

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
            Tell(ToastKind.Warning, SaveFailedText);
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

        await ExpandWithAsync(openCase, inspection.Expansion, () => ExpandCommand.Execute(null));
    }

    /// <summary>
    /// Expands the graph by the given lookups, off the UI thread, and says what came in; after it, a lingering toast
    /// says how many outputs that can never be spent are hidden, when the expansion brought new ones.
    /// </summary>
    /// <param name="openCase">The open case.</param>
    /// <param name="requests">The lookups.</param>
    /// <param name="retry">What <em>Försök igen</em> does when it fails.</param>
    /// <returns>A task that completes when the graph is shown after the expansion.</returns>
    private Task ExpandWithAsync(ICase openCase, IReadOnlyList<ExpansionRequest> requests, Action retry) =>
        ExpandWithAsync(token => _source.ExpandAsync(openCase, requests, Reading, token), retry);

    /// <summary>
    /// Expands the graph by a step off the UI thread, and says what came in; after it, a lingering toast says how many
    /// outputs that can never be spent are hidden, when the step brought new ones.
    /// </summary>
    /// <param name="work">The step, given the cancellation of the graph's work.</param>
    /// <param name="retry">What <em>Försök igen</em> does when it fails.</param>
    /// <returns>A task that completes when the graph is shown after the step.</returns>
    private async Task ExpandWithAsync(Func<CancellationToken, Task<GraphExpansion>> work, Action retry)
    {
        var version = _version;
        var token = _work.Token;
        var hiddenBefore = Scene?.Unspendable ?? 0;
        IsExpanding = true;
        try
        {
            var result = await work(token);
            if (version == _version)
            {
                // A reading that started before the expansion would bring back the graph without what it fetched.
                _version++;
                Show(result.Scene, fit: false);
                var problems = Notice;
                var said = ExpansionNotice(result, IsOffline ? OfflineNote : null);
                Notice = problems.Length == 0 ? said : $"{said} {problems}";
                ToastWanted?.Invoke(this, new ToastRequest(ExpansionKind(result), said));
                SayHiddenUnspendable(hiddenBefore);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (version == _version)
            {
                // Say why, so that the user can tell a source that refused from a fault (a cancelled one says nothing).
                Notice = string.Empty;
                Tell(
                    ToastKind.Error,
                    ExpandFailedText,
                    exception is OperationCanceledException ? string.Empty : exception.Message,
                    retry);
            }
        }
        finally
        {
            IsExpanding = false;
            if (_refreshAfterExpansion)
            {
                _refreshAfterExpansion = false;
                _ = ReloadAsync(fit: false, keepNotice: true);
            }
        }
    }

    /// <summary>Reads the graph anew from the case, off the UI thread.</summary>
    /// <param name="fit">Whether the view should fit the new graph to the screen.</param>
    /// <param name="keepNotice">Whether the notice stays, as after an expansion whose reading waited.</param>
    /// <returns>A task that completes when the graph is shown or could not be read.</returns>
    public async Task ReloadAsync(bool fit, bool keepNotice = false)
    {
        _work.Cancel();
        _work.Dispose();
        _work = new CancellationTokenSource();
        var version = ++_version;
        HasCase = _session.Current is not null;
        if (_session.Current is not { } openCase)
        {
            Clear(NoCaseText);
            return;
        }

        if (Scene is null)
        {
            EmptyText = LoadingText;
        }

        try
        {
            var read = BeginTrashRead();
            var scene = await _source.LoadAsync(openCase, Reading, _work.Token);
            var trash = await ReadTrashAsync(openCase, _work.Token);
            if (version == _version)
            {
                KeepTrash(read, openCase, trash);
                Show(scene, fit, keepNotice);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (version == _version && exception is not OperationCanceledException)
            {
                Clear(LoadFailedText);
            }
        }
    }

    /// <summary>Gives the sort of toast an expansion is told with: new tx are a success, nothing new or a part is
    /// information, and nothing brought in is a warning.</summary>
    /// <param name="result">The expansion.</param>
    /// <returns>The sort.</returns>
    public static ToastKind ExpansionKind(GraphExpansion result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Outcome switch
        {
            ExpansionOutcome.Expanded when result.NewTransactions > 0 => ToastKind.Success,
            ExpansionOutcome.Expanded or ExpansionOutcome.Partly => ToastKind.Information,
            _ => ToastKind.Warning,
        };
    }

    /// <summary>Says what an expansion brought, in a short line, with what the user should know besides.</summary>
    /// <param name="result">What the expansion brought.</param>
    /// <param name="offline">What Chainsaw says while offline, or <see langword="null"/> while lookups can go.</param>
    /// <returns>The line.</returns>
    public static string ExpansionNotice(GraphExpansion result, string? offline)
    {
        ArgumentNullException.ThrowIfNull(result);
        var problems = string.Join(" ", result.Problems);
        var text = result.Outcome switch
        {
            ExpansionOutcome.Expanded when result.NewTransactions == 0 => $"Inget nytt att hämta. {problems}",
            ExpansionOutcome.Expanded => $"{result.NewTransactions} nya tx i grafen. {problems}",
            ExpansionOutcome.Partly => $"Delvis expanderad: {result.NewTransactions} nya tx. {problems}",
            _ => $"Inte expanderad. {problems}",
        };
        return (offline is not null && result.Outcome != ExpansionOutcome.Expanded ? $"{text} {offline}" : text).Trim();
    }

    /// <summary>Lets the empty graph guide where the page would only say that there is nothing (sketch GR6c).</summary>
    /// <param name="value">What the page says in place of the graph.</param>
    partial void OnEmptyTextChanged(string value) => Start.Show(value switch
    {
        NoCaseText => GraphStartMode.NoCase,
        NoStartPointsText => GraphStartMode.NoStartPoint,
        _ => GraphStartMode.None,
    });

    /// <summary>Shows whether lookups can go now, and whether Bitcoin's sources are off.</summary>
    /// <param name="exit">The way out.</param>
    private void ShowWay(ExitWay exit)
    {
        IsOffline = !exit.IsLive;
        OfflineNote = exit.Offline.Text;
        IsSourcesOff = exit.ChainsOff.Contains(Chain.Bitcoin);
        Start.ShowOffline(exit.Offline);
    }

    /// <summary>Says whether the method can be changed now: not while an expansion runs.</summary>
    /// <returns><see langword="true"/> when it can.</returns>
    private bool CanToggleMethod() => !IsExpanding;

    /// <summary>Gives the other of the two methods.</summary>
    /// <param name="method">The method.</param>
    /// <returns>Haircut for FIFO and FIFO for haircut.</returns>
    private static TracingMethodKind Other(TracingMethodKind method) =>
        method == TracingMethodKind.Fifo ? TracingMethodKind.Haircut : TracingMethodKind.Fifo;

    /// <summary>
    /// Starts over for the case now open: nothing chosen, nothing moved, the view as the case stored it (method,
    /// layout, amounts and the part on screen), and the graph read anew.
    /// </summary>
    private void OnCaseChanged()
    {
        CaseChanged?.Invoke(this, EventArgs.Empty);
        ForgetWatchForms();
        _moved.Clear();
        _firstFree.Clear();
        _refreshAfterExpansion = false;
        _viewport = null;
        StoredViewport = null;
        _viewLoaded = false;
        Method = TracingMethodKind.Fifo;
        Layout = GraphLayout.Free;
        Labels = AmountLabelMode.All;
        _showUnspendable = false;
        CloseFan();
        ForgetTrash();
        Scene = null;
        SelectedNode = null;
        SelectedEdge = null;
        Inspect();
        Notice = string.Empty;
        _ = OpenAsync();
    }

    /// <summary>Reads the case's stored view off the UI thread, then the graph by its method.</summary>
    /// <returns>A task that completes when the graph is shown or could not be read.</returns>
    private async Task OpenAsync()
    {
        if (_session.Current is { } openCase)
        {
            var view = GraphViewState.Default;
            var read = false;
            try
            {
                view = await _source.LoadViewAsync(openCase, _work.Token);
                read = true;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The defaults are shown and the graph is read all the same, but nothing is stored over a view that
                // could not be read.
            }

            if (_session.Current != openCase)
            {
                return;
            }

            Method = view.Method;
            Layout = view.Layout;
            Labels = AmountLabelModes.FromStored(view.AmountLabels);
            _showUnspendable = view.ShowUnspendable;
            StoredViewport = view.Viewport is { } stored ? Viewport.FromStored(stored) : null;
            _viewport = StoredViewport;
            _viewLoaded = read;
        }

        await ReloadAsync(fit: true);
    }

    /// <summary>
    /// Stores the view in the case off the UI thread, one store at a time; a change made meanwhile is stored after.
    /// </summary>
    private void SaveView()
    {
        // Until the case's own view is read, what is shown is not the case's, and must never be stored in it.
        if (_session.Current is null || !_viewLoaded)
        {
            return;
        }

        if (!_savingView.IsCompleted)
        {
            _viewChangedMeanwhile = true;
            return;
        }

        _savingView = SaveViewAsync();
    }

    /// <summary>Stores the view, and again as long as it changed while it was stored.</summary>
    /// <returns>A task that completes when the latest view is stored or could not be.</returns>
    private async Task SaveViewAsync()
    {
        var openCase = _session.Current;
        do
        {
            _viewChangedMeanwhile = false;
            if (openCase is null || _session.Current != openCase || !_viewLoaded)
            {
                // A change made in the case opened meanwhile is stored on its own, not lost.
                if (_viewChangedMeanwhile && _viewLoaded && _session.Current is not null)
                {
                    _savingView = SaveViewAsync();
                }

                return;
            }

            var view = new GraphViewState(Method, Layout, Labels.ToStored(), _viewport?.ToStored())
            {
                ShowUnspendable = _showUnspendable,
            };
            try
            {
                await _source.SaveViewAsync(openCase, view);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                // The case may have closed, or have no operator profile for the journal's line; the view still holds
                // in this session. Said only while that case is still the one shown.
                if (_session.Current == openCase)
                {
                    Tell(ToastKind.Warning, ViewSaveFailedText);
                }
            }
        }
        while (_viewChangedMeanwhile);
    }

    /// <summary>
    /// Shows a new graph, keeping what is chosen when it is still there. The scene is shown as it was built; one that
    /// draws the outputs that can never be spent otherwise than the user chose meanwhile is built again off the UI
    /// thread and shown after (principle 6).
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="fit">Whether the view should fit it to the screen.</param>
    /// <param name="keepNotice">Whether the notice stays instead of the graph's problems.</param>
    private void Show(GraphScene scene, bool fit, bool keepNotice = false)
    {
        Scene = scene.WithTrash(_trash);
        scene = Scene;
        EmptyText = scene.Snapshot.Graph.StartPoints.Count == 0 && scene.Nodes.Count == 0
            ? NoStartPointsText
            : TrashEmptyText(scene);
        if (!keepNotice || Notice.Length == 0)
        {
            Notice = string.Join(" ", scene.Snapshot.Graph.Problems);
            if (Notice.Length > 0 && Notice != _toldProblems)
            {
                // The graph's problems are told once, not at every reading of the same graph.
                ToastWanted?.Invoke(this, new ToastRequest(ToastKind.Warning, Notice));
            }

            _toldProblems = Notice;
        }
        SelectedNode = scene.Node(SelectedNode)?.Key;
        SelectedEdge = scene.Edge(SelectedEdge)?.Key;
        Arrange();
        Inspect();
        ShowWatches();
        _ = Checks.FollowAsync(scene);
        FollowFan();
        ShowTrash();
        TellThrownStartPoints(scene);
        OnPropertyChanged(nameof(MethodToolTip));
        if (fit)
        {
            FitWanted?.Invoke(this, EventArgs.Empty);
        }

        _ = FollowReadingAsync();
    }

    /// <summary>Shows no graph, with a text in its place.</summary>
    /// <param name="text">The text.</param>
    private void Clear(string text)
    {
        Scene = null;
        EmptyText = text;
        Arrange();
        Inspect();
        ShowWatches();
        _ = Checks.FollowAsync(null);
        CloseFan();
        ShowTrash();
    }

    /// <summary>Works out every node's place and every line's curve in the layout shown, and draws again.</summary>
    private void Arrange()
    {
        if (Scene is { } scene)
        {
            KeepFirstFreePlaces(scene);
            var moved = new Dictionary<(string Key, GraphLayout Layout), GraphPoint>(_moved);
            foreach (var (key, place) in _firstFree)
            {
                moved.TryAdd((key, GraphLayout.Free), place);
            }

            Places = GraphPlacement.Place(scene.LayoutNodes, Layout, scene.Columns, moved);
            Curves = GraphHitTest.Curves(scene, Places, IsHopColumns);
            Amounts = AmountLayout.Place(scene, Places, Curves);
        }
        else
        {
            Places = new Dictionary<string, GraphPoint>();
            Curves = new Dictionary<string, EdgeCurve>();
            Amounts = new Dictionary<string, AmountSpot>();
        }

        Redraw?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Remembers the free place of each node that appears for the first time and that the case has no place for, so
    /// that a later expansion, which moves the hop columns, does not move it on the free surface. A node goes to its
    /// column place unless a node already stands there (dragged, kept or stored in the case); then below them all.
    /// </summary>
    /// <param name="scene">The scene.</param>
    private void KeepFirstFreePlaces(GraphScene scene)
    {
        var nodes = scene.LayoutNodes;
        var standing = nodes
            .Select(node => FreePlaceOf(node))
            .OfType<GraphPoint>()
            .ToList();
        foreach (var node in nodes.Where(node => FreePlaceOf(node) is null))
        {
            var column = scene.Columns[node.Key];
            var place = BelowStanding(column, standing);
            _firstFree[node.Key] = place;
            standing.Add(place);
        }
    }

    /// <summary>Gives where a node stands on the free surface now: dragged, kept or stored in the case.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The place, or <see langword="null"/> when it has none yet.</returns>
    private GraphPoint? FreePlaceOf(GraphNode node)
    {
        if (_moved.TryGetValue((node.Key, GraphLayout.Free), out var moved))
        {
            return moved;
        }

        if (_firstFree.TryGetValue(node.Key, out var kept))
        {
            return kept;
        }

        return node.Positions.TryGetValue(GraphLayout.Free, out var saved) ? new GraphPoint(saved.X, saved.Y) : null;
    }

    /// <summary>Gives a place in a column under every node standing in it, or the column's place if free.</summary>
    /// <param name="column">The node's place in its hop column.</param>
    /// <param name="standing">The places of the nodes that already stand on the free surface.</param>
    /// <returns>The place.</returns>
    public static GraphPoint BelowStanding(GraphPoint column, IReadOnlyCollection<GraphPoint> standing)
    {
        ArgumentNullException.ThrowIfNull(standing);
        var frame = GraphSizes.NodeFrame(column);
        var inColumn = standing
            .Where(place => GraphSizes.NodeFrame(place).Right > frame.X && place.X < frame.Right)
            .ToList();
        if (!inColumn.Any(place => GraphSizes.NodeFrame(place).Intersects(frame)))
        {
            return column;
        }

        return new GraphPoint(column.X, inColumn.Max(place => place.Y) + GraphSizes.RowSpacing);
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
        Forms.Follow(scene, node);
        History.Follow(scene, node);
        Checks.Inspect(node?.Key);
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
