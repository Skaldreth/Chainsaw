using Chainsaw.App.Navigation;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Tests;

public sealed class ShellViewModelTests
{
    [Fact]
    public void The_shell_opens_on_the_graph_with_only_its_icon_chosen()
    {
        var shell = new ShellViewModel();

        Assert.Equal(AppView.Graph, shell.Current);
        Assert.Equal([AppView.Graph], shell.Items.Where(i => i.IsSelected).Select(i => i.Entry.View));
    }

    [Fact]
    public void Showing_a_view_moves_the_choice_in_the_rail()
    {
        var shell = new ShellViewModel();

        shell.ShowCommand.Execute(AppView.Labels);

        Assert.Equal("Etiketter", shell.CurrentEntry.Name);
        Assert.Equal([AppView.Labels], shell.Items.Where(i => i.IsSelected).Select(i => i.Entry.View));
    }

    [Fact]
    public void The_rail_holds_every_view_in_its_groups()
    {
        var shell = new ShellViewModel();

        Assert.Equal(["Nytt"], shell.Top.Select(i => i.Entry.Name));
        Assert.Equal(["Terminal", "Guide", "Inställningar"], shell.Bottom.Select(i => i.Entry.Name));
        Assert.Equal(ViewTable.All.Count, shell.Items.Count());
    }

    [Fact]
    public void The_command_bar_answers_honestly_that_nothing_is_built()
    {
        var shell = new ShellViewModel { CommandText = "bc1q…7h2k" };

        shell.SubmitCommand.Execute(null);

        Assert.Equal(ShellViewModel.NoCommandsText, shell.CommandAnswer);
        Assert.Equal("bc1q…7h2k", shell.CommandText);
    }

    [Fact]
    public void An_empty_command_bar_gives_no_answer()
    {
        var shell = new ShellViewModel { CommandText = "  " };

        shell.SubmitCommand.Execute(null);

        Assert.Empty(shell.CommandAnswer);
    }

    [Fact]
    public void Writing_again_clears_the_old_answer()
    {
        var shell = new ShellViewModel { CommandText = "/hjälp" };
        shell.SubmitCommand.Execute(null);

        shell.CommandText = "/hjälp2";

        Assert.Empty(shell.CommandAnswer);
    }

    [Fact]
    public void Escape_clears_the_text_and_the_answer()
    {
        var shell = new ShellViewModel { CommandText = "x" };
        shell.SubmitCommand.Execute(null);

        shell.ClearCommandCommand.Execute(null);

        Assert.Empty(shell.CommandText);
        Assert.Empty(shell.CommandAnswer);
    }

    [Fact]
    public void A_waiting_count_shows_a_dot_only_when_something_waits()
    {
        var item = new ShellViewModel().Work.First(i => i.Entry.View == AppView.Monitoring);

        Assert.False(item.HasWaiting);
        item.Waiting = 1;
        Assert.True(item.HasWaiting);
    }

    [Fact]
    public void The_title_bar_and_status_line_never_show_made_up_data()
    {
        var shell = new ShellViewModel();

        Assert.Equal("Inget ärende öppet", shell.CaseTitle);
        Assert.Equal(ShellViewModel.NoSourceText, shell.SourceText);
    }
}
