using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Checks.Contracts;
using static Chainsaw.App.Tests.CheckSamples;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// The checks and suggestions one at a time (sketch KF2b) and the own areas (sketch KF1b) without WPF.
/// </summary>
public sealed class ChecksViewModelTests
{
    /// <summary>A case open, the checks and the graph.</summary>
    private sealed class Rig
    {
        public Rig()
        {
            Session.Open(Case);
            Checks = new ChecksViewModel(Session, Source) { TimeZone = TimeZoneInfo.Utc };
        }

        public CaseSession Session { get; } = new();

        public TestCase Case { get; } = new("2026-0022");

        public TestCheckSource Source { get; } = new();

        public ChecksViewModel Checks { get; }

        public async Task<Rig> Follow()
        {
            await Checks.FollowAsync(Scene());
            return this;
        }
    }

    [Fact]
    public async Task Suggestions_wait_first_obfuscation_is_done_by_itself_and_a_wallet_waits_with_its_area()
    {
        var rig = new Rig();
        rig.Source.Checks.Add(Wallet());
        await rig.Follow();

        Assert.Equal(
            ["Troliga utgångar ur mixern", "Samma plånbok troligt"], rig.Checks.ToDecide.Select(row => row.Title));
        Assert.Equal(["CoinJoin, Wasabi"], rig.Checks.Done.Select(row => row.Title));
        Assert.Equal("Förslag · 2", rig.Checks.ButtonText);
        Assert.Contains(rig.Checks.AllAreas, area => area.Sort == AreaSort.Wallet);
        var wallet = rig.Checks.ToDecide.Single(row => row.Check.Analysis.Name == CheckAnalysisNames.Wallets);
        Assert.False(wallet.HasCandidates);
        Assert.Same(SuggestionWords.Wallets, wallet.Words);
        Assert.Equal("Godkänn", wallet.GoText);
    }

    [Fact]
    public async Task Approving_a_wallet_seals_it_as_a_whole_and_asks_for_the_clusters_to_be_read_again()
    {
        var rig = new Rig();
        rig.Source.Checks.Clear();
        rig.Source.Checks.Add(Wallet());
        await rig.Follow();
        var changed = 0;
        rig.Checks.ClustersChanged += (_, _) => changed++;
        rig.Checks.StartReview(null);
        rig.Checks.Selected!.Reason = "Samma inloggning";
        ToastRequest? toast = null;
        rig.Checks.ToastWanted += (_, request) => toast = request;

        await rig.Checks.ApproveAsync();

        var request = Assert.Single(rig.Source.Decisions);
        Assert.Equal(CheckAnalysisNames.Wallets, request.Analysis.Name);
        Assert.Null(request.CandidateKey);
        Assert.Equal(CheckVerdict.Approved, request.Verdict);
        Assert.Equal("Samma inloggning", request.Reason);
        Assert.Equal(1, changed);
        Assert.Equal("Förslaget godkänt", toast!.Title);
    }

    [Theory]
    [InlineData(Modules.Clustering.Contracts.MergeRefusal.AcrossSplit, ChecksViewModel.SplitFirstText)]
    [InlineData(Modules.Clustering.Contracts.MergeRefusal.BreaksMerge, ChecksViewModel.BreaksMergeText)]
    public async Task A_refused_wallet_says_why_and_reads_nothing_again(
        Modules.Clustering.Contracts.MergeRefusal refusal, string text)
    {
        var rig = new Rig();
        rig.Source.Checks.Clear();
        rig.Source.Checks.Add(Wallet());
        await rig.Follow();
        var changed = 0;
        rig.Checks.ClustersChanged += (_, _) => changed++;
        rig.Source.DecideFailure = new Modules.Clustering.Contracts.MergeRefusedException(refusal);
        rig.Checks.StartReview(null);

        await rig.Checks.ApproveAsync();

        Assert.Equal(text, rig.Checks.Problem);
        Assert.Empty(rig.Source.Decisions);
        Assert.Equal(0, changed);
        Assert.Equal(1, rig.Checks.WaitingCount);
    }

    [Fact]
    public async Task Dismissing_a_wallet_leaves_the_clusters_alone()
    {
        var rig = new Rig();
        rig.Source.Checks.Clear();
        rig.Source.Checks.Add(Wallet());
        await rig.Follow();
        var changed = 0;
        rig.Checks.ClustersChanged += (_, _) => changed++;
        rig.Checks.StartReview(null);

        await rig.Checks.DismissAsync();

        Assert.Equal(CheckVerdict.Dismissed, Assert.Single(rig.Source.Decisions).Verdict);
        Assert.Equal(0, changed);
    }

    [Theory]
    [InlineData(CheckVerdict.Approved, true)]
    [InlineData(CheckVerdict.Withdrawn, true)]
    [InlineData(CheckVerdict.Dismissed, false)]
    public void Only_a_wallets_approval_or_its_take_back_changes_the_clusters(CheckVerdict verdict, bool changes)
    {
        var wallet = new Modules.Clustering.Contracts.HeuristicId(CheckAnalysisNames.Wallets, 1);

        Assert.Equal(changes, ChecksViewModel.ChangesClusters(new DecisionRequest(wallet, "w", null, verdict)));
        Assert.False(ChecksViewModel.ChangesClusters(new DecisionRequest(MixerId, "deposit:0", "c", verdict)));
    }

    [Fact]
    public async Task Suggestions_wait_first_and_obfuscation_is_done_by_itself()
    {
        var rig = await new Rig().Follow();

        Assert.Equal(["Troliga utgångar ur mixern"], rig.Checks.ToDecide.Select(row => row.Title));
        Assert.Equal(["CoinJoin, Wasabi"], rig.Checks.Done.Select(row => row.Title));
        Assert.Empty(rig.Checks.Decided);
        Assert.Equal("Förslag · 1", rig.Checks.ButtonText);
        Assert.Equal("1", rig.Checks.WaitingText);
        Assert.Equal("Kördes 2026-11-07 13:02 på 4 tx", rig.Checks.StatusText);
    }

    [Fact]
    public async Task Ctrl_J_opens_the_overview_and_Enter_goes_through_the_queue_one_at_a_time()
    {
        var rig = await new Rig().Follow();

        rig.Checks.Toggle();
        Assert.True(rig.Checks.IsOverview);
        Assert.Null(rig.Checks.Focus);
        Assert.Equal("Gå igenom 1 att avgöra", rig.Checks.StartText);
        await rig.Checks.ApproveAsync();

        Assert.True(rig.Checks.IsReview);
        Assert.Empty(rig.Source.Decisions);
        Assert.Same(rig.Checks.ToDecide[0], rig.Checks.Selected);
        Assert.Equal("1 av 1", rig.Checks.PositionText);
        Assert.Equal(new HashSet<string> { VKey, UKey, EKey }, rig.Checks.Focus);
        Assert.Equal("mixer-outputs v1/deposit:0", Assert.Single(rig.Checks.Areas).CheckKey);
    }

    [Fact]
    public async Task The_suggestion_shows_its_flow_its_chips_and_its_ranked_candidates()
    {
        var rig = await new Rig().Follow();
        rig.Checks.StartReview(null);
        var row = rig.Checks.Selected!;

        Assert.Equal("1,0000 BTC in · 1 nov 12:00", row.Meta);
        Assert.Equal(["Belopp − avgift", "Tid", "Adresstyp"], row.Basis.Select(chip => chip.Label));
        Assert.Equal("Mixerutgångar · v1", row.AnalysisText);
        Assert.Equal(["1", "2"], row.Candidates.Select(c => c.RankText));
        Assert.Equal(["1 h", "2 h"], row.Candidates.Select(c => c.TimeText));
        Assert.Equal("Godkänn 1", row.GoText);
        Assert.Equal("1,0000 in", row.Flow.InText);
        Assert.Equal([true, false], row.Flow.Bands.Select(band => band.IsChosen));
        Assert.True(row.Flow.Bands[0].Width > row.Flow.Bands[1].Width);

        rig.Checks.ChooseCandidate(row.Candidates[1]);

        Assert.Equal("Godkänn 2", row.GoText);
        Assert.Equal([false, true], row.Flow.Bands.Select(band => band.IsChosen));
        Assert.Contains(rig.Checks.CandidateLines, line => line is { Rank: 2, IsChosen: true });
    }

    [Fact]
    public async Task Enter_approves_the_chosen_candidate_with_the_reason_and_says_so_with_undo()
    {
        var rig = await new Rig().Follow();
        rig.Checks.StartReview(null);
        rig.Checks.ToggleReason();
        rig.Checks.Selected!.Reason = "  Samma belopp ";
        ToastRequest? toast = null;
        rig.Checks.ToastWanted += (_, request) => toast = request;

        await rig.Checks.ApproveAsync();

        var request = Assert.Single(rig.Source.Decisions);
        Assert.Equal(CheckVerdict.Approved, request.Verdict);
        Assert.Equal(Candidate(U, 1, 0.8).Key, request.CandidateKey);
        Assert.Equal("Samma belopp", request.Reason);
        Assert.False(rig.Checks.IsReasonOpen);
        Assert.Equal("Trolig utgång godkänd", toast!.Title);
        Assert.Equal(ChecksViewModel.UndoText, toast.ActionText);
        var line = Assert.Single(rig.Checks.CandidateLines, l => l.IsApproved);
        Assert.Equal(UKey, line.To);

        // A mixer pays out in several parts: the suggestion stays with the next candidate chosen.
        Assert.Equal("Förslag · 1", rig.Checks.ButtonText);
        Assert.False(rig.Checks.IsSettled);
        Assert.Equal("Troliga utgångar ur mixern", rig.Checks.Selected!.Title);
        Assert.Equal("Godkänn 2", rig.Checks.Selected.GoText);
        Assert.Equal("Avfärda resten", rig.Checks.Selected.DismissText);
    }

    [Fact]
    public async Task Several_candidates_are_approved_and_X_dismisses_only_the_rest()
    {
        var rig = await new Rig().Follow();
        rig.Checks.StartReview(null);
        await rig.Checks.ApproveAsync();
        ToastRequest? toast = null;
        rig.Checks.ToastWanted += (_, request) => toast = request;

        await rig.Checks.DismissAsync();

        Assert.Equal(
            [(Candidate(U, 1, 0.8).Key, CheckVerdict.Approved), (Candidate(E, 2, 0.5).Key, CheckVerdict.Dismissed)],
            rig.Source.Decisions.Select(d => (d.CandidateKey, d.Verdict)));
        Assert.True(rig.Checks.IsSettled);
        Assert.Equal("Resten avfärdat", toast!.Title);
        Assert.Equal([true], rig.Checks.Ring);
        Assert.Equal("1 godkänd · inget nytt sedan 13:02", rig.Checks.SettledText);
    }

    [Fact]
    public async Task Ctrl_Z_takes_the_decision_back_sealed_and_the_suggestion_waits_again()
    {
        var rig = await new Rig().Follow();
        rig.Checks.StartReview(null);
        await rig.Checks.ApproveAsync();
        Assert.True(rig.Checks.CanUndoLast);

        await rig.Checks.UndoAsync();

        Assert.Equal(
            [CheckVerdict.Approved, CheckVerdict.Withdrawn], rig.Source.Decisions.Select(decision => decision.Verdict));
        Assert.Equal(rig.Source.Decisions[0].CandidateKey, rig.Source.Decisions[1].CandidateKey);
        Assert.False(rig.Checks.CanUndoLast);
        Assert.True(rig.Checks.IsReview);
        Assert.Equal("Troliga utgångar ur mixern", rig.Checks.Selected!.Title);
        Assert.Equal("Förslag · 1", rig.Checks.ButtonText);
    }

    [Fact]
    public async Task Undo_names_the_decision_it_takes_back_and_says_so()
    {
        var rig = await new Rig().Follow();
        rig.Checks.StartReview(null);
        var toasts = new List<ToastRequest>();
        rig.Checks.ToastWanted += (_, request) => toasts.Add(request);
        await rig.Checks.ApproveAsync();

        await rig.Checks.UndoAsync();

        Assert.Equal(1, rig.Source.Decisions[1].Undoes);
        Assert.Equal(2, rig.Source.Batches);
        Assert.Equal(ChecksViewModel.UndoneText, toasts[^1].Title);
    }

    [Fact]
    public async Task An_old_toasts_undo_never_takes_back_a_newer_decision()
    {
        var rig = await new Rig().Follow();
        rig.Checks.StartReview(null);
        var toasts = new List<ToastRequest>();
        rig.Checks.ToastWanted += (_, request) => toasts.Add(request);
        await rig.Checks.ApproveAsync();
        var first = toasts[0];
        await rig.Checks.UndoAsync();
        await rig.Checks.DismissAsync();

        first.Action!();
        await Task.Yield();

        Assert.Equal(
            [CheckVerdict.Approved, CheckVerdict.Withdrawn, CheckVerdict.Dismissed],
            rig.Source.Decisions.Select(decision => decision.Verdict));
        Assert.True(rig.Checks.IsSettled);
    }

    [Fact]
    public async Task Undo_after_another_case_is_opened_writes_nothing()
    {
        var rig = await new Rig().Follow();
        rig.Checks.StartReview(null);
        ToastRequest? toast = null;
        rig.Checks.ToastWanted += (_, request) => toast = request;
        await rig.Checks.ApproveAsync();

        rig.Session.Open(new TestCase("2026-0023"));
        await rig.Follow();
        toast!.Action!();
        await Task.Yield();

        Assert.Single(rig.Source.Decisions);
    }

    [Fact]
    public async Task Ctrl_Z_with_the_panel_closed_takes_nothing_back()
    {
        var rig = await new Rig().Follow();
        rig.Checks.StartReview(null);
        await rig.Checks.ApproveAsync();
        rig.Checks.Toggle();
        Assert.False(rig.Checks.IsOpen);
        Assert.False(rig.Checks.CanUndoLast);

        await rig.Checks.UndoAsync();

        Assert.Single(rig.Source.Decisions);
    }

    [Fact]
    public async Task Ctrl_Z_after_the_log_took_it_back_does_nothing_and_fails_nothing()
    {
        var rig = await new Rig().Follow();
        rig.Checks.StartReview(null);
        await rig.Checks.DismissAsync();
        await rig.Checks.UndoRowAsync(rig.Checks.Decided[0]);

        await rig.Checks.UndoAsync();

        Assert.Equal(2, rig.Source.Decisions.Count);
        Assert.False(rig.Checks.CanUndoLast);
        Assert.Equal(string.Empty, rig.Checks.Problem);
    }

    [Fact]
    public async Task A_suggestion_just_decided_never_waits_again_while_an_older_run_is_shown()
    {
        var rig = await new Rig().Follow();
        rig.Checks.StartReview(null);
        var decisionHeld = new TaskCompletionSource();
        rig.Source.HoldDecision = decisionHeld;
        var deciding = rig.Checks.DismissAsync();

        // The graph changes while the decision is stored: a run reads the case before the decision is sealed, and
        // the run after the decision is overtaken by a newer one that is still running.
        await rig.Checks.FollowAsync(Scene());
        var afterDecision = new TaskCompletionSource();
        rig.Source.HoldRun = afterDecision;
        decisionHeld.SetResult();
        await Task.Yield();
        var newest = new TaskCompletionSource();
        rig.Source.HoldRun = newest;
        var running = rig.Checks.FollowAsync(Scene());
        afterDecision.SetResult();
        await deciding;

        await rig.Checks.DismissAsync();

        Assert.Single(rig.Source.Decisions);
        newest.SetResult();
        await running;
        Assert.Empty(rig.Checks.ToDecide);
    }

    [Fact]
    public async Task X_dismisses_the_suggestion_as_a_whole_and_the_log_takes_it_back()
    {
        var rig = await new Rig().Follow();
        rig.Checks.StartReview(null);

        await rig.Checks.DismissAsync();

        var request = Assert.Single(rig.Source.Decisions);
        Assert.Null(request.CandidateKey);
        Assert.Equal(CheckVerdict.Dismissed, request.Verdict);
        Assert.True(rig.Checks.IsSettled);
        var row = Assert.Single(rig.Checks.Decided);
        Assert.Equal(CheckRowViewModel.DismissedText, row.StateText);
        Assert.DoesNotContain(rig.Checks.AllAreas, area => area.CheckKey == "mixer-outputs v1/deposit:0");
        Assert.NotEmpty(row.Nodes);
        Assert.Equal("1 avfärdad · inget nytt sedan 13:02", rig.Checks.SettledText);

        await rig.Checks.UndoRowAsync(row);

        Assert.Equal(CheckVerdict.Withdrawn, rig.Source.Decisions[^1].Verdict);
        Assert.Null(rig.Source.Decisions[^1].CandidateKey);
        Assert.Single(rig.Checks.ToDecide);
    }

    [Fact]
    public async Task Senare_moves_the_suggestion_to_the_back_without_writing_anything()
    {
        var rig = new Rig();
        rig.Source.Checks.Add(Split());
        await rig.Follow();
        rig.Checks.StartReview(null);
        Assert.Equal("Troliga utgångar ur mixern", rig.Checks.Selected!.Title);

        rig.Checks.Later();

        Assert.Equal("Uppdelning i små belopp", rig.Checks.Selected!.Title);
        Assert.Equal("1 av 2", rig.Checks.PositionText);
        Assert.Equal(
            ["Uppdelning i små belopp", "Troliga utgångar ur mixern"], rig.Checks.ToDecide.Select(r => r.Title));
        Assert.Empty(rig.Source.Decisions);
        Assert.Equal(1, rig.Source.Runs);
    }

    [Fact]
    public async Task After_a_decision_the_next_waits_and_the_progress_bar_shows_it()
    {
        var rig = new Rig();
        rig.Source.Checks.Add(Split());
        await rig.Follow();
        rig.Checks.StartReview(null);
        Assert.Equal([SegmentState.Current, SegmentState.Left], rig.Checks.Segments.Select(s => s.State));

        await rig.Checks.ApproveAsync();
        await rig.Checks.DismissAsync();

        Assert.Equal("Uppdelning i små belopp", rig.Checks.Selected!.Title);
        Assert.Equal("Godkänn", rig.Checks.Selected.GoText);
        Assert.Equal("2 av 2", rig.Checks.PositionText);
        Assert.Equal([SegmentState.Decided, SegmentState.Current], rig.Checks.Segments.Select(s => s.State));

        rig.Checks.Previous();
        Assert.Equal("Uppdelning i små belopp", rig.Checks.Selected!.Title);

        await rig.Checks.DismissAsync();

        Assert.True(rig.Checks.IsSettled);
        Assert.Equal([true, false], rig.Checks.Ring);
        Assert.Equal("1 godkänd · 1 avfärdad · inget nytt sedan 14:40", rig.Checks.SettledText);
    }

    [Fact]
    public async Task Enter_and_X_are_ignored_until_the_decision_is_stored_and_read_again()
    {
        var rig = new Rig();
        rig.Source.Checks.Add(Split());
        await rig.Follow();
        rig.Checks.StartReview(null);
        var hold = new TaskCompletionSource();
        rig.Source.HoldDecision = hold;

        var first = rig.Checks.ApproveAsync();
        await rig.Checks.DismissAsync();
        await rig.Checks.ApproveAsync();
        hold.SetResult();
        await first;

        Assert.Single(rig.Source.Decisions);
        Assert.False(rig.Checks.IsBusy);
    }

    [Fact]
    public async Task With_nothing_to_decide_the_panel_says_all_is_decided()
    {
        var rig = new Rig();
        rig.Source.Checks.RemoveAt(0);
        await rig.Follow();

        rig.Checks.Toggle();

        Assert.True(rig.Checks.IsSettled);
        Assert.Empty(rig.Checks.Ring);
        Assert.Equal("Inget att avgöra · inget nytt sedan 13:02", rig.Checks.SettledText);
        Assert.Equal(CheckRowViewModel.MarkedText, rig.Checks.Done[0].StateText);
        Assert.False(rig.Checks.Done[0].CanUndo);
    }

    [Fact]
    public async Task An_answer_about_an_earlier_graph_is_dropped()
    {
        var rig = new Rig();
        var hold = new TaskCompletionSource();
        rig.Source.HoldRun = hold;
        var late = rig.Checks.FollowAsync(Scene());
        rig.Source.Checks.RemoveAt(0);

        await rig.Checks.FollowAsync(Scene());
        hold.SetResult();
        await late;

        Assert.Empty(rig.Checks.ToDecide);
        Assert.Equal("Förslag · 0", rig.Checks.ButtonText);
    }

    [Fact]
    public async Task A_hidden_sort_leaves_its_fill_to_the_areas_shown()
    {
        var rig = new Rig();
        rig.Source.Checks.Add(Wallet());
        await rig.Follow();
        ShowEverySort(rig);
        Assert.False(rig.Checks.Areas.Single(area => area.Sort == AreaSort.Wallet).IsFilled);

        rig.Checks.ToggleSort(AreaSort.Obfuscation);

        Assert.True(rig.Checks.Areas.Single(area => area.Sort == AreaSort.Wallet).IsFilled);
    }

    [Fact]
    public async Task Renaming_an_area_that_is_gone_says_so()
    {
        var rig = new Rig();
        rig.Source.Areas.Add(Own("Säljaren", U));
        rig.Source.RenameFindsNothing = true;
        await rig.Follow();
        var key = rig.Checks.AllAreas.Single(area => area.IsOwn).Key;

        rig.Checks.OpenRename(key);
        rig.Checks.FormName = "Kontot";
        await rig.Checks.SaveFormAsync();

        Assert.Equal(ChecksViewModel.GoneText, rig.Checks.FormProblem);
        Assert.True(rig.Checks.IsFormOpen);
    }

    [Fact]
    public async Task The_question_mark_asks_the_Guide_why_with_the_candidates_and_the_seal()
    {
        var rig = await new Rig().Follow();
        rig.Checks.StartReview(null);
        GuideRequest? asked = null;
        rig.Checks.GuideWanted += (_, request) => asked = request;

        rig.Checks.AskWhy();

        Assert.Equal(GuideTopic.MixerCandidates, asked!.Topic);
        var lines = asked.Context!.Lines;
        Assert.Equal(4, lines.Count);
        Assert.Contains("aldrig ett faktum", lines[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("0,80 · ", lines[1].Text, StringComparison.Ordinal);
        Assert.Contains("Mixerutgångar · v1 förseglade det 2026-11-07 13:02", lines[3].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_menu_hides_and_shows_a_sorts_areas_and_counts_them()
    {
        var rig = await new Rig().Follow();
        ShowEverySort(rig);
        var before = rig.Checks.Areas.Count;

        rig.Checks.ToggleSort(AreaSort.Time);

        Assert.DoesNotContain(rig.Checks.Areas, area => area.Sort == AreaSort.Time);
        Assert.Equal(before - 1, rig.Checks.Areas.Count);
        var time = rig.Checks.Sorts.Single(sort => sort.Sort == AreaSort.Time);
        Assert.False(time.IsShown);
        Assert.Equal("1", time.CountText);
        Assert.Equal("inga", rig.Checks.Sorts.Single(sort => sort.Sort == AreaSort.Service).CountText);
    }

    /// <summary>
    /// No area is drawn until the menu picks its sort (TODO 34): drawn all at once they lay on top of each other. A
    /// suggestion's area is drawn while the suggestion is shown.
    /// </summary>
    [Fact]
    public async Task No_area_is_drawn_until_the_menu_picks_it()
    {
        var rig = await new Rig().Follow();

        Assert.NotEmpty(rig.Checks.AllAreas);
        Assert.Empty(rig.Checks.Areas);
        Assert.False(rig.Checks.HasAreas);
        Assert.All(rig.Checks.Sorts, sort => Assert.False(sort.IsShown));

        rig.Checks.ToggleSort(AreaSort.Time);
        Assert.Equal([AreaSort.Time], rig.Checks.Areas.Select(area => area.Sort).Distinct());
        rig.Checks.ToggleSort(AreaSort.Time);
        Assert.Empty(rig.Checks.Areas);

        rig.Checks.Toggle();
        rig.Checks.StartReview(null);
        Assert.Equal(rig.Checks.Selected!.Key, Assert.Single(rig.Checks.Areas).CheckKey);
    }

    /// <summary>Shows every sort's areas, as when the user picks them all in the menu.</summary>
    /// <param name="rig">The rig.</param>
    private static void ShowEverySort(Rig rig)
    {
        foreach (var sort in rig.Checks.Sorts.Where(sort => !sort.IsShown).Select(sort => sort.Sort).ToList())
        {
            rig.Checks.ToggleSort(sort);
        }
    }

    [Fact]
    public async Task Z_draws_an_own_area_around_the_actors_chosen_with_ctrl_click()
    {
        var rig = await new Rig().Follow();
        rig.Checks.TogglePick(KKey);
        rig.Checks.TogglePick(UKey);

        rig.Checks.OpenCreate(null);
        Assert.Equal("2 valda · Ny yta", rig.Checks.FormTitle);
        rig.Checks.FormName = " Säljaren på P2P ";
        rig.Checks.FormNote = "Samma tid.";
        rig.Checks.ChooseTint(AreaTint.Sand);
        rig.Checks.FormSameActor = true;
        await rig.Checks.SaveFormAsync();

        var draft = Assert.Single(rig.Source.Added);
        Assert.Equal("Säljaren på P2P", draft.Name);
        Assert.Equal(AreaTint.Sand, draft.Tint);
        Assert.True(draft.SameActor);
        Assert.Equal([.. new[] { K1, K2, U }.Select(CheckRules.MemberOf)], draft.Members);
        Assert.False(rig.Checks.IsFormOpen);
        Assert.Empty(rig.Checks.Picked);
        Assert.Contains(rig.Checks.Areas, area => area.IsOwn && area.Label == "Säljaren på P2P");
    }

    [Fact]
    public async Task Z_without_ctrl_click_takes_the_chosen_actor_and_without_any_it_says_so()
    {
        var rig = await new Rig().Follow();

        rig.Checks.OpenCreate(null);
        Assert.Equal(ChecksViewModel.PickFirstText, rig.Checks.FormProblem);
        rig.Checks.CancelForm();
        rig.Checks.OpenCreate(VKey);

        Assert.Equal([VKey], rig.Checks.Picked);
        Assert.Equal(string.Empty, rig.Checks.FormProblem);
    }

    [Fact]
    public async Task An_own_area_needs_a_name()
    {
        var rig = await new Rig().Follow();
        rig.Checks.OpenCreate(VKey);

        await rig.Checks.SaveFormAsync();

        Assert.Empty(rig.Source.Added);
        Assert.Equal("Ytan behöver ett namn.", rig.Checks.FormProblem);
        Assert.True(rig.Checks.IsFormOpen);
    }

    [Fact]
    public async Task An_own_area_is_renamed_and_removed_from_its_chip()
    {
        var rig = new Rig();
        rig.Source.Areas.Add(Own("Säljaren", U));
        await rig.Follow();
        var key = rig.Checks.AllAreas.Single(area => area.IsOwn).Key;

        rig.Checks.ChooseArea(key);
        rig.Checks.OpenRename(key);
        Assert.Equal("Säljaren", rig.Checks.FormName);
        rig.Checks.FormName = "Kontot";
        await rig.Checks.SaveFormAsync();
        rig.Checks.OpenRemove(key);
        Assert.Equal("Skäl, valfritt", rig.Checks.NoteLabel);
        await rig.Checks.SaveFormAsync();

        Assert.Equal(("own-1", "Kontot"), Assert.Single(rig.Source.Renamed));
        Assert.Equal(("own-1", (string?)null), Assert.Single(rig.Source.Removed));
        Assert.DoesNotContain(rig.Checks.AllAreas, area => area.IsOwn);
    }

    [Fact]
    public async Task A_suggestions_areas_chip_opens_the_panel_at_it_and_a_checks_only_names_it()
    {
        var rig = await new Rig().Follow();

        rig.Checks.ChooseArea($"coinjoin-area v1/{Tx[2].Value}");
        Assert.False(rig.Checks.IsOpen);
        Assert.Equal($"coinjoin-area v1/{Tx[2].Value}", rig.Checks.ChosenArea);
        rig.Checks.ChooseArea("mixer-outputs v1/deposit:0");

        Assert.True(rig.Checks.IsOpen);
        Assert.True(rig.Checks.IsReview);
        Assert.Equal("Troliga utgångar ur mixern", rig.Checks.Selected!.Title);
    }

    [Fact]
    public async Task A_check_in_the_overview_shows_its_actors_and_decides_nothing()
    {
        var rig = await new Rig().Follow();
        rig.Checks.Toggle();
        IReadOnlyList<string>? shown = null;
        rig.Checks.ShowWanted += (_, nodes) => shown = nodes;

        rig.Checks.Select(rig.Checks.Done[0]);

        Assert.Equal([KKey, EKey], shown);
        Assert.Empty(rig.Source.Decisions);
        Assert.True(rig.Checks.IsOverview);
    }

    [Fact]
    public async Task A_failed_run_says_so_honestly()
    {
        var rig = new Rig();
        rig.Source.RunFailure = new InvalidOperationException("Ingen profil.");

        await rig.Follow();

        Assert.Equal(ChecksViewModel.RunFailedText, rig.Checks.StatusText);
        Assert.Empty(rig.Checks.ToDecide);
    }

    [Fact]
    public async Task A_failed_run_after_a_good_one_drops_the_former_checks()
    {
        var rig = await new Rig().Follow();
        Assert.NotEmpty(rig.Checks.ToDecide);
        rig.Source.RunFailure = new InvalidOperationException("Ingen profil.");

        await rig.Follow();

        Assert.Equal(ChecksViewModel.RunFailedText, rig.Checks.StatusText);
        Assert.Empty(rig.Checks.ToDecide);
        Assert.Empty(rig.Checks.Done);
        Assert.DoesNotContain(rig.Checks.AllAreas, area => area.Sort == AreaSort.Wallet);
    }

    [Fact]
    public async Task The_inspector_lists_what_concerns_the_chosen_actor()
    {
        var rig = await new Rig().Follow();

        rig.Checks.Inspect(EKey);

        Assert.Equal(
            [("Troliga utgångar ur mixern", "väntar"), ("CoinJoin, Wasabi", "kontroll")],
            rig.Checks.Here.Select(line => (line.Title, line.State)));
    }

    [Fact]
    public async Task Another_case_forgets_everything()
    {
        var rig = await new Rig().Follow();
        rig.Checks.Toggle();
        rig.Checks.TogglePick(UKey);

        rig.Session.Open(new TestCase("2026-0023"));

        Assert.False(rig.Checks.IsOpen);
        Assert.Empty(rig.Checks.ToDecide);
        Assert.Empty(rig.Checks.Picked);
        Assert.Empty(rig.Checks.AllAreas);
        Assert.Equal(ChecksViewModel.NotRunText, rig.Checks.StatusText);
    }

    [Fact]
    public async Task The_candidates_lines_go_from_the_mixer_and_leave_out_the_dismissed()
    {
        var rig = new Rig();
        rig.Source.Checks[0] = Mixer(Decision(Candidate(E, 2, 0.5).Key, CheckVerdict.Dismissed));
        await rig.Follow();

        var line = Assert.Single(rig.Checks.CandidateLines);

        Assert.Equal((VKey, UKey, "0,80"), (line.From, line.To, line.ScoreText));
    }
}
