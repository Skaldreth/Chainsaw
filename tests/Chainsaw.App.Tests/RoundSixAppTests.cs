using Chainsaw.App.Links;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// Answers that come after a lock, a link that waits while the bar has text, and links held until the host is built.
/// </summary>
public sealed class RoundSixAppTests
{
    [Fact]
    public async Task A_creation_that_ends_after_a_lock_does_not_touch_a_panel_shown_again()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.NewCase.Show(caseNumber: "A1");
        rig.Cases.DuringCreate = () =>
        {
            rig.Gate.Lock();
            rig.Vault.Unlock("correct horse battery");
            rig.NewCase.Show(caseNumber: "B2");
        };

        await rig.NewCase.CreateAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.True(rig.NewCase.IsOpen);
        Assert.Equal("B2", rig.NewCase.CaseNumber);
        Assert.Empty(rig.NewCase.ErrorText);
    }

    [Fact]
    public async Task The_workflow_says_while_a_creation_runs_so_that_the_exit_can_wait_for_it()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var runningDuring = false;
        rig.Cases.DuringCreate = () => runningDuring = !rig.Workflow.Running.IsCompleted;

        await rig.Workflow.CreateAsync(new NewCaseRequest("A1", string.Empty, []), null);

        Assert.True(runningDuring);
        Assert.True(rig.Workflow.Running.IsCompleted);
    }

    [Fact]
    public async Task A_command_that_ends_after_a_lock_gives_no_answer_after_the_unlock()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.AddFile("A1", minutesAgo: 1);
        rig.Cases.DuringOpen = () => rig.Gate.Lock();
        rig.Shell.CommandText = "/open A1";

        await rig.Shell.SubmitAsync();

        Assert.Empty(rig.Shell.CommandAnswer);
        Assert.Empty(rig.Shell.CommandText);
    }

    [Fact]
    public void A_link_while_the_bar_has_text_waits_and_comes_in_when_the_bar_is_empty()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Session.Open(new TestCase("A1"));
        rig.Shell.OpenCommandBar();
        rig.Shell.CommandText = "/verify";

        rig.Links.Receive("chainsaw:bitcoin/" + Samples.Address);

        Assert.Equal("/verify", rig.Shell.CommandText);
        Assert.True(rig.Shell.HasWaitingLink);
        rig.Shell.CommandText = string.Empty;
        Assert.Equal(Samples.Address, rig.Shell.CommandText);
        Assert.False(rig.Shell.HasWaitingLink);
    }

    [Fact]
    public void A_waiting_link_goes_with_the_lock()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Session.Open(new TestCase("A1"));
        rig.Shell.OpenCommandBar();
        rig.Shell.CommandText = "/verify";
        rig.Links.Receive("chainsaw:bitcoin/" + Samples.Address);

        rig.Gate.Lock();

        Assert.False(rig.Shell.HasWaitingLink);
        Assert.Empty(rig.Shell.CommandText);
    }

    [Fact]
    public void Messages_before_the_receiver_are_held_and_handed_over_in_order()
    {
        var hold = new MessageHold(capacity: 2);
        hold.Arrive("första");
        hold.Arrive("andra");
        hold.Arrive("tredje");
        List<string> received = [];

        hold.Attach(received.Add);
        hold.Arrive("fjärde");

        Assert.Equal(["första", "andra", "fjärde"], received);
        Assert.Equal(0, hold.Count);
    }
}
