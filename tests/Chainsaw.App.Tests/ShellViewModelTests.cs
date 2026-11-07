using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;

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

    [Fact]
    public void Showing_a_view_moves_the_choice_in_the_rail()
    {
        var shell = Shell();

        shell.ShowCommand.Execute(AppView.Labels);

        Assert.Equal("Etiketter", shell.CurrentEntry.Name);
        Assert.Equal([AppView.Labels], shell.Items.Where(i => i.IsSelected).Select(i => i.Entry.View));
    }

    [Fact]
    public void The_rail_holds_every_view_in_its_groups()
    {
        var shell = Shell();

        Assert.Equal(["Nytt"], shell.Top.Select(i => i.Entry.Name));
        Assert.Equal(["Terminal", "Guide", "Inställningar"], shell.Bottom.Select(i => i.Entry.Name));
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
    public async Task Escape_clears_the_text_and_the_answer()
    {
        var shell = Shell();
        shell.CommandText = "x";
        await shell.SubmitAsync();

        shell.ClearCommandCommand.Execute(null);

        Assert.Empty(shell.CommandText);
        Assert.Empty(shell.CommandAnswer);
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
        Assert.False(shell.IsExitConnected);
    }

    [Fact]
    public void The_exit_is_shown_by_its_place_when_it_is_connected_and_offline_when_it_falls()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var exit = new ExitInfo(Guid.NewGuid(), "wg-sthlm", "Stockholm", ExitKind.WireGuard);
        rig.Exits.Known.Add(exit);

        rig.Exit.Set(new ExitStatus(ExitState.Connected, exit.Id, null));
        Assert.True(SpinWait.SpinUntil(() => rig.Shell.ExitText == "Stockholm", TimeSpan.FromSeconds(5)));
        Assert.True(rig.Shell.IsExitConnected);
        Assert.Equal("Uppslag går genom utgången Stockholm", rig.Shell.SourceText);

        rig.Exit.Set(new ExitStatus(ExitState.Failed, exit.Id, ExitFailure.ConnectionLost));
        Assert.False(rig.Shell.IsExitConnected);
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
