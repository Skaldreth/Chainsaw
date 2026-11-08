using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// <em>Sortera grafen</em> without WPF: the icon only where it can be used, the question first, Avbryt that changes
/// nothing, the sorting stored in one write, comments that follow their actor, and <em>Ångra</em> that puts
/// everything back exactly.
/// </summary>
public sealed class GraphTidyTests
{
    private readonly TestGraphSource _source = new();

    private readonly TestGraphComments _comments = new();

    private readonly CaseSession _session = new();

    private readonly List<ToastRequest> _toasts = [];

    private int _fits;

    private async Task<GraphViewModel> Open(bool open = true)
    {
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), _session);
        var graph = new GraphViewModel(
            _session, workflow, _source, TestWays.For(new TestExitConnection()), new InlineUi(),
            new TestGraphEditor(), new TestClock(), comments: _comments);
        graph.ToastWanted += (_, toast) => _toasts.Add(toast);
        graph.FitWanted += (_, _) => _fits++;
        if (open)
        {
            _session.Open(new TestCase("2026-0042"));
            await graph.ReloadAsync(fit: false);
            await graph.Comments.LoadAsync();
        }

        return graph;
    }

    /// <summary>Drags every actor to the same corner of a messy surface, so that sorting moves them all.</summary>
    private static void Mess(GraphViewModel graph)
    {
        var offset = 0;
        foreach (var key in graph.Places.Keys.ToList())
        {
            graph.MoveNode(key, graph.Places[key], new GraphPoint(900 + offset, 40 + (offset / 2)));
            offset += 37;
        }
    }

    [Fact]
    public async Task The_icon_is_shown_only_on_the_free_surface_of_a_case_with_two_actors_or_more()
    {
        var closed = await Open(open: false);
        Assert.False(closed.CanTidy);

        var graph = await Open();
        Assert.True(graph.CanTidy);

        graph.ShowLayout(GraphLayout.HopColumns);
        Assert.False(graph.CanTidy);
        graph.ShowLayout(GraphLayout.Free);
        Assert.True(graph.CanTidy);
    }

    [Fact]
    public async Task A_graph_with_one_actor_offers_no_sorting()
    {
        var single = GraphSamples.Graph() with { Nodes = [.. GraphSamples.Graph().Nodes.Where(node => node.Key == VKey)], Edges = [] };
        _source.Scene = () => Scene(graph: single);

        var graph = await Open();

        Assert.False(graph.CanTidy);
    }

    [Fact]
    public async Task A_click_asks_first_and_says_how_many_actors_move()
    {
        var graph = await Open();
        Mess(graph);
        var before = new Dictionary<string, GraphPoint>(graph.Places);

        await graph.AskTidyAsync();

        Assert.True(graph.IsTidyConfirmOpen);
        Assert.Equal(GraphViewModel.QuestionText(5), graph.TidyConfirmText);
        Assert.Equal("Sortera om grafen? 5 aktörer flyttas från där du har lagt dem.", graph.TidyConfirmText);
        Assert.Equal(before, graph.Places);
        Assert.Empty(_source.SavedTogether);
    }

    [Fact]
    public async Task Cancelling_changes_nothing()
    {
        var graph = await Open();
        Mess(graph);
        var before = new Dictionary<string, GraphPoint>(graph.Places);
        await graph.AskTidyAsync();

        graph.CancelTidy();
        await graph.ConfirmTidyAsync();

        Assert.False(graph.IsTidyConfirmOpen);
        Assert.Equal(before, graph.Places);
        Assert.Empty(_source.SavedTogether);
        Assert.Empty(_toasts);
    }

    [Fact]
    public async Task Sorting_places_every_actor_stores_them_in_one_write_and_offers_undo()
    {
        var graph = await Open();
        Mess(graph);
        var fitsBefore = _fits;
        await graph.AskTidyAsync();

        await graph.ConfirmTidyAsync();

        var expected = TidyLayout.Arrange(
            graph.Scene!.LayoutNodes, graph.Scene.Edges.Select(edge => (edge.Edge.From, edge.Edge.To)));
        Assert.False(graph.IsTidyConfirmOpen);
        Assert.Equal(expected.OrderBy(pair => pair.Key), graph.Places.OrderBy(pair => pair.Key));
        var (layout, saved) = Assert.Single(_source.SavedTogether);
        Assert.Equal(GraphLayout.Free, layout);
        Assert.Equal(expected.OrderBy(pair => pair.Key), saved.OrderBy(pair => pair.Key));
        Assert.Equal(fitsBefore + 1, _fits);
        var toast = Assert.Single(_toasts);
        Assert.Equal(GraphViewModel.TidiedText, toast.Title);
        Assert.Equal(GraphViewModel.TidyUndoText, toast.ActionText);
    }

    [Fact]
    public async Task Undo_puts_every_actor_back_exactly()
    {
        var graph = await Open();
        Mess(graph);
        var before = new Dictionary<string, GraphPoint>(graph.Places);
        await graph.AskTidyAsync();
        await graph.ConfirmTidyAsync();

        await graph.UndoTidyAsync();

        Assert.Equal(before.OrderBy(pair => pair.Key), graph.Places.OrderBy(pair => pair.Key));
        Assert.Equal(2, _source.SavedTogether.Count);
        Assert.Equal(before.OrderBy(pair => pair.Key), _source.SavedTogether[1].Places.OrderBy(pair => pair.Key));

        // Undone once; a second Ångra does nothing.
        await graph.UndoTidyAsync();
        Assert.Equal(2, _source.SavedTogether.Count);
    }

    [Fact]
    public async Task A_comment_follows_its_actor_and_a_loose_comment_stays()
    {
        var tied = _comments.Seed("Om K", GraphPile.MemberKey(K1), new NodePosition(5000, 5000));
        var loose = _comments.Seed("Fri", null, new NodePosition(-400, -400));
        var graph = await Open();
        Mess(graph);
        var tiedNote = graph.Comments.Find(tied.Id)!;
        Assert.Equal(KKey, tiedNote.AnchorNode);
        var actorBefore = graph.Places[KKey];

        await graph.AskTidyAsync();
        await graph.ConfirmTidyAsync();

        var actorAfter = graph.Places[KKey];
        var expected = new GraphPoint(
            5000 + (actorAfter.X - actorBefore.X), 5000 + (actorAfter.Y - actorBefore.Y));
        Assert.Equal(expected, tiedNote.Place);
        Assert.Equal(new GraphPoint(-400, -400), graph.Comments.Find(loose.Id)!.Place);
        Assert.Contains(_comments.Placed, placed => placed.Id == tied.Id);
        Assert.DoesNotContain(_comments.Placed, placed => placed.Id == loose.Id);

        await graph.UndoTidyAsync();
        Assert.Equal(new GraphPoint(5000, 5000), tiedNote.Place);
    }

    [Fact]
    public async Task A_graph_already_sorted_is_said_without_a_question()
    {
        var graph = await Open();
        await graph.AskTidyAsync();
        await graph.ConfirmTidyAsync();
        _toasts.Clear();

        await graph.AskTidyAsync();

        Assert.False(graph.IsTidyConfirmOpen);
        Assert.Equal(GraphViewModel.AlreadyTidyText, Assert.Single(_toasts).Title);
    }

    [Fact]
    public async Task Switching_to_the_hop_columns_closes_the_question()
    {
        var graph = await Open();
        Mess(graph);
        await graph.AskTidyAsync();

        graph.ShowLayout(GraphLayout.HopColumns);
        await graph.ConfirmTidyAsync();

        Assert.False(graph.IsTidyConfirmOpen);
        Assert.Empty(_source.SavedTogether);
    }

    [Fact]
    public async Task Undo_does_nothing_after_the_case_changed()
    {
        var graph = await Open();
        Mess(graph);
        await graph.AskTidyAsync();
        await graph.ConfirmTidyAsync();

        _session.Open(new TestCase("2026-0043"));
        await graph.UndoTidyAsync();

        Assert.Single(_source.SavedTogether);
    }
}
