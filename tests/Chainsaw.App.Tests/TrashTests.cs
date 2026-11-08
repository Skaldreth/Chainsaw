using Chainsaw.App.Graph;
using Chainsaw.App.Settings;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
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

    public Task<IReadOnlyList<TrashThrow>> ReadAsync(ICase @case, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<TrashThrow>>([.. Throws]);

    public Task<TrashThrow> ThrowAsync(ICase @case, IReadOnlyList<TrashMember> members)
    {
        if (Fails)
        {
            return Task.FromException<TrashThrow>(new CaseException(CaseError.NoOperatorProfile));
        }

        Thrown.Add(members);
        var thrown = new TrashThrow(
            _next++, members, new OperatorProfile("Testa", "Utredare", "Enheten"), DateTimeOffset.UnixEpoch, 1);
        Throws.Add(thrown);
        return Task.FromResult(thrown);
    }

    public Task<IReadOnlyList<TrashMember>> RestoreAsync(ICase @case, long throwSequence, string? key)
    {
        Restored.Add((throwSequence, key));
        var index = Throws.FindIndex(each => each.Sequence == throwSequence);
        if (index < 0)
        {
            return Task.FromResult<IReadOnlyList<TrashMember>>([]);
        }

        var back = key is null ? Throws[index].Members : Throws[index].Branch(key);
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

    public IReadOnlyList<TrashedCase> Load() => Saved;

    public bool Save(IReadOnlyList<TrashedCase> cases)
    {
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
    public void A_cluster_is_found_again_by_any_of_its_addresses()
    {
        var member = new TrashMember("cluster:other", null, "K", "Cluster") { Keys = [GraphPile.MemberKey(K2)] };

        var scene = Scene().WithTrash(new TrashFilter([ThrowOf(member)]));

        Assert.Null(scene.Node(KKey));
        Assert.Contains(new TrashStub(VKey, true), scene.TrashStubs);
        Assert.Contains(new TrashStub(EKey, false), scene.TrashStubs);
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

    [Theory]
    [InlineData(1, 0, "Slängde 1 aktör")]
    [InlineData(5, 1, "Slängde 5 aktörer · bevakningen avslutad")]
    [InlineData(3, 2, "Slängde 3 aktörer · 2 bevakningar avslutade")]
    public void The_toast_says_how_many_and_the_watches_ended(int count, int ended, string expected) =>
        Assert.Equal(expected, GraphViewModel.ThrownText(count, ended));
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

        rows[2].EndsWatches = false;

        Assert.Equal(["w1"], warning.WatchesToEnd);
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
}
