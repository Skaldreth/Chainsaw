using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>Stands in for Tracing's comments: keeps them in memory and counts what the graph asks.</summary>
internal sealed class TestGraphComments : IGraphComments
{
    private static readonly OperatorProfile Testa = new("Testa Testsson", "Utredare", "Enheten");

    private readonly Dictionary<string, GraphComment> _comments = new(StringComparer.Ordinal);

    private int _sequence;

    public List<GraphCommentDraft> Added { get; } = [];

    public List<(string Id, string Text)> Edited { get; } = [];

    public List<(string Id, string? Anchor)> Anchored { get; } = [];

    public List<string> Removed { get; } = [];

    public List<(string Id, NodePosition Place, CommentSize? Size)> Placed { get; } = [];

    /// <summary>When set, writing a comment throws this.</summary>
    public Exception? Failure { get; set; }

    /// <summary>When set, changing a comment's text throws this.</summary>
    public Exception? EditFailure { get; set; }

    /// <summary>When set, storing a place throws this.</summary>
    public Exception? PlaceFailure { get; set; }

    /// <summary>When set, writing and changing a comment wait for it, as a slow disk does.</summary>
    public TaskCompletionSource? Hold { get; set; }

    public IReadOnlyList<GraphComment> Stored => [.. _comments.Values];

    public GraphComment Seed(string text, string? anchor = null, NodePosition place = default)
    {
        var comment = Comment(Guid.NewGuid().ToString("N"), text, anchor, place);
        _comments[comment.Id] = comment;
        return comment;
    }

    public Task<IReadOnlyList<GraphComment>> GetAsync(ICase @case, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<GraphComment>>([.. _comments.Values]);

    public async Task<GraphComment> AddAsync(ICase @case, GraphCommentDraft draft)
    {
        if (Hold is { } hold)
        {
            await hold.Task;
        }

        if (Failure is { } failure)
        {
            throw failure;
        }

        Added.Add(draft);
        var comment = Comment(Guid.NewGuid().ToString("N"), draft.Text, draft.Anchor, draft.Place);
        _comments[comment.Id] = comment;
        return comment;
    }

    public async Task<GraphComment?> EditAsync(ICase @case, string id, string text)
    {
        if (Hold is { } hold)
        {
            await hold.Task;
        }

        if (EditFailure is { } failure)
        {
            throw failure;
        }

        Edited.Add((id, text));
        return Change(id, comment => comment with { Text = text });
    }

    public Task<GraphComment?> AnchorAsync(ICase @case, string id, string? anchor)
    {
        Anchored.Add((id, anchor));
        return Task.FromResult(Change(id, comment => comment with { Anchor = anchor }));
    }

    public Task<bool> RemoveAsync(ICase @case, string id)
    {
        Removed.Add(id);
        return Task.FromResult(_comments.Remove(id));
    }

    public Task<bool> SavePlaceAsync(ICase @case, string id, NodePosition place, CommentSize? size)
    {
        if (PlaceFailure is { } failure)
        {
            return Task.FromException<bool>(failure);
        }

        Placed.Add((id, place, size));
        return Task.FromResult(Change(id, comment => comment with { Place = place, Size = size }) is not null);
    }

    private GraphComment? Change(string id, Func<GraphComment, GraphComment> change)
    {
        if (!_comments.TryGetValue(id, out var comment))
        {
            return null;
        }

        return _comments[id] = change(comment);
    }

    private GraphComment Comment(string id, string text, string? anchor, NodePosition place)
    {
        var utc = new DateTimeOffset(2026, 11, 8, 13, 32, 0, TimeSpan.Zero);
        return new GraphComment(id, text, anchor, place, null, Testa, utc, Testa, utc, ++_sequence);
    }
}

/// <summary>
/// The graph's comments (sketch KN1a) without WPF: a note begun with N, a double click or the node's icon, sealed only
/// when it is left with something written, its text, thread, place and removal stored through Tracing, and its link
/// followed into the graph.
/// </summary>
public sealed class CommentTests
{
    private readonly TestGraphComments _comments = new();

    private readonly CaseSession _session = new();

    private readonly List<ToastRequest> _toasts = [];

    private async Task<GraphViewModel> Graph()
    {
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), _session);
        var graph = new GraphViewModel(
            _session, workflow, new TestGraphSource(), TestWays.For(new TestExitConnection()), new InlineUi(),
            new TestGraphEditor(), new TestClock(), comments: _comments);
        graph.ToastWanted += (_, toast) => _toasts.Add(toast);
        _session.Open(new TestCase("2026-0042"));
        await graph.ReloadAsync(fit: false);
        return graph;
    }

    [Fact]
    public async Task A_note_begun_over_a_node_is_a_draft_beside_it_tied_to_it_and_written_in()
    {
        var graph = await Graph();
        graph.Choose(KKey, null);

        var note = graph.BeginComment(new GraphPoint(5, 5));

        Assert.NotNull(note);
        Assert.True(note.IsDraft);
        Assert.True(note.IsEditing);
        Assert.Same(note, graph.Comments.Editing);
        Assert.Equal(GraphPile.MemberKey(K1), note.Anchor);
        Assert.Equal(KKey, note.AnchorNode);
        var frame = GraphSizes.NodeFrame(graph.Places[KKey]);
        Assert.True(note.Place.X > frame.Right);
        Assert.True(note.Place.Y < frame.Y);
        Assert.Empty(_comments.Added);
    }

    [Fact]
    public async Task A_note_begun_on_the_empty_surface_stands_where_it_was_asked_for_without_a_thread()
    {
        var graph = await Graph();

        var note = graph.BeginLooseComment(new GraphPoint(120, 340));

        Assert.Equal(new GraphPoint(120, 340), note!.Place);
        Assert.Null(note.Anchor);
        Assert.Equal(CommentNote.WritingFoot, note.Foot);
    }

    [Fact]
    public async Task An_empty_draft_that_is_left_is_dropped_and_nothing_is_stored()
    {
        var graph = await Graph();
        var note = graph.BeginLooseComment(default)!;
        note.Writing = "  \n ";

        await graph.Comments.LeaveAsync(note);

        Assert.Empty(graph.Comments.Notes);
        Assert.Null(graph.Comments.Editing);
        Assert.Empty(_comments.Added);
    }

    [Fact]
    public async Task A_written_draft_is_sealed_when_it_is_left()
    {
        var graph = await Graph();
        graph.Choose(VKey, null);
        var draft = graph.BeginComment(default)!;
        draft.Writing = "## Uttag via Kraken?\r\nBeloppet stämmer.\r\n";

        await graph.Comments.LeaveAsync(draft);

        var added = Assert.Single(_comments.Added);
        Assert.Equal("## Uttag via Kraken?\nBeloppet stämmer.", added.Text);
        Assert.Equal(GraphPile.MemberKey(V), added.Anchor);
        Assert.Equal(new NodePosition(draft.Place.X, draft.Place.Y), added.Place);
        var stored = Assert.Single(graph.Comments.Notes);
        Assert.False(stored.IsDraft);
        Assert.False(stored.IsEditing);
        Assert.Equal(VKey, stored.AnchorNode);
        Assert.Same(stored, graph.Comments.Selected);
        Assert.StartsWith("Testa Testsson · ", stored.Foot, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_draft_that_could_not_be_stored_keeps_its_text_and_is_written_in_again()
    {
        var graph = await Graph();
        var draft = graph.BeginLooseComment(default)!;
        draft.Writing = "Viktigt";
        _comments.Failure = new InvalidOperationException("Disken är full.");

        await graph.Comments.LeaveAsync(draft);

        Assert.True(draft.IsEditing);
        Assert.Equal("Viktigt", draft.Writing);
        Assert.Contains(_toasts, toast => toast.Title == CommentsViewModel.SaveFailedText);
    }

    [Fact]
    public async Task A_changed_text_is_sealed_and_the_same_text_writes_nothing()
    {
        var seeded = _comments.Seed("Först");
        var graph = await Graph();
        var note = Assert.Single(graph.Comments.Notes);

        graph.Comments.Edit(note);
        Assert.Equal("Först", note.Writing);
        await graph.Comments.LeaveAsync(note);
        Assert.Empty(_comments.Edited);

        graph.Comments.Edit(note);
        note.Writing = "Sedan";
        await graph.Comments.LeaveAsync(note);

        Assert.Equal((seeded.Id, "Sedan"), Assert.Single(_comments.Edited));
        Assert.Equal("Sedan", note.Text);
    }

    [Fact]
    public async Task An_emptied_note_is_removed_and_its_text_kept_in_the_case()
    {
        var seeded = _comments.Seed("Bort");
        var graph = await Graph();
        var note = Assert.Single(graph.Comments.Notes);

        graph.Comments.Edit(note);
        note.Writing = string.Empty;
        await graph.Comments.LeaveAsync(note);

        Assert.Equal(seeded.Id, Assert.Single(_comments.Removed));
        Assert.Empty(graph.Comments.Notes);
    }

    [Fact]
    public async Task Choosing_another_note_leaves_the_one_written_in_and_stores_it()
    {
        _comments.Seed("Ett");
        _comments.Seed("Två");
        var graph = await Graph();
        var first = graph.Comments.Notes[0];
        var second = graph.Comments.Notes[1];
        graph.Comments.Edit(first);
        first.Writing = "Ett, ändrad";

        graph.Comments.Edit(second);

        Assert.False(first.IsEditing);
        Assert.True(second.IsEditing);
        Assert.Equal("Ett, ändrad", Assert.Single(_comments.Edited).Text);
    }

    [Fact]
    public async Task A_drag_is_stored_when_it_ends_and_a_size_is_kept_within_the_sketch()
    {
        var seeded = _comments.Seed("Lapp");
        var graph = await Graph();
        var note = Assert.Single(graph.Comments.Notes);

        graph.Comments.Move(note, new GraphPoint(300, 40));
        graph.Comments.Resize(note, 2_000, 10);
        Assert.Empty(_comments.Placed);
        await graph.Comments.DropAsync(note);

        var placed = Assert.Single(_comments.Placed);
        Assert.Equal(seeded.Id, placed.Id);
        Assert.Equal(new NodePosition(300, 40), placed.Place);
        Assert.Equal(new CommentSize(CommentRules.MaximumWidth, CommentRules.MinimumHeight), placed.Size);
        Assert.Equal(CommentRules.MaximumWidth, note.Width);
    }

    [Fact]
    public async Task A_draft_that_is_moved_takes_its_place_with_it_when_it_is_sealed()
    {
        var graph = await Graph();
        var draft = graph.BeginLooseComment(default)!;
        graph.Comments.Move(draft, new GraphPoint(70, 80));
        await graph.Comments.DropAsync(draft);
        Assert.Empty(_comments.Placed);
        draft.Writing = "Här";

        await graph.Comments.LeaveAsync(draft);

        Assert.Equal(new NodePosition(70, 80), Assert.Single(_comments.Added).Place);
    }

    [Fact]
    public async Task A_thread_is_tied_to_another_node_and_untied()
    {
        var seeded = _comments.Seed("Tråd", GraphPile.MemberKey(V));
        var graph = await Graph();
        var note = Assert.Single(graph.Comments.Notes);
        Assert.Equal(VKey, note.AnchorNode);

        await graph.Comments.TieAsync(note, graph.Scene!.Node(KKey)!.Node);
        Assert.Equal(KKey, note.AnchorNode);
        await graph.Comments.TieAsync(note, null);

        Assert.Equal(
            [(seeded.Id, GraphPile.MemberKey(K1)), (seeded.Id, (string?)null)],
            _comments.Anchored);
        Assert.Null(note.AnchorNode);
    }

    [Fact]
    public async Task A_removed_note_leaves_the_surface_and_a_draft_is_only_dropped()
    {
        var seeded = _comments.Seed("Bort");
        var graph = await Graph();
        var draft = graph.BeginLooseComment(default)!;

        await graph.Comments.RemoveAsync(draft);
        await graph.Comments.RemoveAsync(graph.Comments.Notes.Single());

        Assert.Equal([seeded.Id], _comments.Removed);
        Assert.Empty(graph.Comments.Notes);
    }

    [Fact]
    public async Task The_notes_follow_the_case_and_are_gone_when_it_closes()
    {
        _comments.Seed("Ett");
        var graph = await Graph();
        Assert.Single(graph.Comments.Notes);

        await _session.CloseAsync();

        Assert.Empty(graph.Comments.Notes);
    }

    [Fact]
    public async Task The_node_has_a_comment_icon_and_its_menu_a_row_that_begin_a_note_tied_to_it()
    {
        var graph = await Graph();
        graph.Choose(EKey, null);

        var tool = Assert.Single(graph.NodeTools, row => row.Action == NodeMenuAction.Comment);
        Assert.Equal(GraphViewModel.CommentKey, tool.Key);
        graph.RunMenu(tool);

        Assert.Equal(EKey, Assert.Single(graph.Comments.Notes).AnchorNode);
        graph.OpenMenu(VKey);
        Assert.True(Assert.Single(graph.MenuRows, row => row.Action == NodeMenuAction.Comment).IsEnabled);
    }

    [Fact]
    public async Task A_link_in_a_note_chooses_its_node_and_says_honestly_when_it_is_not_in_the_graph()
    {
        var graph = await Graph();
        var shown = new List<IReadOnlyList<string>>();
        graph.ShowNodesWanted += (_, nodes) => shown.Add(nodes);

        graph.FollowCommentLinkCommand.Execute("chainsaw:bitcoin/" + K2.Value);
        Assert.Equal(KKey, graph.SelectedNode);
        Assert.Equal([KKey], Assert.Single(shown));

        graph.FollowCommentLinkCommand.Execute("chainsaw:bitcoin/" + Address(99).Value);
        Assert.Contains(_toasts, toast => toast.Title == GraphViewModel.LinkNotInGraphText);
        graph.FollowCommentLinkCommand.Execute("https://example.com");
        Assert.Single(shown);
    }

    [Fact]
    public async Task A_new_note_drops_an_untouched_draft_and_stores_one_being_written()
    {
        var graph = await Graph();
        var first = graph.BeginLooseComment(new GraphPoint(1, 1))!;
        first.Writing = "Skrivet";

        var second = graph.BeginLooseComment(new GraphPoint(2, 2))!;

        Assert.Equal("Skrivet", Assert.Single(_comments.Added).Text);
        Assert.Contains(second, graph.Comments.Notes);
        Assert.True(second.IsEditing);
        Assert.Equal(2, graph.Comments.Notes.Count);
    }
}

/// <summary>
/// The comments' writes after the independent review: nothing written is lost when the case closes, a write fails or
/// writes overlap, and a sealed draft is never sealed twice.
/// </summary>
public sealed class CommentWriteTests
{
    private readonly TestGraphComments _comments = new();

    private readonly CaseSession _session = new();

    private readonly List<ToastRequest> _toasts = [];

    private async Task<GraphViewModel> Graph()
    {
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), _session);
        var graph = new GraphViewModel(
            _session, workflow, new TestGraphSource(), TestWays.For(new TestExitConnection()), new InlineUi(),
            new TestGraphEditor(), new TestClock(), comments: _comments);
        graph.ToastWanted += (_, toast) => _toasts.Add(toast);
        _session.Open(new TestCase("2026-0043"));
        await graph.ReloadAsync(fit: false);
        return graph;
    }

    [Fact]
    public async Task A_draft_being_written_is_sealed_before_the_case_closes()
    {
        var graph = await Graph();
        var draft = graph.BeginLooseComment(default)!;
        draft.Writing = "Skrivet före låsningen";

        await _session.CloseAsync();

        Assert.Equal("Skrivet före låsningen", Assert.Single(_comments.Added).Text);
        Assert.Empty(graph.Comments.Notes);
        Assert.False(draft.IsEditing);
        Assert.Null(graph.Comments.Editing);
    }

    [Fact]
    public async Task A_stored_note_being_written_is_sealed_when_the_case_is_forgotten_at_a_lock()
    {
        var seeded = _comments.Seed("Först");
        var graph = await Graph();
        var note = Assert.Single(graph.Comments.Notes);
        graph.Comments.Edit(note);
        note.Writing = "Ändrad före låsningen";

        _session.Forget();

        Assert.Equal((seeded.Id, "Ändrad före låsningen"), Assert.Single(_comments.Edited));
        Assert.False(note.IsEditing);
    }

    [Fact]
    public async Task A_failed_change_gives_the_text_back_to_write_in()
    {
        _comments.Seed("Först");
        var graph = await Graph();
        var note = Assert.Single(graph.Comments.Notes);
        _comments.EditFailure = new InvalidOperationException("Disken är full.");
        graph.Comments.Edit(note);
        note.Writing = "Viktig ändring";

        await graph.Comments.LeaveAsync(note);

        Assert.True(note.IsEditing);
        Assert.Equal("Viktig ändring", note.Writing);
        Assert.Equal("Först", note.Text);
        Assert.Contains(_toasts, toast => toast.Title == CommentsViewModel.SaveFailedText);
    }

    [Fact]
    public async Task A_new_note_keeps_a_draft_being_stored_and_its_text_if_the_store_fails()
    {
        var graph = await Graph();
        var first = graph.BeginLooseComment(new GraphPoint(1, 1))!;
        first.Writing = "Under sparning";
        var hold = _comments.Hold = new TaskCompletionSource();

        var second = graph.BeginLooseComment(new GraphPoint(2, 2))!;

        Assert.True(first.IsStoring);
        Assert.Contains(first, graph.Comments.Notes);
        _comments.Failure = new InvalidOperationException("Disken är full.");
        hold.SetResult();
        await first.Pending;

        Assert.Contains(first, graph.Comments.Notes);
        Assert.True(first.IsEditing);
        Assert.Equal("Under sparning", first.Writing);
        Assert.DoesNotContain(second, graph.Comments.Notes);
    }

    [Fact]
    public async Task Writes_of_a_note_wait_for_each_other_and_it_opens_with_the_latest_text()
    {
        var seeded = _comments.Seed("A");
        var graph = await Graph();
        var note = Assert.Single(graph.Comments.Notes);
        var hold = _comments.Hold = new TaskCompletionSource();
        graph.Comments.Edit(note);
        note.Writing = "B";
        var first = graph.Comments.LeaveAsync(note);

        graph.Comments.Edit(note);
        Assert.Equal("B", note.Writing);
        note.Writing = "C";
        var second = graph.Comments.LeaveAsync(note);
        Assert.Empty(_comments.Edited);
        hold.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal([(seeded.Id, "B"), (seeded.Id, "C")], _comments.Edited);
        Assert.Equal("C", note.Text);
        Assert.False(note.IsStoring);
    }

    [Fact]
    public async Task A_draft_opened_again_while_it_is_stored_keeps_what_was_written()
    {
        var graph = await Graph();
        var draft = graph.BeginLooseComment(default)!;
        draft.Writing = "Text";
        var hold = _comments.Hold = new TaskCompletionSource();
        var leaving = graph.Comments.LeaveAsync(draft);

        graph.Comments.Edit(draft);
        Assert.Equal("Text", draft.Writing);
        hold.SetResult();
        await leaving;
        await graph.Comments.LeaveAsync(draft);

        Assert.Single(_comments.Added);
        Assert.Empty(_comments.Edited);
    }

    [Fact]
    public async Task A_sealed_draft_whose_size_could_not_be_stored_is_never_sealed_again()
    {
        var graph = await Graph();
        var draft = graph.BeginLooseComment(default)!;
        graph.Comments.Resize(draft, 400, 200);
        draft.Writing = "Stor lapp";
        _comments.PlaceFailure = new InvalidOperationException("Disken är full.");

        await graph.Comments.LeaveAsync(draft);
        graph.Comments.Edit(draft);
        await graph.Comments.LeaveAsync(draft);

        Assert.Single(_comments.Added);
        Assert.False(draft.IsDraft);
        Assert.Contains(_toasts, toast => toast.Title == CommentsViewModel.PlaceFailedText);
        Assert.DoesNotContain(_toasts, toast => toast.Title == CommentsViewModel.SaveFailedText);
    }

    [Fact]
    public async Task A_text_that_is_too_long_is_said_and_kept_without_a_write()
    {
        var graph = await Graph();
        var draft = graph.BeginLooseComment(default)!;
        var text = new string('x', CommentRules.MaximumTextLength + 1);
        draft.Writing = text;

        await graph.Comments.LeaveAsync(draft);

        Assert.Empty(_comments.Added);
        Assert.True(draft.IsEditing);
        Assert.Equal(text, draft.Writing);
        Assert.Contains(_toasts, toast => toast.Title == CommentsViewModel.TooLongText);
    }

    [Fact]
    public async Task A_move_while_a_change_is_stored_is_not_reset_by_it()
    {
        _comments.Seed("A");
        var graph = await Graph();
        var note = Assert.Single(graph.Comments.Notes);
        var hold = _comments.Hold = new TaskCompletionSource();
        graph.Comments.Edit(note);
        note.Writing = "B";
        var leaving = graph.Comments.LeaveAsync(note);

        graph.Comments.Move(note, new GraphPoint(500, 600));
        hold.SetResult();
        await leaving;

        Assert.Equal(new GraphPoint(500, 600), note.Place);
    }

    [Fact]
    public void A_link_whose_text_names_another_address_shows_where_it_goes()
    {
        var a = "chainsaw:bitcoin/" + K1.Value;
        var b = "chainsaw:bitcoin/" + K2.Value;

        Assert.True(Links.NoteLinks.Misleads(a, b));
        Assert.True(Links.NoteLinks.Misleads(K1.Value, b));
        Assert.False(Links.NoteLinks.Misleads("tx 9f3c…a1", b));
        Assert.False(Links.NoteLinks.Misleads(" " + b, b));
    }
}

/// <summary>Where a comment stands and what its thread ties (sketch KN1a).</summary>
public sealed class CommentGeometryTests
{
    [Fact]
    public void A_thread_runs_between_the_edges_of_the_note_and_the_node()
    {
        var note = new GraphRect(200, 0, 100, 100);
        var node = new GraphRect(0, 30, 40, 40);

        var (atNote, atNode) = CommentGeometry.Thread(note, node);

        Assert.Equal(new GraphPoint(200, 50), atNote);
        Assert.Equal(new GraphPoint(40, 50), atNode);
    }

    [Fact]
    public void The_edge_toward_the_middle_itself_is_the_middle()
    {
        var frame = new GraphRect(0, 0, 10, 10);

        Assert.Equal(frame.Centre, CommentGeometry.Edge(frame, frame.Centre));
        Assert.Equal(new GraphPoint(5, 0), CommentGeometry.Edge(frame, new GraphPoint(5, -100)));
    }

    [Fact]
    public void A_new_note_stands_up_and_to_the_right_of_its_node_or_where_it_was_asked_for()
    {
        Assert.Equal(new GraphPoint(9, 9), CommentGeometry.PlaceOfNew(null, new GraphPoint(9, 9)));
        var beside = CommentGeometry.PlaceOfNew(new GraphPoint(0, 200), new GraphPoint(9, 9));
        Assert.True(beside.X > GraphSizes.NodeWidth);
        Assert.True(beside.Y < 200);
    }

    [Fact]
    public void A_thread_finds_its_node_by_key_or_by_an_address_of_a_cluster_and_none_otherwise()
    {
        var scene = Scene();

        Assert.Equal(VKey, CommentGeometry.Resolve(scene, VKey));
        Assert.Equal(KKey, CommentGeometry.Resolve(scene, GraphPile.MemberKey(K2)));
        Assert.Null(CommentGeometry.Resolve(scene, GraphPile.MemberKey(Address(99))));
        Assert.Null(CommentGeometry.Resolve(scene, "address:inte-en-adress"));
        Assert.Null(CommentGeometry.Resolve(scene, null));
        Assert.Null(CommentGeometry.Resolve(null, VKey));
    }

    [Fact]
    public void A_thread_to_an_address_or_a_cluster_is_stored_under_an_address_and_to_anything_else_under_its_key()
    {
        var scene = Scene();

        Assert.Equal(GraphPile.MemberKey(K1), CommentGeometry.AnchorOf(scene.Node(KKey)!.Node));
        Assert.Equal(GraphPile.MemberKey(V), CommentGeometry.AnchorOf(scene.Node(VKey)!.Node));
    }
}
