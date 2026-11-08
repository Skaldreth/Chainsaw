using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// Picking several actors with a box on the empty surface, as on the Windows desktop, without WPF: the box picks what
/// it touches whichever way it is drawn, the keys replace, add or turn, Esc gives the pick back, a picked actor that
/// is dragged takes the others along in one write, and Delete throws them all.
/// </summary>
public sealed class BoxSelectionTests
{
    /// <summary>Three nodes on a row, 200 apart, each 132 wide (<see cref="GraphSizes.NodeWidth"/>).</summary>
    private static readonly Dictionary<string, GraphPoint> Row = new(StringComparer.Ordinal)
    {
        [WKey] = new GraphPoint(0, 0),
        [VKey] = new GraphPoint(200, 0),
        [UKey] = new GraphPoint(400, 0),
    };

    private readonly CaseSession _session = new();

    private readonly TestGraphSource _source = new();

    private readonly TestGraphComments _comments = new();

    private readonly TestTrashSource _trash = new();

    private static IReadOnlyList<NodeView> Nodes() => [.. Scene().Nodes.Where(node => Row.ContainsKey(node.Key))];

    private async Task<GraphViewModel> Open()
    {
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), _session);
        var graph = new GraphViewModel(
            _session, workflow, _source, TestWays.For(new TestExitConnection()), new InlineUi(),
            new TestGraphEditor(), new TestClock(), trash: _trash, comments: _comments);
        _session.Open(new TestCase("2026-0042"));
        await graph.ReloadAsync(fit: false);
        await graph.Comments.LoadAsync();
        return graph;
    }

    /// <summary>Gives a box that holds exactly the given nodes' frames, as the graph places them.</summary>
    private static GraphRect Around(GraphViewModel graph, params string[] keys) =>
        keys.Select(key => GraphSizes.NodeFrame(graph.Places[key])).Aggregate((all, frame) => all.Union(frame));

    [Fact]
    public void A_box_drawn_up_and_to_the_left_is_the_same_box()
    {
        var down = BoxSelection.Between(new GraphPoint(10, 20), new GraphPoint(110, 70));
        var up = BoxSelection.Between(new GraphPoint(110, 70), new GraphPoint(10, 20));

        Assert.Equal(new GraphRect(10, 20, 100, 50), down);
        Assert.Equal(down, up);
    }

    [Fact]
    public void Touching_a_frame_picks_the_node_and_missing_it_does_not()
    {
        var frame = GraphSizes.NodeFrame(new GraphPoint(200, 0));

        Assert.True(BoxSelection.Touches(new GraphRect(150, 10, 51, 5), frame));
        Assert.True(BoxSelection.Touches(new GraphRect(150, 10, 50, 5), frame));
        Assert.False(BoxSelection.Touches(new GraphRect(150, 10, 49, 5), frame));
    }

    [Fact]
    public void A_box_drawn_backwards_picks_what_it_touches()
    {
        var box = new BoxSelection(BoxMode.Replace, []);

        // From the right of V back to the middle of W: touches W and V, not U.
        var changed = box.Update(
            BoxSelection.Between(new GraphPoint(340, 10), new GraphPoint(60, 5)), Nodes(), Row);

        Assert.True(changed);
        Assert.Equal([VKey, WKey], box.Picked.Order());
    }

    [Fact]
    public void A_move_that_touches_the_same_nodes_changes_nothing()
    {
        var box = new BoxSelection(BoxMode.Replace, []);
        box.Update(new GraphRect(0, 0, 10, 10), Nodes(), Row);

        Assert.False(box.Update(new GraphRect(0, 0, 20, 20), Nodes(), Row));
    }

    [Fact]
    public void Without_a_key_the_box_replaces_the_pick()
    {
        var box = new BoxSelection(BoxSelection.ModeOf(control: false, shift: false), [UKey]);
        Assert.Empty(box.Picked);

        box.Update(new GraphRect(0, 0, 10, 10), Nodes(), Row);

        Assert.Equal([WKey], box.Picked);
    }

    [Fact]
    public void Shift_adds_to_the_pick()
    {
        var box = new BoxSelection(BoxSelection.ModeOf(control: false, shift: true), [UKey]);

        box.Update(new GraphRect(0, 0, 210, 10), Nodes(), Row);

        Assert.Equal(BoxMode.Add, box.Mode);
        Assert.Equal([UKey, WKey, VKey], box.Picked);
    }

    [Fact]
    public void Ctrl_turns_what_the_box_touches()
    {
        var box = new BoxSelection(BoxSelection.ModeOf(control: true, shift: true), [UKey, VKey]);

        // Touches V (picked: taken back) and W (not picked: picked); U stays picked.
        box.Update(new GraphRect(0, 0, 210, 10), Nodes(), Row);

        Assert.Equal(BoxMode.Toggle, box.Mode);
        Assert.Equal([UKey, WKey], box.Picked);

        // The box shrinks off V again: V is picked as before.
        box.Update(new GraphRect(0, 0, 10, 10), Nodes(), Row);
        Assert.Equal([UKey, VKey, WKey], box.Picked);
    }

    [Fact]
    public void The_surface_scrolls_only_near_its_edge()
    {
        Assert.Equal(new GraphPoint(0, 0), BoxSelection.ScrollSpeed(new GraphPoint(400, 300), 800, 600));
        Assert.Equal(-BoxSelection.EdgeSpeed, BoxSelection.ScrollSpeed(new GraphPoint(-5, 300), 800, 600).X);
        Assert.Equal(BoxSelection.EdgeSpeed, BoxSelection.ScrollSpeed(new GraphPoint(400, 900), 800, 600).Y);
        Assert.InRange(BoxSelection.ScrollSpeed(new GraphPoint(790, 300), 800, 600).X, 1, BoxSelection.EdgeSpeed - 1);
    }

    [Fact]
    public async Task The_box_picks_into_the_same_pick_as_ctrl_click()
    {
        var graph = await Open();
        graph.Checks.TogglePick(EKey);

        graph.BeginBox(BoxMode.Replace);
        Assert.Empty(graph.Checks.Picked);
        graph.BoxTo(Around(graph, WKey));
        graph.EndBox();
        graph.BeginBox(BoxMode.Add);
        graph.BoxTo(Around(graph, VKey));
        graph.EndBox();

        Assert.Equal([WKey, VKey], graph.Checks.Picked);
        Assert.True(graph.Checks.IsPicked(VKey));
        Assert.False(graph.IsBoxing);
    }

    [Fact]
    public async Task Esc_gives_back_the_pick_the_box_began_with()
    {
        var graph = await Open();
        graph.Checks.TogglePick(EKey);
        graph.Checks.TogglePick(KKey);

        graph.BeginBox(BoxMode.Replace);
        graph.BoxTo(Around(graph, WKey));
        Assert.Equal([WKey], graph.Checks.Picked);
        graph.CancelBox();

        Assert.Equal([EKey, KKey], graph.Checks.Picked);
        Assert.False(graph.IsBoxing);
    }

    [Fact]
    public async Task A_picked_actor_dragged_takes_the_others_along_in_one_write()
    {
        var tied = _comments.Seed("Om V", GraphPile.MemberKey(V), new NodePosition(3000, 3000));
        var graph = await Open();
        var note = graph.Comments.Find(tied.Id)!;
        var (w, v, u) = (graph.Places[WKey], graph.Places[VKey], graph.Places[UKey]);
        graph.Checks.TogglePick(WKey);
        graph.Checks.TogglePick(VKey);

        graph.MoveNode(WKey, w, new GraphPoint(w.X + 10, w.Y + 5));
        graph.MoveNode(WKey, w, new GraphPoint(w.X + 40, w.Y + 25));
        await graph.DropNodeAsync(WKey);

        Assert.Equal(new GraphPoint(w.X + 40, w.Y + 25), graph.Places[WKey]);
        Assert.Equal(new GraphPoint(v.X + 40, v.Y + 25), graph.Places[VKey]);
        Assert.Equal(u, graph.Places[UKey]);
        Assert.Equal(new GraphPoint(3040, 3025), note.Place);
        Assert.Empty(_source.Saved);
        var (layout, saved) = Assert.Single(_source.SavedTogether);
        Assert.Equal(GraphLayout.Free, layout);
        Assert.Equal([VKey, WKey], saved.Keys.Order());
        Assert.Contains(_comments.Placed, placed => placed.Id == tied.Id);
    }

    [Fact]
    public async Task An_actor_that_is_not_picked_is_dragged_alone()
    {
        var graph = await Open();
        var w = graph.Places[WKey];
        graph.Checks.TogglePick(VKey);
        graph.Checks.TogglePick(UKey);

        graph.MoveNode(WKey, w, new GraphPoint(w.X + 40, w.Y));
        await graph.DropNodeAsync(WKey);

        Assert.Single(_source.Saved);
        Assert.Empty(_source.SavedTogether);
    }

    [Fact]
    public async Task Delete_throws_every_picked_actor()
    {
        var graph = await Open();
        graph.BeginBox(BoxMode.Replace);
        graph.BoxTo(Around(graph, UKey));
        graph.EndBox();
        graph.BeginBox(BoxMode.Add);
        graph.BoxTo(Around(graph, KKey));
        graph.EndBox();

        graph.ThrowChosen();
        var warning = graph.TrashWarning!;
        Assert.Contains(warning.Rows, row => row.Key == UKey && row.IsRoot);
        Assert.Contains(warning.Rows, row => row.Key == KKey && row.IsRoot);
        await graph.ConfirmThrowAsync();

        var thrown = Assert.Single(_trash.Thrown).Select(member => member.Key).ToList();
        Assert.Contains(UKey, thrown);
        Assert.Contains(KKey, thrown);
        Assert.Null(graph.Scene!.Node(UKey));
        Assert.Null(graph.Scene.Node(KKey));
    }
}
