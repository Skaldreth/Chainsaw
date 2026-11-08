using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel.Commands;

namespace Chainsaw.App.Tests;

public sealed class ShellViewModelTests
{
    [Fact]
    public void The_shell_opens_on_the_graph_with_only_its_icon_chosen()
    {
        var shell = Shell();

        Assert.Equal(AppView.Graph, shell.Current);
        Assert.Equal([AppView.Graph], shell.Items.Where(i => i.IsSelected).Select(i => i.Entry.View));
    }

    /// <summary>F1 shows the view Guide, which is built; the placeholder is only for the views that are not.</summary>
    [Fact]
    public void The_guide_is_a_built_view()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var shell = rig.Shell;
        rig.Session.Open(new TestCase("2026-0117"));
        Assert.False(shell.IsPlaceholderShown);

        shell.ShowCommand.Execute(AppView.Guide);
        Assert.True(shell.IsGuideShown);
        Assert.False(shell.IsPlaceholderShown);

        shell.ShowCommand.Execute(AppView.Labels);
        Assert.True(shell.IsPlaceholderShown);
    }

    /// <summary>Without a case, the views that need one fold away and their shortcuts do nothing.</summary>
    [Fact]
    public async Task Ctrl_W_closes_the_case_and_goes_to_the_start()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var shell = rig.Shell;
        rig.Session.Open(new TestCase("2026-0117"));
        shell.ShowCommand.Execute(AppView.Monitoring);

        await shell.CloseCaseAsync();

        Assert.Null(rig.Session.Current);
        Assert.Equal(ViewTable.Start, shell.Current);
    }

    [Fact]
    public void Without_a_case_the_views_that_need_one_fold_away()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var shell = rig.Shell;

        Assert.DoesNotContain(shell.Work, i => i.IsShown);
        shell.ShowCommand.Execute(AppView.Monitoring);
        Assert.Equal(AppView.Graph, shell.Current);

        rig.Session.Open(new TestCase("2026-0117"));
        Assert.All(shell.Work, item => Assert.True(item.IsShown));
        shell.ShowCommand.Execute(AppView.Monitoring);
        rig.Session.Forget();

        Assert.Equal(AppView.Graph, shell.Current);
    }

    [Fact]
    public void Showing_a_view_moves_the_choice_in_the_rail()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var shell = rig.Shell;
        rig.Session.Open(new TestCase("2026-0117"));

        shell.ShowCommand.Execute(AppView.Labels);

        Assert.Equal("Etiketter", shell.CurrentEntry.Name);
        Assert.Equal([AppView.Labels], shell.Items.Where(i => i.IsSelected).Select(i => i.Entry.View));
    }

    [Fact]
    public void The_rail_holds_every_view_in_its_groups()
    {
        var shell = Shell();

        Assert.Equal(["Nytt"], shell.Top.Select(i => i.Entry.Name));
        Assert.Equal(["Guide", "Inställningar"], shell.Bottom.Select(i => i.Entry.Name));
        Assert.Equal(ViewTable.All.Count, shell.Items.Count());
    }

    [Fact]
    public async Task An_empty_command_bar_gives_no_answer()
    {
        var shell = Shell();
        shell.CommandText = "  ";

        await shell.SubmitAsync();

        Assert.Empty(shell.CommandAnswer);
    }

    [Fact]
    public async Task Writing_again_clears_the_old_answer()
    {
        var shell = Shell();
        shell.CommandText = "/hjälp";
        await shell.SubmitAsync();

        shell.CommandText = "/hjälp2";

        Assert.Empty(shell.CommandAnswer);
    }

    [Fact]
    public async Task Clearing_empties_the_text_and_the_answer()
    {
        var shell = Shell();
        shell.CommandText = "x";
        await shell.SubmitAsync();

        shell.ClearCommandCommand.Execute(null);

        Assert.Empty(shell.CommandText);
        Assert.Empty(shell.CommandAnswer);
    }

    [Fact]
    public void The_command_bar_is_hidden_until_it_is_brought_up()
    {
        var shell = Shell();
        var wanted = 0;
        shell.CommandBarWanted += (_, _) => wanted++;
        Assert.False(shell.IsCommandBarOpen);

        shell.OpenCommandBarCommand.Execute(null);

        Assert.True(shell.IsCommandBarOpen);
        Assert.Equal(1, wanted);
    }

    [Fact]
    public async Task Escape_hides_the_bar_and_its_answer_but_keeps_the_text()
    {
        var shell = Shell();
        shell.OpenCommandBar();
        shell.CommandText = "/open X9";
        await shell.SubmitAsync();
        Assert.True(shell.IsCommandBarOpen);
        Assert.NotEmpty(shell.CommandAnswer);

        shell.CloseCommandBarCommand.Execute(null);

        Assert.False(shell.IsCommandBarOpen);
        Assert.Empty(shell.CommandAnswer);
        Assert.Equal("/open X9", shell.CommandText);
    }

    [Fact]
    public async Task A_command_that_did_what_it_was_asked_hides_the_bar_and_the_toaster_says_the_answer()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("A1", 1);
        var shell = rig.Shell;
        shell.OpenCommandBar();
        shell.CommandText = "/open A1";

        await shell.SubmitAsync();

        Assert.False(shell.IsCommandBarOpen);
        var toast = Assert.Single(rig.Toaster.Toasts);
        Assert.Equal(shell.CommandAnswer, toast.Title);
        Assert.Equal(ToastKind.Information, toast.Kind);
    }

    [Fact]
    public async Task A_failed_command_keeps_the_bar_up_and_says_nothing_through_the_toaster()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var shell = rig.Shell;
        shell.OpenCommandBar();
        shell.CommandText = "/open X9";

        await shell.SubmitAsync();

        Assert.True(shell.IsCommandBarOpen);
        Assert.True(shell.IsShowingError);
        Assert.Empty(rig.Toaster.Toasts);
    }

    [Fact]
    public void An_answer_while_the_bar_is_hidden_is_said_by_the_toaster_and_not_shown_when_it_comes_up()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var shell = rig.Shell;

        shell.Say(CommandReply.Failed("Något gick fel."));

        var toast = Assert.Single(rig.Toaster.Toasts);
        Assert.Equal(ToastKind.Error, toast.Kind);
        shell.OpenCommandBar();
        Assert.Empty(shell.ShownAbove);
    }

    [Fact]
    public void Showing_a_view_hides_the_command_bar()
    {
        var shell = Shell();
        shell.OpenCommandBar();

        shell.ShowCommand.Execute(AppView.Guide);

        Assert.False(shell.IsCommandBarOpen);
    }

    [Fact]
    public void The_command_bar_stays_hidden_while_nytt_arende_lies_over_the_page()
    {
        var shell = Shell();
        shell.NewCase.Show();

        shell.OpenCommandBar();

        Assert.False(shell.IsCommandBarOpen);
    }

    [Fact]
    public void A_waiting_count_shows_a_dot_only_when_something_waits()
    {
        var item = Shell().Work.First(i => i.Entry.View == AppView.Monitoring);

        Assert.False(item.HasWaiting);
        item.Waiting = 1;
        Assert.True(item.HasWaiting);
    }

    [Fact]
    public void The_title_bar_and_status_line_never_show_made_up_data()
    {
        var shell = Shell();

        Assert.Equal("Inget ärende öppet", shell.CaseTitle);
        Assert.Equal(ShellViewModel.NoExitText, shell.ExitText);
        Assert.Equal(ShellViewModel.OfflineText, shell.SourceText);
        Assert.False(shell.IsExitLive);
    }

    [Fact]
    public void The_exit_is_shown_by_its_place_when_it_is_connected_and_offline_when_it_falls()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var exit = new ExitInfo(Guid.NewGuid(), "wg-sthlm", "Stockholm", ExitKind.WireGuard);
        rig.Exits.Known.Add(exit);

        rig.Exit.Set(new ExitStatus(ExitState.Connected, exit.Id, null));
        Assert.True(SpinWait.SpinUntil(() => rig.Shell.ExitText == "Stockholm", TimeSpan.FromSeconds(5)));
        Assert.True(rig.Shell.IsExitLive);
        Assert.Equal("Uppslag går genom utgången Stockholm", rig.Shell.SourceText);

        rig.Exit.Set(new ExitStatus(ExitState.Failed, exit.Id, ExitFailure.ConnectionLost));
        Assert.False(rig.Shell.IsExitLive);
        Assert.Equal(ShellViewModel.OfflineText, rig.Shell.SourceText);
    }

    [Fact]
    public void The_title_bar_names_the_open_case_with_its_title()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var open = new TestCase("2026-0117");
        open.SetTitle("Investeringsbedrägeri via chatt");

        rig.Session.Open(open);

        Assert.Equal("2026-0117 · Investeringsbedrägeri via chatt", rig.Shell.CaseTitle);
    }

    [Fact]
    public void Nytt_in_the_rail_opens_the_panel_and_keeps_the_view()
    {
        var shell = Shell();

        shell.ShowCommand.Execute(AppView.New);

        Assert.True(shell.NewCase.IsOpen);
        Assert.Equal(AppView.Graph, shell.Current);
    }

    private static ShellViewModel Shell() => new GateRig(VaultState.Unlocked).Shell;
}
