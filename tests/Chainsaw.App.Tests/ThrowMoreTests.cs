using Chainsaw.App.Graph;
using Chainsaw.App.Settings;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// What could not be thrown before (sketch SO1a): a comment (sketch KN1a) and an own area (sketch KF1b) go in the
/// graph's trash, sealed, are only hidden and come back when put back; nothing about them is removed from the case.
/// </summary>
public sealed class GraphThrowMoreTests
{
    /// <summary>The graph with comments, checks and a trash in memory, a case open.</summary>
    private sealed class Rig
    {
        public Rig()
        {
            Workflow = new CaseWorkflow(new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), Session);
            Graph = new GraphViewModel(
                Session, Workflow, new TestGraphSource(), TestWays.For(new TestExitConnection()), new InlineUi(),
                new TestGraphEditor(), new TestClock(), checks: Checks, comments: Comments, trash: Trash);
            Graph.ToastWanted += (_, toast) => Toasts.Add(toast);
        }

        public CaseSession Session { get; } = new();

        public CaseWorkflow Workflow { get; }

        public TestGraphComments Comments { get; } = new();

        public TestTrashSource Trash { get; } = new();

        public TestCheckSource Checks { get; } = new();

        public GraphViewModel Graph { get; }

        public List<ToastRequest> Toasts { get; } = [];

        public async Task OpenAsync()
        {
            Session.Open(new TestCase("2026-0042"));
            await Graph.ReloadAsync(fit: false);
            await Graph.Comments.LoadAsync();
        }

        public OwnArea SeedArea(string name = "Bedragarens plånbok")
        {
            var area = new OwnArea(
                "own-9", name, AreaSort.Own, AreaTint.Green, true, "Samma mönster",
                [CheckRules.AddressMember + U.Value], CheckSamples.Who, DateTimeOffset.UnixEpoch, 9);
            Checks.Areas.Add(area);
            return area;
        }
    }

    [Fact]
    public async Task A_thrown_note_is_hidden_said_with_undo_and_comes_back_with_ctrl_z()
    {
        var rig = new Rig();
        var seeded = rig.Comments.Seed("Uttag via Kraken?", GraphPile.MemberKey(U));
        await rig.OpenAsync();
        var note = Assert.Single(rig.Graph.Comments.Notes);
        rig.Graph.Comments.Select(note);

        await rig.Graph.Comments.ThrowAsync(note);

        var member = Assert.Single(Assert.Single(rig.Trash.Thrown));
        Assert.Equal(TrashKeys.OfComment(seeded.Id), member.Key);
        Assert.Equal("Uttag via Kraken?", member.Name);
        Assert.True(note.IsInTrash);
        Assert.Null(rig.Graph.Comments.Selected);
        var toast = Assert.Single(rig.Toasts);
        Assert.Equal("Slängde 1 kommentar", toast.Title);
        Assert.Equal(GraphViewModel.UndoText, toast.ActionText);
        Assert.True(rig.Graph.Bin.HasThrows);
        Assert.Equal(1, rig.Graph.TrashView.HiddenInGraph);

        await rig.Graph.UndoThrowAsync();

        Assert.False(note.IsInTrash);
        Assert.Equal(UKey, note.AnchorNode);
        Assert.Single(rig.Comments.Stored);
        Assert.Empty(rig.Trash.Throws);
    }

    [Fact]
    public async Task A_thrown_note_stays_hidden_when_the_case_is_read_again_and_is_put_back_from_the_trash_view()
    {
        var rig = new Rig();
        rig.Comments.Seed("Lapp");
        await rig.OpenAsync();
        var note = Assert.Single(rig.Graph.Comments.Notes);
        await rig.Graph.Comments.ThrowAsync(note);

        await rig.Graph.ReloadAsync(fit: false);
        await rig.Graph.Comments.LoadAsync();
        var again = Assert.Single(rig.Graph.Comments.Notes);
        Assert.True(again.IsInTrash);

        var group = Assert.Single(rig.Graph.TrashView.Groups);
        Assert.Equal("1 kommentar", group.Count);
        var row = Assert.Single(group.Rows);
        Assert.Equal("Icon.Note", row.Look.Icon);
        rig.Graph.TrashView.RestoreCommand.Execute(row);
        await Task.Yield();

        Assert.False(again.IsInTrash);
    }

    [Fact]
    public async Task A_note_that_cannot_be_thrown_stays_on_the_surface_and_says_so()
    {
        var rig = new Rig();
        rig.Comments.Seed("Lapp");
        await rig.OpenAsync();
        var note = Assert.Single(rig.Graph.Comments.Notes);
        rig.Trash.Fails = true;

        await rig.Graph.Comments.ThrowAsync(note);

        Assert.False(note.IsInTrash);
        Assert.Contains(rig.Toasts, toast => toast.Title == CommentsViewModel.ThrowFailedText);
        Assert.Empty(rig.Trash.Throws);
    }

    [Fact]
    public async Task A_note_already_in_the_trash_is_only_hidden_without_a_second_throw()
    {
        var rig = new Rig();
        rig.Comments.Seed("Lapp");
        await rig.OpenAsync();
        var note = Assert.Single(rig.Graph.Comments.Notes);
        rig.Trash.AlreadyThrown = true;

        await rig.Graph.Comments.ThrowAsync(note);

        Assert.True(note.IsInTrash);
        Assert.Empty(rig.Toasts);
    }

    [Fact]
    public async Task A_note_dragged_onto_the_hot_bin_is_thrown_and_one_let_go_away_from_it_is_not()
    {
        var rig = new Rig();
        rig.Comments.Seed("Lapp");
        await rig.OpenAsync();
        var note = Assert.Single(rig.Graph.Comments.Notes);

        rig.Graph.DragNear(600);
        Assert.False(rig.Graph.DropNoteOnBin(note));
        Assert.Empty(rig.Trash.Thrown);

        rig.Graph.DragNear(0);
        Assert.True(rig.Graph.Bin.IsHot);
        Assert.True(rig.Graph.DropNoteOnBin(note));
        await note.Pending;

        Assert.True(note.IsInTrash);
        Assert.Single(rig.Trash.Thrown);
        Assert.False(rig.Graph.Bin.IsDragging);
    }

    [Fact]
    public async Task A_note_emptied_while_the_case_closes_is_thrown_before_it_closes()
    {
        var rig = new Rig();
        var seeded = rig.Comments.Seed("Bort före stängningen");
        await rig.OpenAsync();
        var note = Assert.Single(rig.Graph.Comments.Notes);
        rig.Graph.Comments.Edit(note);
        note.Writing = "  ";

        await rig.Session.CloseAsync();

        Assert.Equal(TrashKeys.OfComment(seeded.Id), Assert.Single(Assert.Single(rig.Trash.Thrown)).Key);
        Assert.Empty(rig.Comments.Edited);
    }

    [Fact]
    public async Task A_draft_thrown_is_only_dropped_and_nothing_goes_in_the_trash()
    {
        var rig = new Rig();
        await rig.OpenAsync();
        var draft = rig.Graph.BeginLooseComment(default)!;

        await rig.Graph.Comments.ThrowAsync(draft);

        Assert.Empty(rig.Graph.Comments.Notes);
        Assert.Empty(rig.Trash.Thrown);
        Assert.Empty(rig.Toasts);
    }

    [Fact]
    public async Task A_chosen_own_area_is_thrown_with_delete_hidden_and_put_back_while_its_actors_stay()
    {
        var rig = new Rig();
        var area = rig.SeedArea();
        await rig.OpenAsync();
        var key = GraphAreas.OwnKey(area.Id);
        Assert.Contains(rig.Graph.Checks.AllAreas, each => each.Key == key);
        rig.Graph.Choose(VKey, null);
        rig.Graph.Checks.ChooseArea(key);
        Assert.Same(area, rig.Graph.ChosenOwnArea);
        Assert.True(rig.Graph.HasChosenToThrow);

        rig.Graph.ThrowChosen();
        await Task.Yield();

        var member = Assert.Single(Assert.Single(rig.Trash.Thrown));
        Assert.Equal(key, member.Key);
        Assert.Equal("Bedragarens plånbok", member.Name);
        Assert.Equal(TrashKeys.AreaSort, member.Sort);
        Assert.DoesNotContain(rig.Graph.Checks.AllAreas, each => each.Key == key);
        Assert.NotNull(rig.Graph.Scene!.Node(UKey));
        Assert.Equal(VKey, rig.Graph.SelectedNode);
        Assert.Null(rig.Graph.Checks.ChosenArea);
        Assert.Equal("Slängde 1 yta", Assert.Single(rig.Toasts).Title);
        Assert.Empty(rig.Checks.Removed);
        Assert.Single(rig.Checks.Areas);

        await rig.Graph.UndoThrowAsync();

        Assert.Contains(rig.Graph.Checks.AllAreas, each => each.Key == key);
    }

    [Fact]
    public async Task Without_a_chosen_area_delete_throws_the_chosen_node_as_before()
    {
        var rig = new Rig();
        rig.SeedArea();
        await rig.OpenAsync();
        rig.Graph.Choose(UKey, null);

        rig.Graph.ThrowChosen();
        await Task.Yield();

        Assert.Equal(UKey, Assert.Single(Assert.Single(rig.Trash.Thrown)).Key);
    }

    [Fact]
    public void The_trash_draws_a_comment_and_an_area_in_their_own_form()
    {
        Assert.Equal("Icon.Note", TrashRowViewModel.LookOf(TrashKeys.CommentSort).Icon);
        Assert.Equal("Icon.Area", TrashRowViewModel.LookOf(TrashKeys.AreaSort).Icon);
        Assert.Equal(NodeShape.Square, TrashRowViewModel.LookOf(TrashKeys.CommentSort).Shape);
        Assert.Equal(VisualLanguage.Of(NodeSort.Unknown), TrashRowViewModel.LookOf("Ingen sådan sort"));
    }

    [Theory]
    [InlineData(0, 0, 0, "0 aktörer")]
    [InlineData(1, 0, 0, "1 aktör")]
    [InlineData(0, 1, 0, "1 kommentar")]
    [InlineData(0, 2, 0, "2 kommentarer")]
    [InlineData(0, 0, 2, "2 ytor")]
    [InlineData(3, 0, 1, "3 aktörer och 1 yta")]
    [InlineData(2, 1, 1, "2 aktörer, 1 kommentar och 1 yta")]
    public void The_count_never_calls_a_comment_or_an_area_an_actor(int actors, int comments, int areas, string text)
    {
        var members = Enumerable.Range(0, actors).Select(index => new TrashMember($"address:a{index}", null, "A", "Unknown"))
            .Concat(Enumerable.Range(0, comments).Select(index => TrashKeys.Comment($"c{index}", "Text")))
            .Concat(Enumerable.Range(0, areas).Select(index => TrashKeys.Area($"o{index}", "Yta")))
            .ToList();

        Assert.Equal(text, TrashViewModel.CountText(members));
        Assert.Equal("Slängde " + text, GraphViewModel.ThrownText(members, 0));
    }

    [Fact]
    public void A_comment_without_a_line_of_text_is_called_a_comment_in_the_trash()
    {
        Assert.Equal(TrashKeys.CommentName, TrashKeys.Comment("c1", "   ").Name);
        Assert.Equal("Rubrik", TrashKeys.Comment("c1", "# Rubrik\nmer").Name);
        Assert.True(TrashKeys.IsComment(TrashKeys.Comment("c1", "x")));
        Assert.False(TrashKeys.IsArea(TrashKeys.Comment("c1", "x")));
        Assert.True(TrashKeys.IsArea(TrashKeys.Area("o1", "Yta")));
    }
}

/// <summary>
/// A comment chosen on the surface is shown in the inspector with its history (sketches KN1a and AN1b): its sealed
/// records and its throws and puttings back, in time order, read only.
/// </summary>
public sealed class CommentInspectorTests
{
    private static readonly OperatorProfile Testa = new("Testa Testsson", "Utredare", "Enheten");

    private static readonly OperatorProfile Kim = new("Kim Kollega", "Analytiker", "Enheten");

    /// <summary>A time before the fake trash's records, which start at the epoch.</summary>
    private static DateTimeOffset At(int minute) => DateTimeOffset.UnixEpoch.AddHours(-1).AddMinutes(minute);

    private static GraphCommentRecord Record(long sequence, CommentAction action, string text, string? anchor, int minute,
        OperatorProfile? by = null) => new(sequence, action, text, anchor, by ?? Testa, At(minute), sequence + 100);

    private static TrashRecord Trashed(long sequence, TrashAction action, int minute) =>
        new(sequence, action, 1, Kim, At(minute), sequence + 200);

    private sealed class Rig
    {
        public Rig()
        {
            Workflow = new CaseWorkflow(new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), Session);
            Graph = new GraphViewModel(
                Session, Workflow, new TestGraphSource(), TestWays.For(new TestExitConnection()), new InlineUi(),
                new TestGraphEditor(), new TestClock(), comments: Comments, trash: Trash);
        }

        public CaseSession Session { get; } = new();

        public CaseWorkflow Workflow { get; }

        public TestGraphComments Comments { get; } = new();

        public TestTrashSource Trash { get; } = new();

        public GraphViewModel Graph { get; }

        public async Task OpenAsync()
        {
            Session.Open(new TestCase("2026-0042"));
            await Graph.ReloadAsync(fit: false);
            await Graph.Comments.LoadAsync();
        }
    }

    [Fact]
    public void The_history_lists_writing_changes_threads_and_the_trash_in_time_order()
    {
        var data = new CommentHistoryData(
            [
                Record(1, CommentAction.Added, "## Uttag via Kraken?\nmer", "address:x", 0),
                Record(2, CommentAction.Edited, "Begäran skickad", "address:x", 5, Kim),
                Record(3, CommentAction.Anchored, "Begäran skickad", "address:y", 7),
                Record(4, CommentAction.Anchored, "Begäran skickad", null, 9),
            ],
            [Trashed(10, TrashAction.Thrown, 6), Trashed(11, TrashAction.Restored, 6)]);

        var rows = CommentHistory.Build(data, anchor => anchor == "address:y" ? "Kraken" : null);

        Assert.Equal(
            [
                CommentHistory.WrittenText, CommentHistory.EditedText, CommentHistory.ThrownText,
                CommentHistory.RestoredText, CommentHistory.TiedText + "Kraken", CommentHistory.UntiedText,
            ],
            rows.Select(row => row.Text));
        Assert.Equal("Testa Testsson · ”Uttag via Kraken?”", rows[0].Sub);
        Assert.Equal("Kim Kollega · ”Begäran skickad”", rows[1].Sub);
        Assert.Equal("Kim Kollega", rows[2].Sub);
        Assert.All(rows, row => Assert.False(row.CanUndo));
        Assert.All(rows, row => Assert.Equal(HistoryKind.Comment, row.Kind));
        Assert.Equal(rows.Count, rows.Select(row => row.Key).Distinct().Count());
    }

    [Fact]
    public void A_thread_to_a_node_not_shown_and_a_removal_from_an_older_case_are_said_honestly()
    {
        var data = new CommentHistoryData(
            [
                Record(1, CommentAction.Anchored, "Text", "address:gone", 1),
                Record(2, CommentAction.Removed, "Text", null, 2),
            ],
            []);

        var rows = CommentHistory.Build(data, _ => null);

        Assert.Equal(CommentHistory.TiedText + CommentHistory.NotInGraphText, rows[0].Text);
        Assert.Equal(CommentHistory.RemovedText, rows[1].Text);
    }

    [Fact]
    public async Task A_chosen_note_fills_the_inspector_and_its_history_tab_with_its_records_and_throws()
    {
        var rig = new Rig();
        var seeded = rig.Comments.Seed("Uttag via Kraken?", GraphPile.MemberKey(U));
        rig.Comments.History[seeded.Id] =
        [
            Record(1, CommentAction.Added, "Uttag", GraphPile.MemberKey(U), 0),
            Record(2, CommentAction.Edited, "Uttag via Kraken?", GraphPile.MemberKey(U), 3),
        ];
        await rig.OpenAsync();
        var note = Assert.Single(rig.Graph.Comments.Notes);
        await rig.Graph.Comments.ThrowAsync(note);
        await rig.Graph.UndoThrowAsync();

        rig.Graph.Comments.Select(note);

        Assert.True(rig.Graph.IsInspectorShown);
        var shown = rig.Graph.Inspection!;
        Assert.Equal("Uttag via Kraken?", shown.Title);
        Assert.Equal(InspectorContent.CommentWord, shown.Subtitle);
        Assert.True(shown.IsComment);
        Assert.False(shown.HasWhy);
        Assert.Contains(shown.Facts, fact => fact.Name == "Tråd till" && fact.Value == rig.Graph.Scene!.Node(UKey)!.Name);
        Assert.Contains(shown.Facts, fact => fact.Name == "Skriven av" && fact.Value == "Testa Testsson");
        Assert.True(rig.Graph.History.HasNode);
        Assert.Equal(
            [
                CommentHistory.WrittenText, CommentHistory.EditedText, CommentHistory.ThrownText,
                CommentHistory.RestoredText,
            ],
            rig.Graph.History.Rows.Select(row => row.Event.Text));
        Assert.Equal("4", rig.Graph.History.CountText);
    }

    [Fact]
    public async Task Choosing_a_note_lets_go_of_the_chosen_node_and_choosing_a_node_lets_go_of_the_note()
    {
        var rig = new Rig();
        rig.Comments.Seed("Lapp");
        await rig.OpenAsync();
        var note = Assert.Single(rig.Graph.Comments.Notes);
        rig.Graph.Choose(UKey, null);

        rig.Graph.Comments.Select(note);

        Assert.Null(rig.Graph.SelectedNode);
        Assert.Same(note, rig.Graph.Comments.Selected);
        Assert.Equal("Lapp", rig.Graph.Inspection!.Title);

        rig.Graph.Choose(VKey, null);

        Assert.Null(rig.Graph.Comments.Selected);
        Assert.False(rig.Graph.Inspection!.IsComment);
        Assert.True(rig.Graph.Inspection.HasWhy);
    }

    [Fact]
    public async Task The_inspector_follows_a_new_text_and_lets_go_when_the_note_is_let_go_or_closed()
    {
        var rig = new Rig();
        rig.Comments.Seed("Först");
        await rig.OpenAsync();
        var note = Assert.Single(rig.Graph.Comments.Notes);
        rig.Graph.Comments.Edit(note);
        note.Writing = "Sedan";

        await rig.Graph.Comments.LeaveAsync(note);

        Assert.Equal("Sedan", rig.Graph.Inspection!.Title);

        rig.Graph.Deselect();

        Assert.Null(rig.Graph.Comments.Selected);
        Assert.Null(rig.Graph.Inspection);
        Assert.False(rig.Graph.IsInspectorShown);
    }

    [Fact]
    public async Task A_draft_has_no_inspector_until_it_is_sealed()
    {
        var rig = new Rig();
        await rig.OpenAsync();

        rig.Graph.BeginLooseComment(default);

        Assert.False(rig.Graph.HasChosenComment);
        Assert.Null(rig.Graph.Inspection);
        Assert.False(rig.Graph.History.HasNode);
    }
}

/// <summary>
/// Throwing on the start page beyond a loose case (sketches SO1a and GR8a): a whole pile, and a case that lies in a pile.
/// Both are kept on this computer and come back where they lay; no file is touched.
/// </summary>
public sealed class StartPileTrashTests
{
    private sealed class MemoryPiles : ICasePileStore
    {
        public IReadOnlyList<CasePile> Saved { get; set; } = [];

        public IReadOnlyList<CasePile> Load() => Saved;

        public bool Save(IReadOnlyList<CasePile> piles)
        {
            Saved = piles;
            return true;
        }
    }

    private sealed class Rig
    {
        public Rig()
        {
            Workflow = new CaseWorkflow(Store, new TestTracing(), new TestVault(VaultState.Unlocked), Session);
            Start = new GraphStartViewModel(Session, Workflow, new InlineUi(), Clock, TimeZoneInfo.Utc);
            Trash = new CaseTrash(Clock);
            Start.UseCaseTrash(Trash);
            Start.PileThrown += (_, pile) => PilesThrown.Add(pile);
            Start.CardsThrown += (_, cards) => CardsThrown.Add(cards);
        }

        public TestClock Clock { get; } = new();

        public TestCaseStore Store { get; } = new();

        public CaseSession Session { get; } = new();

        public CaseWorkflow Workflow { get; }

        public GraphStartViewModel Start { get; }

        public CaseTrash Trash { get; }

        public MemoryPiles Piles { get; } = new();

        public List<CasePileItem> PilesThrown { get; } = [];

        public List<IReadOnlyList<string>> CardsThrown { get; } = [];

        public IEnumerable<string> Loose => Start.Recent.Select(item => item.CaseNumber);

        public IReadOnlyList<CasePileItem> RingPiles => [.. Start.Ring.OfType<CasePileItem>()];

        /// <summary>Lists five cases and piles the first three, named.</summary>
        public async Task<CasePileItem> PileAsync()
        {
            string[] numbers = ["2026-0131", "2026-0129", "2026-0124", "2026-0122", "2026-0101"];
            for (var index = 0; index < numbers.Length; index++)
            {
                Store.AddFile(numbers[index], minutesAgo: 60 * (index + 1));
            }

            await Workflow.RefreshCasesAsync();
            await Start.UsePilesAsync(Piles);
            Start.Stack("2026-0129", "2026-0131");
            var pile = Assert.Single(RingPiles);
            Start.AddToPile("2026-0124", pile.Id);
            Start.NamePile(pile.Id, "Väntar på börs");
            return Assert.Single(RingPiles);
        }
    }

    [Fact]
    public async Task A_lifted_pile_dropped_on_the_hot_bin_leaves_the_start_page_whole_and_comes_back_whole()
    {
        var rig = new Rig();
        var pile = await rig.PileAsync();
        rig.Start.LiftPile(pile.Id);
        Assert.True(rig.Start.IsLifting);
        rig.Start.LiftNear(0);
        Assert.True(rig.Start.Bin.IsHot);

        Assert.True(rig.Start.DropPileOnBin(pile.Id));
        rig.Start.Drop();

        Assert.False(rig.Start.IsLifting);
        Assert.Empty(rig.RingPiles);
        Assert.Equal("Väntar på börs", Assert.Single(rig.PilesThrown).ShownName);
        Assert.Equal(["2026-0124", "2026-0129", "2026-0131"], rig.Trash.Cases.Select(each => each.CaseNumber).Order());
        Assert.All(rig.Trash.Cases, each => Assert.Equal(pile.Id, each.PileId));
        Assert.DoesNotContain("2026-0129", rig.Loose);
        Assert.True(rig.Start.Bin.HasThrows);
        Assert.Single(rig.Start.Piles);

        rig.Start.RestorePile(pile.Id);

        var back = Assert.Single(rig.RingPiles);
        Assert.Equal(pile.Id, back.Id);
        Assert.Equal(3, back.Count);
        Assert.Equal("Väntar på börs", back.ShownName);
        Assert.Empty(rig.Trash.Cases);
    }

    [Fact]
    public async Task A_pile_let_go_away_from_the_bin_is_not_thrown()
    {
        var rig = new Rig();
        var pile = await rig.PileAsync();
        rig.Start.LiftPile(pile.Id);
        rig.Start.LiftNear(600);

        Assert.False(rig.Start.DropPileOnBin(pile.Id));

        Assert.Empty(rig.PilesThrown);
        Assert.Single(rig.RingPiles);
    }

    [Fact]
    public async Task A_card_dragged_from_an_open_pile_onto_the_bin_is_thrown_and_comes_back_into_its_pile()
    {
        var rig = new Rig();
        var pile = await rig.PileAsync();
        rig.Start.OpenPileOf(pile.Id);
        rig.Start.CardsNear(0);
        Assert.True(rig.Start.Bin.IsHot);

        Assert.True(rig.Start.DropCardsOnBin(["2026-0129"]));

        Assert.False(rig.Start.IsPileOpen);
        Assert.Equal(["2026-0129"], Assert.Single(rig.CardsThrown));
        Assert.Equal(2, Assert.Single(rig.RingPiles).Count);
        Assert.Null(Assert.Single(rig.Trash.Cases).PileId);
        Assert.DoesNotContain("2026-0129", rig.Loose);

        rig.Start.RestoreCases(["2026-0129"]);

        Assert.Equal(3, Assert.Single(rig.RingPiles).Count);
        Assert.DoesNotContain("2026-0129", rig.Loose);
    }

    [Fact]
    public async Task Cards_let_go_away_from_the_bin_are_not_thrown_and_without_an_open_pile_the_bin_stays()
    {
        var rig = new Rig();
        var pile = await rig.PileAsync();
        rig.Start.CardsNear(0);
        Assert.False(rig.Start.Bin.IsDragging);

        rig.Start.OpenPileOf(pile.Id);
        rig.Start.CardsNear(600);

        Assert.False(rig.Start.DropCardsOnBin(["2026-0129"]));
        Assert.Empty(rig.Trash.Cases);
        Assert.True(rig.Start.IsPileOpen);
    }

    [Fact]
    public async Task Delete_throws_the_chosen_cards_and_a_pile_left_with_none_leaves_the_start_page()
    {
        var rig = new Rig();
        var pile = await rig.PileAsync();
        rig.Start.OpenPileOf(pile.Id);
        Assert.False(rig.Start.ThrowChosenCards());
        foreach (var card in rig.Start.OpenCards)
        {
            rig.Start.ToggleCard(card.CaseNumber);
        }

        Assert.True(rig.Start.ThrowChosenCards());

        Assert.Equal(3, Assert.Single(rig.CardsThrown).Count);
        Assert.Empty(rig.RingPiles);
        Assert.Single(rig.Start.Piles);

        rig.Start.RestoreCases(Assert.Single(rig.CardsThrown));

        Assert.Equal(3, Assert.Single(rig.RingPiles).Count);
    }

    [Fact]
    public async Task The_trash_view_lists_a_thrown_pile_as_a_tree_and_puts_it_back_from_there()
    {
        var rig = new Rig();
        var pile = await rig.PileAsync();
        rig.Start.LiftPile(pile.Id);
        rig.Start.LiftNear(0);
        rig.Start.DropPileOnBin(pile.Id);
        rig.Trash.Throw("2026-0101");
        var view = new TrashViewModel(rig.Trash, rig.Clock, TimeZoneInfo.Utc);

        Assert.True(view.HasCases);
        Assert.False(view.IsEmpty);
        Assert.Equal(["2026-0101"], view.Cases.Select(row => row.CaseNumber));
        var shown = Assert.Single(view.Piles);
        Assert.Equal("Väntar på börs", shown.Name);
        Assert.Equal("3 ärenden", shown.CountText);
        Assert.Equal(
            [TrashRowViewModel.MiddleGlyph, TrashRowViewModel.MiddleGlyph, TrashRowViewModel.LastGlyph],
            shown.Cases.Select(row => row.Glyph));

        view.RestoreCaseCommand.Execute(shown.Cases[0]);
        Assert.Equal(2, Assert.Single(view.Piles).Cases.Count);

        view.RestorePileCommand.Execute(view.Piles[0]);

        Assert.Empty(view.Piles);
        Assert.Equal(3, Assert.Single(rig.RingPiles).Count);
        Assert.Single(view.Cases);
    }

    [Fact]
    public void A_pile_whose_cases_all_lie_in_the_trash_already_is_not_thrown_again()
    {
        var trash = new CaseTrash(new TestClock());
        trash.Throw("A");

        Assert.Equal(["B"], trash.ThrowPile("p1", "Hög", ["A", "B"]));
        Assert.Empty(trash.ThrowPile("p1", "Hög", ["A", "B"]));
        Assert.True(trash.RestorePile("p1"));
        Assert.False(trash.RestorePile("p1"));
        Assert.Equal(["A"], trash.Cases.Select(each => each.CaseNumber));
    }

    [Fact]
    public void A_pile_thrown_whole_is_kept_on_this_computer_and_an_older_file_without_piles_is_read()
    {
        var folder = Path.Combine(Path.GetTempPath(), "chainsaw-trash-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new CaseTrashStore(folder);
            var thrown = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
            Assert.True(store.Save([new TrashedCase("A", thrown, "p1", "Hög"), new TrashedCase("B", thrown)]));

            Assert.Equal([new TrashedCase("A", thrown, "p1", "Hög"), new TrashedCase("B", thrown)], store.Load());

            File.WriteAllText(store.FilePath, """{ "Cases": [ { "Case": "C", "Thrown": "2026-11-08T09:00:00+00:00" } ] }""");
            Assert.Equal([new TrashedCase("C", thrown)], store.Load());
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [Fact]
    public async Task The_graph_says_a_thrown_pile_and_thrown_cards_with_undo()
    {
        var session = new CaseSession();
        var store = new TestCaseStore();
        var workflow = new CaseWorkflow(store, new TestTracing(), new TestVault(VaultState.Unlocked), session);
        var graph = new GraphViewModel(
            session, workflow, new TestGraphSource(), TestWays.For(new TestExitConnection()), new InlineUi(),
            new TestGraphEditor(), new TestClock());
        var toasts = new List<ToastRequest>();
        graph.ToastWanted += (_, toast) => toasts.Add(toast);
        store.AddFile("A1", minutesAgo: 10);
        store.AddFile("A2", minutesAgo: 20);
        store.AddFile("A3", minutesAgo: 30);
        await workflow.RefreshCasesAsync();
        var start = graph.Start;
        start.Stack("A1", "A2");
        var pile = Assert.Single(start.Ring.OfType<CasePileItem>());
        start.NamePile(pile.Id, "Hög");

        start.LiftPile(pile.Id);
        start.LiftNear(0);
        start.DropPileOnBin(pile.Id);

        var toast = Assert.Single(toasts);
        Assert.Equal("Högen Hög ligger i soptunnan", toast.Title);
        Assert.Equal(GraphViewModel.PileThrownText, toast.Text);
        toast.Action!();
        Assert.Single(start.Ring.OfType<CasePileItem>());

        start.OpenPileOf(pile.Id);
        start.CardsNear(0);
        start.DropCardsOnBin(["A1", "A2"]);

        Assert.Equal("2 ärenden ligger i soptunnan", toasts[^1].Title);
        Assert.Equal("Ärendet A1 ligger i soptunnan", GraphViewModel.CardsThrownTitle(["A1"]));
        toasts[^1].Action!();
        Assert.Equal(2, Assert.Single(start.Ring.OfType<CasePileItem>()).Count);
    }
}
