using Chainsaw.App.Graph;
using Chainsaw.App.Settings;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>A trash in memory that throws and puts back as the module does, and remembers what it was asked.</summary>
internal sealed class TestTrashSource : ITrashSource
{
    private long _next = 1;

    public List<TrashThrow> Throws { get; } = [];

    public List<IReadOnlyList<TrashMember>> Thrown { get; } = [];

    public List<(long Throw, string? Key)> Restored { get; } = [];

    public bool Fails { get; set; }

    /// <summary>Makes a throw find its node already in the trash.</summary>
    public bool AlreadyThrown { get; set; }

    /// <summary>Holds the next throws until the test lets them go.</summary>
    public TaskCompletionSource? HoldThrow { get; set; }

    /// <summary>Readings that wait for what the test gives, taken in order.</summary>
    public Queue<TaskCompletionSource<IReadOnlyList<TrashThrow>>> HeldReads { get; } = [];

    /// <summary>Called when a throw is written, before it returns.</summary>
    public Action? OnThrow { get; set; }

    public Task<IReadOnlyList<TrashThrow>> ReadAsync(ICase @case, CancellationToken cancellationToken) =>
        HeldReads.TryDequeue(out var held)
            ? held.Task
            : Task.FromResult<IReadOnlyList<TrashThrow>>([.. Throws]);

    public async Task<TrashThrow> ThrowAsync(ICase @case, IReadOnlyList<TrashMember> members)
    {
        if (HoldThrow is { } hold)
        {
            await hold.Task;
        }

        if (Fails)
        {
            throw new CaseException(CaseError.NoOperatorProfile);
        }

        if (AlreadyThrown)
        {
            throw new InvalidOperationException("Already in the trash.");
        }

        OnThrow?.Invoke();

        Thrown.Add(members);
        var thrown = new TrashThrow(
            _next++, members, new OperatorProfile("Testa", "Utredare", "Enheten"), DateTimeOffset.UnixEpoch, 1);
        Throws.Add(thrown);
        Records.Add(([.. members.Select(member => member.Key)], Record(TrashAction.Thrown, thrown.Sequence)));
        return thrown;
    }

    /// <summary>Every throw and putting back, with the keys it concerned, oldest first.</summary>
    public List<(IReadOnlyList<string> Keys, TrashRecord Record)> Records { get; } = [];

    public Task<IReadOnlyList<TrashRecord>> HistoryAsync(ICase @case, string key, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TrashRecord>>(
            [.. Records.Where(each => each.Keys.Contains(key)).Select(each => each.Record)]);

    private TrashRecord Record(TrashAction action, long throwSequence) => new(
        Records.Count + 100, action, throwSequence, new OperatorProfile("Testa", "Utredare", "Enheten"),
        DateTimeOffset.UnixEpoch.AddMinutes(Records.Count), 1);

    public Task<IReadOnlyList<TrashMember>> RestoreAsync(ICase @case, long throwSequence, string? key)
    {
        Restored.Add((throwSequence, key));
        var index = Throws.FindIndex(each => each.Sequence == throwSequence);
        if (index < 0)
        {
            return Task.FromResult<IReadOnlyList<TrashMember>>([]);
        }

        var back = key is null ? Throws[index].Members : Throws[index].Branch(key);
        Records.Add(([.. back.Select(member => member.Key)], Record(TrashAction.Restored, throwSequence)));
        var left = Throws[index].Members.Where(member => !back.Contains(member)).ToList();
        if (left.Count == 0)
        {
            Throws.RemoveAt(index);
        }
        else
        {
            Throws[index] = Throws[index] with { Members = left };
        }

        return Task.FromResult(back);
    }
}

/// <summary>A store of thrown cases in memory.</summary>
internal sealed class TestCaseTrashStore : ICaseTrashStore
{
    public List<TrashedCase> Saved { get; set; } = [];

    /// <summary>Makes every save fail, as on a full disk.</summary>
    public bool Refuses { get; set; }

    public IReadOnlyList<TrashedCase> Load() => Saved;

    public bool Save(IReadOnlyList<TrashedCase> cases)
    {
        if (Refuses)
        {
            return false;
        }

        Saved = [.. cases];
        return true;
    }
}

/// <summary>The trash in the graph (sketch SO1a): the filter, the stubs, the warning and the throws.</summary>
public sealed class GraphTrashTests
{
    /// <summary>The graph with a trash in memory, a case open.</summary>
    private sealed class Rig
    {
        public Rig()
        {
            Workflow = new CaseWorkflow(new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), Session);
            Graph = new GraphViewModel(
                Session, Workflow, new TestGraphSource(), TestWays.For(new TestExitConnection()), new InlineUi(),
                new TestGraphEditor(), Clock, trash: Trash);
            Graph.ToastWanted += (_, toast) => Toasts.Add(toast);
            Session.Open(new TestCase("2026-0001"));
        }

        public CaseSession Session { get; } = new();

        public CaseWorkflow Workflow { get; }

        public TestClock Clock { get; } = new();

        public TestTrashSource Trash { get; } = new();

        public GraphViewModel Graph { get; }

        public List<ToastRequest> Toasts { get; } = [];
    }

    private static TrashThrow ThrowOf(params TrashMember[] members) =>
        new(1, members, new OperatorProfile("Testa", "Utredare", "Enheten"), DateTimeOffset.UnixEpoch, 1);

    [Fact]
    public void A_thrown_actor_is_hidden_and_its_lines_become_stubs_at_the_others()
    {
        var scene = Scene().WithTrash(new TrashFilter([ThrowOf(new TrashMember(UKey, null, "U", "Unknown"))]));

        Assert.Null(scene.Node(UKey));
        Assert.DoesNotContain(scene.Edges, edge => edge.Edge.To == UKey);
        Assert.Equal([new TrashStub(VKey, true)], scene.TrashStubs);
        Assert.Equal(1, scene.Trashed);
    }

    [Fact]
    public void A_cluster_is_found_again_by_all_its_addresses_under_another_key()
    {
        var member = new TrashMember("cluster:other", null, "K", "Cluster")
        {
            Keys = [GraphPile.MemberKey(K1), GraphPile.MemberKey(K2)],
        };

        var scene = Scene().WithTrash(new TrashFilter([ThrowOf(member)]));

        Assert.Null(scene.Node(KKey));
        Assert.Contains(new TrashStub(VKey, true), scene.TrashStubs);
        Assert.Contains(new TrashStub(EKey, false), scene.TrashStubs);
    }

    [Fact]
    public void A_cluster_that_grew_into_a_thrown_address_is_not_hidden()
    {
        var member = new TrashMember("address:k2", null, "K2", "Unknown") { Keys = [GraphPile.MemberKey(K2)] };

        var scene = Scene().WithTrash(new TrashFilter([ThrowOf(member)]));

        Assert.NotNull(scene.Node(KKey));
        Assert.Empty(scene.TrashStubs);
    }

    [Fact]
    public void A_line_with_both_ends_in_the_trash_goes_without_a_stub()
    {
        var scene = Scene().WithTrash(new TrashFilter([ThrowOf(
            new TrashMember(KKey, null, "K", "Cluster"), new TrashMember(EKey, KKey, "E", "Unknown"))]));

        Assert.Equal([new TrashStub(VKey, true)], scene.TrashStubs);
        Assert.Equal(2, scene.Trashed);
    }

    [Fact]
    public void Nothing_in_the_trash_leaves_the_scene_as_it_was()
    {
        var scene = Scene();

        Assert.Same(scene, scene.WithTrash(TrashFilter.Empty));
        Assert.Empty(scene.TrashStubs);
        Assert.Equal(5, scene.Nodes.Count);
    }

    [Fact]
    public void Showing_the_unspendable_keeps_what_the_trash_hides()
    {
        var scene = Scene().WithTrash(new TrashFilter([ThrowOf(new TrashMember(UKey, null, "U", "Unknown"))]));

        Assert.Null(scene.WithUnspendable(true).Node(UKey));
    }

    [Fact]
    public async Task A_lone_end_is_thrown_at_once_with_undo_on_the_toast()
    {
        var rig = new Rig();
        await rig.Graph.ReloadAsync(fit: false);
        rig.Graph.Choose(UKey, null);

        rig.Graph.ThrowChosen();
        await Task.Yield();

        var thrown = Assert.Single(rig.Trash.Thrown);
        Assert.Equal(UKey, Assert.Single(thrown).Key);
        Assert.Equal([GraphPile.MemberKey(U)], thrown[0].Keys);
        Assert.Null(rig.Graph.Scene!.Node(UKey));
        Assert.Null(rig.Graph.SelectedNode);
        Assert.False(rig.Graph.HasTrashWarning);
        var toast = rig.Toasts.Last();
        Assert.Equal("Slängde 1 aktör", toast.Title);
        Assert.Equal(GraphViewModel.UndoText, toast.ActionText);

        toast.Action!();
        await Task.Yield();

        Assert.Equal((1L, (string?)null), Assert.Single(rig.Trash.Restored));
        Assert.NotNull(rig.Graph.Scene!.Node(UKey));
    }

    [Fact]
    public async Task What_hangs_on_the_thrown_opens_the_warning_and_is_thrown_on_confirm()
    {
        var rig = new Rig();
        await rig.Graph.ReloadAsync(fit: false);

        rig.Graph.AskThrow([KKey]);

        var warning = rig.Graph.TrashWarning!;
        Assert.Equal([KKey, EKey], warning.Rows.Select(row => row.Key));
        Assert.Equal(["", TrashRowViewModel.LastGlyph], warning.Rows.Select(row => row.Glyph));
        Assert.Equal("Släng alla 2 · Enter", warning.ConfirmText);
        Assert.Empty(rig.Trash.Thrown);
        Assert.True(rig.Graph.IsFocused);
        Assert.Equal(warning.Keys, rig.Graph.Near.Nodes);

        await rig.Graph.ConfirmThrowAsync();

        Assert.Equal([KKey, EKey], Assert.Single(rig.Trash.Thrown).Select(member => member.Key));
        Assert.Equal(KKey, rig.Trash.Thrown[0][1].Parent);
        Assert.False(rig.Graph.HasTrashWarning);
        Assert.Null(rig.Graph.Scene!.Node(KKey));
        Assert.Null(rig.Graph.Scene.Node(EKey));
    }

    [Fact]
    public async Task Cancel_throws_nothing()
    {
        var rig = new Rig();
        await rig.Graph.ReloadAsync(fit: false);
        rig.Graph.AskThrow([KKey]);

        rig.Graph.CancelThrow();

        Assert.False(rig.Graph.HasTrashWarning);
        Assert.Empty(rig.Trash.Thrown);
        Assert.NotNull(rig.Graph.Scene!.Node(KKey));
    }

    [Fact]
    public async Task Ctrl_z_puts_the_latest_throw_back()
    {
        var rig = new Rig();
        await rig.Graph.ReloadAsync(fit: false);
        rig.Graph.AskThrow([UKey]);
        await Task.Yield();

        await rig.Graph.UndoThrowAsync();

        Assert.Empty(rig.Trash.Throws);
        Assert.NotNull(rig.Graph.Scene!.Node(UKey));
    }

    [Fact]
    public async Task A_throw_that_cannot_be_written_says_so_and_hides_nothing()
    {
        var rig = new Rig();
        await rig.Graph.ReloadAsync(fit: false);
        rig.Trash.Fails = true;

        rig.Graph.AskThrow([UKey]);
        await Task.Yield();

        Assert.Equal(GraphViewModel.ThrowFailedText, rig.Toasts.Last().Title);
        Assert.NotNull(rig.Graph.Scene!.Node(UKey));
    }

    [Fact]
    public async Task What_lies_in_the_trash_is_hidden_when_the_case_is_read_again()
    {
        var rig = new Rig();
        rig.Trash.Throws.Add(ThrowOf(new TrashMember(UKey, null, "U", "Unknown")));

        await rig.Graph.ReloadAsync(fit: false);

        Assert.Null(rig.Graph.Scene!.Node(UKey));
        Assert.True(rig.Graph.Bin.HasThrows);
        var group = Assert.Single(rig.Graph.TrashView.Groups);
        Assert.Equal("1 aktör", group.Count);
        Assert.Equal("1 slängning · 1 dolda i grafen", rig.Graph.TrashView.HeadText);
    }

    [Fact]
    public async Task A_row_in_the_view_is_put_back_through_the_graph()
    {
        var rig = new Rig();
        await rig.Graph.ReloadAsync(fit: false);
        rig.Graph.AskThrow([KKey]);
        await rig.Graph.ConfirmThrowAsync();
        var row = rig.Graph.TrashView.Groups[0].Rows[1];

        rig.Graph.TrashView.RestoreCommand.Execute(row);
        await Task.Yield();

        Assert.Equal((1L, (string?)EKey), Assert.Single(rig.Trash.Restored));
        Assert.NotNull(rig.Graph.Scene!.Node(EKey));
        Assert.Null(rig.Graph.Scene.Node(KKey));
    }

    [Fact]
    public async Task A_node_dragged_onto_the_hot_bin_is_thrown_and_not_moved()
    {
        var rig = new Rig();
        await rig.Graph.ReloadAsync(fit: false);

        rig.Graph.DragNear(0);
        Assert.True(rig.Graph.Bin.IsHot);
        Assert.True(rig.Graph.DropOnBin(UKey));
        await Task.Yield();

        Assert.Single(rig.Trash.Thrown);
        Assert.False(rig.Graph.Bin.IsDragging);
        Assert.False(rig.Graph.DropOnBin(UKey));
    }

    [Fact]
    public void Without_a_case_nothing_can_be_thrown()
    {
        var rig = new Rig();
        rig.Session.Forget();

        rig.Graph.DragNear(0);

        Assert.False(rig.Graph.CanThrow);
        Assert.False(rig.Graph.Bin.IsDragging);
    }

    [Fact]
    public async Task Undo_after_another_case_was_opened_puts_nothing_back_there()
    {
        var rig = new Rig();
        await rig.Graph.ReloadAsync(fit: false);
        rig.Graph.AskThrow([UKey]);
        await Task.Yield();
        var undo = rig.Toasts.Last().Action!;

        rig.Session.Open(new TestCase("2026-0002"));
        await rig.Graph.UndoThrowAsync();
        undo();
        await Task.Yield();

        Assert.Empty(rig.Trash.Restored);
        Assert.Equal(GraphViewModel.UndoOtherCaseText, rig.Toasts.Last().Title);
    }

    [Fact]
    public async Task A_throw_that_ends_after_another_case_was_opened_is_neither_remembered_nor_told()
    {
        var rig = new Rig();
        await rig.Graph.ReloadAsync(fit: false);
        rig.Trash.HoldThrow = new TaskCompletionSource();
        rig.Graph.AskThrow([UKey]);

        rig.Session.Open(new TestCase("2026-0002"));
        await rig.Graph.ReloadAsync(fit: false);
        rig.Trash.HoldThrow.SetResult();
        await Task.Yield();
        await rig.Graph.UndoThrowAsync();

        Assert.Empty(rig.Trash.Restored);
        Assert.DoesNotContain(rig.Toasts, toast => toast.Title.StartsWith("Slängde", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_older_reading_of_the_trash_never_replaces_a_newer_one()
    {
        var rig = new Rig();
        rig.Trash.Throws.Add(ThrowOf(new TrashMember(UKey, null, "U", "Unknown")));
        await rig.Graph.ReloadAsync(fit: false);
        Assert.Null(rig.Graph.Scene!.Node(UKey));
        var stale = new TaskCompletionSource<IReadOnlyList<TrashThrow>>();
        rig.Trash.HeldReads.Enqueue(stale);
        var older = rig.Graph.ReloadAsync(fit: false);

        await rig.Graph.RestoreAsync(1, null);
        Assert.NotNull(rig.Graph.Scene!.Node(UKey));
        stale.SetResult([ThrowOf(new TrashMember(UKey, null, "U", "Unknown"))]);
        await older;

        Assert.NotNull(rig.Graph.Scene!.Node(UKey));
        Assert.Empty(rig.Graph.TrashView.Groups);
    }

    [Fact]
    public async Task Two_quick_deletes_throw_once()
    {
        var rig = new Rig();
        await rig.Graph.ReloadAsync(fit: false);
        rig.Trash.HoldThrow = new TaskCompletionSource();
        rig.Graph.Choose(UKey, null);

        rig.Graph.ThrowChosen();
        rig.Graph.ThrowChosen();
        rig.Trash.HoldThrow.SetResult();
        await Task.Yield();

        Assert.Single(rig.Trash.Thrown);
    }

    [Fact]
    public async Task What_already_lies_in_the_trash_is_said_apart_from_a_write_error()
    {
        var rig = new Rig();
        await rig.Graph.ReloadAsync(fit: false);
        rig.Trash.AlreadyThrown = true;

        rig.Graph.AskThrow([UKey]);
        await Task.Yield();

        Assert.Equal(GraphViewModel.AlreadyThrownText, rig.Toasts.Last().Title);
        Assert.Equal(ToastKind.Information, rig.Toasts.Last().Kind);
    }

    [Fact]
    public async Task A_watch_is_ended_only_after_the_throw_is_written()
    {
        var rig = new Rig();
        var watches = new TestWatchSource();
        var watch = watches.Add(WatchSamples.Subject(U, "U"), WatchSamples.Filter(WatchDirection.Out, []));
        var board = new WatchBoard(rig.Session, watches, new InlineUi());
        rig.Graph.FollowWatches(board, new WatchFormViewModel(board));
        await board.ReloadAsync();
        await rig.Graph.ReloadAsync(fit: false);
        var endedBeforeThrow = -1;
        rig.Trash.OnThrow = () => endedBeforeThrow = watches.Removed.Count;

        rig.Trash.Fails = true;
        rig.Graph.AskThrow([UKey]);
        Assert.Equal("Släng och avsluta 1 bevakning · Enter", rig.Graph.TrashWarning!.ConfirmText);
        await rig.Graph.ConfirmThrowAsync();
        Assert.Empty(watches.Removed);
        Assert.Equal(GraphViewModel.ThrowFailedText, rig.Toasts.Last().Title);

        rig.Trash.Fails = false;
        rig.Graph.AskThrow([UKey]);
        await rig.Graph.ConfirmThrowAsync();

        Assert.Equal(0, endedBeforeThrow);
        Assert.Equal((watch.Id, (string?)GraphViewModel.WatchEndedReason), Assert.Single(watches.Removed));
        var toast = rig.Toasts.Last();
        Assert.Equal("Slängde 1 aktör · bevakningen avslutad", toast.Title);
        Assert.Equal(GraphViewModel.WatchesStayEndedText, toast.Text);
    }

    [Fact]
    public async Task A_watch_that_cannot_be_ended_after_the_throw_is_said()
    {
        var rig = new Rig();
        var watches = new TestWatchSource { RemoveFails = true };
        watches.Add(WatchSamples.Subject(U, "U"), WatchSamples.Filter(WatchDirection.Out, []));
        var board = new WatchBoard(rig.Session, watches, new InlineUi());
        rig.Graph.FollowWatches(board, new WatchFormViewModel(board));
        await board.ReloadAsync();
        await rig.Graph.ReloadAsync(fit: false);

        rig.Graph.AskThrow([UKey]);
        await rig.Graph.ConfirmThrowAsync();

        Assert.Single(rig.Trash.Thrown);
        Assert.Contains(rig.Toasts, toast => toast.Title == GraphViewModel.WatchesNotEndedText);
    }

    [Fact]
    public async Task A_watch_whose_counterparty_is_thrown_is_asked_about_too()
    {
        var rig = new Rig();
        var watches = new TestWatchSource();
        var watch = watches.Add(
            WatchSamples.Subject(V), WatchSamples.Filter(WatchDirection.Out, [WatchSamples.On(U)]));
        var board = new WatchBoard(rig.Session, watches, new InlineUi());
        rig.Graph.FollowWatches(board, new WatchFormViewModel(board));
        await board.ReloadAsync();
        await rig.Graph.ReloadAsync(fit: false);

        rig.Graph.AskThrow([UKey]);

        Assert.Equal([watch.Id], Assert.Single(rig.Graph.TrashWarning!.Rows).Watches);
    }

    [Fact]
    public async Task A_thrown_start_point_is_told_with_put_back_and_never_leaves_the_graph_silent()
    {
        var rig = new Rig();
        rig.Trash.Throws.Add(ThrowOf(new TrashMember(VKey, null, "V", "Unknown") { Keys = [GraphPile.MemberKey(V)] }));

        await rig.Graph.ReloadAsync(fit: false);

        var told = rig.Toasts.Single(toast => toast.Title == GraphViewModel.StartPointThrownText);
        Assert.Equal(GraphViewModel.RestoreActionText, told.ActionText);
        await rig.Graph.ReloadAsync(fit: false);
        Assert.Single(rig.Toasts, toast => toast.Title == GraphViewModel.StartPointThrownText);

        told.Action!();
        await Task.Yield();

        Assert.Equal((1L, (string?)VKey), Assert.Single(rig.Trash.Restored));
        Assert.NotNull(rig.Graph.Scene!.Node(VKey));
    }

    [Fact]
    public async Task A_graph_whose_every_node_lies_in_the_trash_says_so()
    {
        var rig = new Rig();
        rig.Trash.Throws.Add(ThrowOf(
            new TrashMember(WKey, null, "W", "Unknown"), new TrashMember(VKey, null, "V", "Unknown"),
            new TrashMember(KKey, null, "K", "Cluster"), new TrashMember(UKey, null, "U", "Unknown"),
            new TrashMember(EKey, null, "E", "Unknown")));

        await rig.Graph.ReloadAsync(fit: false);

        Assert.Empty(rig.Graph.Scene!.Nodes);
        Assert.Equal(GraphViewModel.AllInTrashText, rig.Graph.EmptyText);
    }

    [Fact]
    public async Task Delete_with_nothing_chosen_has_nothing_to_throw()
    {
        var rig = new Rig();
        await rig.Graph.ReloadAsync(fit: false);

        Assert.False(rig.Graph.HasChosenToThrow);
        rig.Graph.Choose(UKey, null);
        Assert.True(rig.Graph.HasChosenToThrow);
    }

    [Theory]
    [InlineData(1, 0, "Slängde 1 aktör")]
    [InlineData(5, 1, "Slängde 5 aktörer · bevakningen avslutad")]
    [InlineData(3, 2, "Slängde 3 aktörer · 2 bevakningar avslutade")]
    public void The_toast_says_how_many_and_the_watches_ended(int count, int ended, string expected)
    {
        var members = Enumerable.Range(0, count)
            .Select(index => new TrashMember($"address:a{index}", null, "A", "Unknown"))
            .ToList();
        Assert.Equal(expected, GraphViewModel.ThrownText(members, ended));
    }
}

/// <summary>The bin's growth (sketch SO1a) and the tree's rows.</summary>
public sealed class TrashBinAndRowTests
{
    [Theory]
    [InlineData(0, 1)]
    [InlineData(110, 1)]
    [InlineData(270, 0.5)]
    [InlineData(430, 0)]
    [InlineData(900, 0)]
    public void The_bin_grows_as_the_pointer_comes_near(double distance, double nearness) =>
        Assert.Equal(nearness, TrashBinViewModel.NearnessAt(distance), 3);

    [Fact]
    public void Only_a_release_at_the_grown_area_throws()
    {
        var bin = new TrashBinViewModel();

        bin.DragAt(200);
        Assert.True(bin.IsGrowing);
        Assert.False(bin.IsHot);

        bin.DragAt(40);
        Assert.True(bin.IsHot);
        Assert.Equal(2 * (TrashBinViewModel.RestRadius + TrashBinViewModel.Growth), bin.Diameter, 3);

        Assert.True(bin.EndDrag());
        Assert.Equal(TrashBinViewModel.RestRadius, bin.Radius, 3);
        Assert.False(bin.EndDrag());
    }

    [Fact]
    public void The_rows_hang_on_each_other_with_corner_and_tee_and_count_what_is_under()
    {
        var thrown = new TrashThrow(
            7,
            [
                new("x", null, "X", "Unknown"), new("o1", "x", "O1", "Cluster"), new("e2", "o1", "E2", "Gambling"),
                new("o3", "x", "O3", "Unknown"), new("o4", "o3", "O4", "Nonsense"),
            ],
            new OperatorProfile("Testa", "Utredare", "Enheten"),
            DateTimeOffset.UnixEpoch,
            1);

        var rows = TrashRowViewModel.Tree(thrown.Members, member => thrown.DepthOf(member.Key), _ => [], 7);

        Assert.Equal(["", "├", "└", "└", "└"], rows.Select(row => row.Glyph));
        Assert.Equal([4, 1, 0, 1, 0], rows.Select(row => row.Below));
        Assert.Equal("Lägg tillbaka med 4 under", rows[0].RestoreText);
        Assert.Equal("Lägg tillbaka", rows[2].RestoreText);
        Assert.Equal([0, 0, 18, 0, 18], rows.Select(row => row.Indent));
        Assert.Equal(NodeSort.Gambling, rows[2].Look.Sort);
        Assert.Equal(NodeSort.Unknown, rows[4].Look.Sort);
        Assert.All(rows, row => Assert.Equal(7, row.ThrowSequence));
        Assert.Equal("slängd", rows[0].TrashRole);
        Assert.Equal("följer med", rows[1].WarningRole);
    }

    [Fact]
    public void The_view_draws_a_branch_line_through_each_row_where_the_tree_goes_on_below()
    {
        var thrown = new TrashThrow(
            7,
            [
                new("x", null, "X", "Unknown"), new("o1", "x", "O1", "Cluster"), new("e2", "o1", "E2", "Gambling"),
                new("o3", "x", "O3", "Unknown"), new("o4", "o3", "O4", "Unknown"),
            ],
            new OperatorProfile("Testa", "Utredare", "Enheten"),
            DateTimeOffset.UnixEpoch,
            1);

        var rows = TrashRowViewModel.Tree(thrown.Members, member => thrown.DepthOf(member.Key), _ => [], 7);

        var first = TrashRowViewModel.LineAt(1);
        var second = TrashRowViewModel.LineAt(2);
        Assert.Equal(TrashRowViewModel.TreeStart + TrashRowViewModel.BranchOffset, first);
        Assert.Empty(rows[0].Rails);
        Assert.Equal([first], rows[1].Rails);
        Assert.Equal([first], rows[2].Rails);
        Assert.Empty(rows[3].Rails);
        Assert.Empty(rows[4].Rails);
        Assert.Equal(second, rows[2].ElbowLeft);
        Assert.Equal(TrashRowViewModel.TreeStart + (2 * TrashRowViewModel.TreeStep), rows[4].TreePad);
    }

    [Fact]
    public void The_warning_asks_about_each_watch_and_ends_only_those_left_on()
    {
        TrashMember[] members =
        [
            new("x", null, "bc1q…x0wl", "Unknown"), new("o3", "x", "O3", "Unknown"), new("o4", "o3", "O4", "Unknown"),
        ];
        var depths = new Dictionary<string, int> { ["x"] = 0, ["o3"] = 1, ["o4"] = 2 };
        var watches = new Dictionary<string, IReadOnlyList<string>> { ["o3"] = ["w1"], ["o4"] = ["w2"] };
        var rows = TrashRowViewModel.Tree(
            members, member => depths[member.Key], member => watches.GetValueOrDefault(member.Key) ?? []);
        var warning = new TrashWarningViewModel(rows);

        Assert.True(warning.HasWatches);
        Assert.Equal(
            "2 aktörer hänger på bc1q…x0wl och nås bara genom den. 2 av dem är bevakade: välj vilka bevakningar som "
            + "ska avslutas.",
            warning.Text);
        Assert.Equal(["w1", "w2"], warning.WatchesToEnd);
        Assert.Equal("Släng och avsluta 2 bevakningar · Enter", warning.ConfirmText);
        var told = new List<string?>();
        warning.PropertyChanged += (_, e) => told.Add(e.PropertyName);

        rows[2].EndsWatches = false;

        Assert.Equal(["w1"], warning.WatchesToEnd);
        Assert.Equal("Släng och avsluta 1 bevakning · Enter", warning.ConfirmText);
        Assert.Contains(nameof(TrashWarningViewModel.ConfirmText), told);

        rows[1].EndsWatches = false;

        Assert.Equal("Släng alla 3 · Enter", warning.ConfirmText);
    }
}

/// <summary>The trash's view and the cases thrown on the start page (sketch SO1a).</summary>
public sealed class TrashViewTests
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Chainsaw/Test", TimeSpan.Zero, "Test", "Test");

    [Fact]
    public void The_time_is_said_as_today_yesterday_or_a_date()
    {
        var clock = new TestClock();
        var view = new TrashViewModel(new CaseTrash(clock), clock, Zone);

        Assert.Equal("i dag 09:14", view.When(new DateTimeOffset(2026, 11, 7, 9, 14, 0, TimeSpan.Zero)));
        Assert.Equal("i går 21:02", view.When(new DateTimeOffset(2026, 11, 6, 21, 2, 0, TimeSpan.Zero)));
        Assert.Equal("4 nov 08:00", view.When(new DateTimeOffset(2026, 11, 4, 8, 0, 0, TimeSpan.Zero)));
        Assert.Equal("4 nov 2025", view.When(new DateTimeOffset(2025, 11, 4, 8, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void An_empty_trash_says_so_and_the_newest_throw_comes_first()
    {
        var clock = new TestClock();
        var view = new TrashViewModel(new CaseTrash(clock), clock, Zone);
        Assert.True(view.IsEmpty);
        Assert.Equal("Soptunnan", view.HeadText);
        var by = new OperatorProfile("E. Berg", "Utredare", "Enheten");

        view.Show(
            [
                new TrashThrow(1, [new("a", null, "A", "Unknown")], by, clock.UtcNow, 1),
                new TrashThrow(2, [new("b", null, "B", "Unknown"), new("c", "b", "C", "Unknown")], by, clock.UtcNow, 2),
            ],
            3,
            _ => []);

        Assert.False(view.IsEmpty);
        Assert.Equal([2L, 1L], view.Groups.Select(group => group.Throw.Sequence));
        Assert.Equal("2 aktörer", view.Groups[0].Count);
        Assert.Equal("E. Berg", view.Groups[0].By);
        Assert.Equal("2 slängningar · 3 dolda i grafen", view.HeadText);
        Assert.Equal("2 slängningar · 3 dolda i grafen", view.SubText);
        Assert.Equal("2 slängningar", view.ThrowsCount);
        Assert.Equal("B och 1 till", view.Groups[0].Title);
        Assert.Equal("A", view.Groups[1].Title);
        Assert.True(view.Groups[0].HasTree);
        Assert.False(view.Groups[1].HasTree);
        Assert.Equal(view.Groups[0].When + " · E. Berg · 2 aktörer", view.Groups[0].Meta);
    }

    [Fact]
    public void Putting_everything_back_asks_for_every_throw_and_the_start_page()
    {
        var clock = new TestClock();
        var cases = new CaseTrash(clock);
        var view = new TrashViewModel(cases, clock, Zone);
        var by = new OperatorProfile("E. Berg", "Utredare", "Enheten");
        view.Show(
            [
                new TrashThrow(1, [new("a", null, "A", "Unknown")], by, clock.UtcNow, 1),
                new TrashThrow(2, [new("b", null, "B", "Unknown")], by, clock.UtcNow, 2),
            ],
            2,
            _ => []);
        var wanted = new List<(long Throw, string? Key)>();
        view.RestoreWanted += (_, each) => wanted.Add(each);

        view.RestoreEverythingCommand.Execute(null);

        Assert.Equal([(2L, (string?)null), (1L, (string?)null)], wanted);
    }

    [Fact]
    public async Task A_thrown_case_leaves_the_start_page_and_comes_back_when_put_back()
    {
        var clock = new TestClock();
        var trash = new CaseTrash(clock);
        var store = new TestCaseTrashStore();
        await trash.UseAsync(store, new InlineUi());
        var view = new TrashViewModel(trash, clock, Zone);

        Assert.True(trash.Throw("2026-0001"));
        Assert.False(trash.Throw("2026-0001"));
        await trash.Saved;

        Assert.True(view.HasCases);
        Assert.Equal("2026-0001", Assert.Single(store.Saved).CaseNumber);

        view.RestoreCaseCommand.Execute(view.Cases[0]);
        await trash.Saved;

        Assert.False(view.HasCases);
        Assert.Empty(store.Saved);
    }

    [Fact]
    public async Task A_case_thrown_before_the_saved_ones_are_read_is_kept_and_written()
    {
        var clock = new TestClock();
        var trash = new CaseTrash(clock);
        var store = new TestCaseTrashStore { Saved = [new TrashedCase("2026-0001", clock.UtcNow)] };

        trash.Throw("2026-0002");
        await trash.UseAsync(store, new InlineUi());
        await trash.Saved;

        Assert.Equal(["2026-0001", "2026-0002"], store.Saved.Select(each => each.CaseNumber));
    }

    [Fact]
    public async Task A_failed_save_is_told()
    {
        var clock = new TestClock();
        var trash = new CaseTrash(clock);
        var failures = 0;
        trash.SaveFailed += (_, _) => failures++;
        await trash.UseAsync(new TestCaseTrashStore { Refuses = true }, new InlineUi());

        trash.Throw("2026-0003");
        await trash.Saved;

        Assert.Equal(1, failures);
    }

    [Fact]
    public void The_store_keeps_the_cases_on_disk_and_reads_a_damaged_file_as_none()
    {
        var folder = Path.Combine(Path.GetTempPath(), "chainsaw-trash-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CaseTrashStore(folder);
            var when = new DateTimeOffset(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

            Assert.True(store.Save([new TrashedCase("2026-0002", when)]));
            Assert.Equal([new TrashedCase("2026-0002", when)], store.Load());

            File.WriteAllText(store.FilePath, "{ not json");
            Assert.Empty(store.Load());
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    /// <summary>
    /// The warning's tree is virtualized in a bounded box: a throw that takes most of a large graph with it builds
    /// only the rows on screen (principle 6).
    /// </summary>
    [Fact]
    public void The_warning_tree_is_virtualized()
    {
        var path = Path.Combine(GraphSceneTests.Root(), "src", "Chainsaw.App", "Views", "TrashWarningView.xaml");
        var document = System.Xml.Linq.XDocument.Load(path);
        System.Xml.Linq.XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var list = document.Descendants()
            .Single(element => (string?)element.Attribute(xaml + "Name") == "RowList");

        Assert.NotNull(list.Attribute("MaxHeight"));
        Assert.Equal("True", (string?)list.Attribute("VirtualizingPanel.IsVirtualizing"));
        Assert.Contains(list.Descendants(), element => element.Name.LocalName == "VirtualizingStackPanel");
        Assert.Contains(
            list.Descendants(),
            element => element.Name.LocalName == "ScrollViewer"
                && (string?)element.Attribute("CanContentScroll") == "True");
        Assert.DoesNotContain(list.Ancestors(), element => element.Name.LocalName == "ScrollViewer");
    }
}
