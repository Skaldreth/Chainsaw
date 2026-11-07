using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Checks.Contracts;
using static Chainsaw.App.Tests.CheckSamples;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>The list of checks and suggestions (sketch KF1a) and the own areas (sketch KF1b) without WPF.</summary>
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
    public async Task Suggestions_wait_first_obfuscation_is_done_by_itself_and_wallets_are_only_areas()
    {
        var rig = await new Rig().Follow();

        Assert.Equal(["Troliga utgångar ur mixern"], rig.Checks.ToDecide.Select(row => row.Title));
        Assert.Equal(["CoinJoin, Wasabi"], rig.Checks.Done.Select(row => row.Title));
        Assert.Empty(rig.Checks.Decided);
        Assert.Equal("Förslag · 2", rig.Checks.ButtonText);
        Assert.Equal("Kördes 2026-11-07 13:02 på 4 tx", rig.Checks.StatusText);
        Assert.Contains(rig.Checks.AllAreas, area => area.Sort == AreaSort.Wallet);
    }

    [Fact]
    public async Task Opening_the_list_chooses_the_first_row_and_tones_down_all_but_its_actors()
    {
        var rig = await new Rig().Follow();

        rig.Checks.Toggle();

        Assert.True(rig.Checks.IsOpen);
        Assert.Same(rig.Checks.ToDecide[0], rig.Checks.Selected);
        Assert.Equal(new HashSet<string> { VKey, UKey, EKey }, rig.Checks.Focus);
        Assert.Equal("mixer-outputs v1/deposit:0", Assert.Single(rig.Checks.Areas).CheckKey);
    }

    [Fact]
    public async Task J_and_K_step_through_the_rows_and_stop_at_the_ends()
    {
        var rig = await new Rig().Follow();
        rig.Checks.Toggle();

        rig.Checks.Next();
        Assert.Equal("CoinJoin, Wasabi", rig.Checks.Selected!.Title);
        rig.Checks.Next();
        Assert.Equal("CoinJoin, Wasabi", rig.Checks.Selected!.Title);
        rig.Checks.Previous();
        Assert.Equal("Troliga utgångar ur mixern", rig.Checks.Selected!.Title);
    }

    [Fact]
    public async Task Enter_approves_the_best_candidate_with_the_reason_and_the_next_waits()
    {
        var rig = await new Rig().Follow();
        rig.Checks.Toggle();
        rig.Checks.Selected!.Reason = "  Samma belopp ";

        Assert.Equal("Godkänn bästa", rig.Checks.Selected.GoText);
        await rig.Checks.ApproveAsync();

        var request = Assert.Single(rig.Source.Decisions);
        Assert.Equal(CheckVerdict.Approved, request.Verdict);
        Assert.Equal(Candidate(U, 1, 0.8).Key, request.CandidateKey);
        Assert.Equal("Samma belopp", request.Reason);
        Assert.Equal("Förslag · 1", rig.Checks.ButtonText);
        Assert.True(rig.Checks.Selected!.ChosenCandidate!.Candidate.Address == E);
    }

    [Fact]
    public async Task X_dismisses_the_chosen_candidate_and_a_suggestion_all_decided_moves_to_Avgjort()
    {
        var rig = await new Rig().Follow();
        rig.Checks.Toggle();
        rig.Checks.ChooseCandidate(rig.Checks.Selected!.Candidates[1]);

        await rig.Checks.DismissAsync();
        await rig.Checks.DismissAsync();

        Assert.Equal(
            [Candidate(E, 2, 0.5).Key, Candidate(U, 1, 0.8).Key],
            rig.Source.Decisions.Select(decision => decision.CandidateKey));
        Assert.All(rig.Source.Decisions, decision => Assert.Equal(CheckVerdict.Dismissed, decision.Verdict));
        Assert.Empty(rig.Checks.ToDecide);
        Assert.Single(rig.Checks.Decided);
        Assert.Equal("Förslag · 0", rig.Checks.ButtonText);
        Assert.DoesNotContain(rig.Checks.AllAreas, area => area.CheckKey == "mixer-outputs v1/deposit:0");
    }

    [Fact]
    public async Task A_decided_candidate_cannot_be_chosen_again()
    {
        var rig = await new Rig().Follow();
        rig.Checks.Toggle();
        await rig.Checks.ApproveAsync();
        var row = rig.Checks.Selected!;

        rig.Checks.ChooseCandidate(row.Candidates[0]);

        Assert.Same(row.Candidates[1], row.ChosenCandidate);
    }

    [Fact]
    public async Task Enter_on_a_check_shows_its_actors_and_decides_nothing()
    {
        var rig = await new Rig().Follow();
        rig.Checks.Toggle();
        rig.Checks.Next();
        IReadOnlyList<string>? shown = null;
        rig.Checks.ShowWanted += (_, nodes) => shown = nodes;

        await rig.Checks.ApproveAsync();
        await rig.Checks.DismissAsync();

        Assert.Equal([KKey, EKey], shown);
        Assert.Empty(rig.Source.Decisions);
        Assert.Equal("Visa i grafen", rig.Checks.Selected!.GoText);
    }

    [Fact]
    public async Task The_question_mark_asks_the_Guide_why_with_the_candidates_and_the_seal()
    {
        var rig = await new Rig().Follow();
        rig.Checks.Toggle();
        GuideRequest? asked = null;
        rig.Checks.GuideWanted += (_, request) => asked = request;

        rig.Checks.AskWhy();

        Assert.Equal(GuideTopic.MixerCandidates, asked!.Topic);
        var lines = asked.Context!.Lines;
        Assert.Equal(4, lines.Count);
        Assert.Contains("aldrig ett faktum", lines[0].Text, StringComparison.Ordinal);
        Assert.StartsWith("0,80 · ", lines[1].Text, StringComparison.Ordinal);
        Assert.Contains("Mixerutgångar v1 förseglade det 2026-11-07 13:02", lines[3].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_menu_hides_and_shows_a_sorts_areas_and_counts_them()
    {
        var rig = await new Rig().Follow();
        var before = rig.Checks.Areas.Count;

        rig.Checks.ToggleSort(AreaSort.Time);

        Assert.DoesNotContain(rig.Checks.Areas, area => area.Sort == AreaSort.Time);
        Assert.Equal(before - 1, rig.Checks.Areas.Count);
        var time = rig.Checks.Sorts.Single(sort => sort.Sort == AreaSort.Time);
        Assert.False(time.IsShown);
        Assert.Equal("1", time.CountText);
        Assert.Equal("inga", rig.Checks.Sorts.Single(sort => sort.Sort == AreaSort.Service).CountText);
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
    public async Task A_check_areas_chip_opens_the_list_at_its_row()
    {
        var rig = await new Rig().Follow();

        rig.Checks.ChooseArea($"coinjoin-area v1/{Tx[2].Value}");

        Assert.True(rig.Checks.IsOpen);
        Assert.Equal("CoinJoin, Wasabi", rig.Checks.Selected!.Title);
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
        var rig = await new Rig().Follow();
        rig.Checks.Toggle();
        rig.Checks.ChooseCandidate(rig.Checks.Selected!.Candidates[1]);
        await rig.Checks.DismissAsync();

        var line = Assert.Single(rig.Checks.CandidateLines);

        Assert.Equal((VKey, UKey, "0,80"), (line.From, line.To, line.ScoreText));
    }
}
