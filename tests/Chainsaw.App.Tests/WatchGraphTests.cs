using Chainsaw.App.Graph;
using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Security.Contracts;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// The watches in the graph (sketch BV2a): the node's menu, Bevaka … (B), Ctrl+click into the form, the hits seen
/// when the node is chosen, the history (AN1b), the pill's dot and the setting <em>Hur ofta</em>.
/// </summary>
public sealed class WatchGraphTests
{
    private readonly TestWatchSource _source = new();

    private (GraphViewModel Graph, WatchBoard Board, WatchFormViewModel Form) Graph()
    {
        var session = new CaseSession();
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), session);
        var graph = new GraphViewModel(
            session, workflow, new TestGraphSource(), TestWays.For(new TestExitConnection()), new InlineUi(),
            new TestGraphEditor(), new TestClock());
        var board = new WatchBoard(session, _source, new InlineUi());
        var form = new WatchFormViewModel(board);
        graph.FollowWatches(board, form);
        session.Open(new TestCase("2026-0026"));
        return (graph, board, form);
    }

    [Fact]
    public async Task The_menu_offers_Bevaka_on_an_address_and_a_cluster_and_opens_the_form()
    {
        var (graph, _, form) = Graph();
        await graph.ReloadAsync(fit: false);

        graph.OpenMenu(KKey);
        Assert.Equal(KKey, graph.SelectedNode);
        var watch = graph.MenuRows.Single(row => row.Action == NodeMenuAction.Watch);
        Assert.True(watch.IsEnabled);
        graph.RunMenu(watch);

        Assert.False(graph.IsMenuOpen);
        Assert.True(form.IsOpen);
        Assert.Equal(WatchSubjectKind.Cluster, form.Subject!.Kind);
        Assert.Equal(WatchSamples.On(K1), form.Subject.Anchor);
    }

    [Fact]
    public async Task Ctrl_click_adds_a_node_to_the_open_form_and_is_left_for_the_areas_otherwise()
    {
        var (graph, _, form) = Graph();
        await graph.ReloadAsync(fit: false);

        Assert.False(graph.PickForWatch(EKey));
        graph.Choose(VKey, null);
        graph.OpenWatch();
        Assert.True(graph.PickForWatch(EKey));

        Assert.Equal(WatchSamples.On(E), Assert.Single(form.Receivers).Address);
    }

    [Fact]
    public async Task Choosing_the_watched_node_sees_its_hits_and_a_hit_is_told()
    {
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter(WatchDirection.Out, [WatchSamples.On(E)]));
        var hit = _source.Hit(watch, WatchSamples.Out(WatchSamples.On(E), 0.3m));
        var (graph, board, _) = Graph();
        await graph.ReloadAsync(fit: false);
        await board.ReloadAsync();
        Assert.True(graph.WatchOverlay.Moves);

        graph.TellHits([hit]);
        graph.Choose(VKey, null);
        await board.ReloadAsync();

        Assert.Contains("Bevakning slog till", graph.Notice, StringComparison.Ordinal);
        Assert.Equal(1, _source.Seen[watch.Id]);
        Assert.False(graph.WatchOverlay.Moves);
    }

    [Fact]
    public async Task Show_watch_chooses_its_node_or_says_it_is_not_in_the_graph()
    {
        var inGraph = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter());
        var outside = _source.Add(WatchSamples.Subject(Address(77), "Utanför"), WatchSamples.Filter());
        var (graph, _, _) = Graph();
        await graph.ReloadAsync(fit: false);

        Assert.True(graph.ShowWatch(inGraph));
        Assert.Equal(VKey, graph.SelectedNode);
        Assert.False(graph.ShowWatch(outside));
    }

    [Fact]
    public async Task The_history_shows_the_watch_its_taking_back_and_its_hits()
    {
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter(WatchDirection.Out, [WatchSamples.On(E)]));
        _source.Hit(watch, WatchSamples.Out(WatchSamples.On(E), 0.3m));
        await _source.RemoveAsync(new TestCase("x"), watch.Id, "Klar");
        var scene = Scene();
        var data = new NodeHistoryData([], [], [], null, new Dictionary<long, string>())
        {
            Watches = _source.Records,
            WatchHits = _source.Hits,
        };

        var rows = NodeHistory.Build(scene, scene.Node(VKey)!, data);

        var made = rows.Single(row => row.Kind == HistoryKind.Watch);
        Assert.True(made.IsWithdrawn);
        Assert.False(made.CanUndo);
        Assert.Equal("Bevakning · ut · 1 mottagare", made.Text);
        Assert.Contains(rows, row => row.Kind == HistoryKind.Withdrawal && row.Text == "Tillbakatagen: bevakningen");
        Assert.Contains(rows, row => row is { Kind: HistoryKind.WatchHit, IsFact: true });
        Assert.DoesNotContain(NodeHistory.Build(scene, scene.Node(UKey)!, data), row => row.Kind == HistoryKind.Watch);
    }

    [Fact]
    public void A_watch_in_force_can_be_taken_back_from_the_history()
    {
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter());
        var scene = Scene();
        var data = new NodeHistoryData([], [], [], null, new Dictionary<long, string>()) { Watches = _source.Records };

        var made = NodeHistory.Build(scene, scene.Node(VKey)!, data).Single(row => row.Kind == HistoryKind.Watch);

        Assert.True(made.CanUndo);
        Assert.Equal(HistoryUndo.RemoveWatch, made.Undo);
        Assert.Equal(watch.Id, made.WatchId);
    }

    [Fact]
    public async Task The_pill_eye_gets_a_dot_for_unseen_hits_and_Nytt_Bevakning_opens_the_form()
    {
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter());
        _source.Hit(watch, WatchSamples.Out(WatchSamples.On(E), 0.3m));
        var rig = new GateRig(VaultState.Unlocked);
        var board = new WatchBoard(rig.Session, _source, rig.Ui);
        var monitoring = new MonitoringViewModel(
            board, new WatchFormViewModel(board), rig.Clock);
        var shell = new ShellViewModel(
            rig.Session, rig.Commands, rig.NewCase, rig.Workflow, rig.Way, rig.Exits, rig.Sanctions, rig.ExitPanel,
            rig.Settings, rig.Ui, monitoring, board);
        rig.Session.Open(new TestCase("2026-0026"));
        await board.ReloadAsync();

        var eye = shell.Items.Single(item => item.Entry.View == AppView.Monitoring);
        Assert.True(eye.HasWaiting);
        var nytt = shell.Items.Single(item => item.Entry.View == AppView.New).Submenu!;
        nytt.Open();
        nytt.Choose(nytt.Rows.Single(row => row.Row.Action == SubmenuAction.Watch));

        Assert.True(shell.IsMonitoringShown);
        Assert.False(shell.IsPlaceholderShown);
        Assert.True(monitoring.Form.IsOpen);
    }

    [Fact]
    public async Task How_often_is_chosen_per_watch_in_the_form_and_kept_by_a_change()
    {
        var (_, board, form) = Graph();
        form.OpenFor(WatchSamples.Subject(V));
        Assert.Equal(WatchPace.EveryBlock, form.Pace);

        form.ChoosePace(WatchPace.FiveMinutes);
        await form.SaveAsync();
        await board.ReloadAsync();
        form.OpenChange(board.Snapshot.Watches.Single());

        Assert.Equal(WatchPace.FiveMinutes, _source.Added.Single().Pace);
        Assert.Equal(WatchPace.FiveMinutes, form.Pace);
        Assert.True(form.Paces.Single(choice => Equals(choice.Value, WatchPace.FiveMinutes)).IsChosen);
        Assert.Equal(["Varje block", "1 min", "5 min", "15 min"], form.Paces.Select(choice => choice.Label));
    }
}
