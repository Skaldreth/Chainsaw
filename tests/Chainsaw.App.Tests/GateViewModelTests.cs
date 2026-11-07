using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

public sealed class GateViewModelTests
{
    [Theory]
    [InlineData(VaultState.NotCreated, GateMode.FirstRun)]
    [InlineData(VaultState.Locked, GateMode.Locked)]
    public void Chainsaw_starts_with_the_setup_without_a_vault_and_locked_with_one(VaultState state, GateMode mode)
    {
        var gate = new GateRig(state).Gate;

        Assert.Equal(mode, gate.Mode);
        Assert.False(gate.IsShellVisible);
        Assert.True(gate.IsGateVisible);
    }

    [Fact]
    public async Task Unlocking_shows_the_shell()
    {
        var rig = new GateRig(VaultState.Locked);

        await rig.LockScreen.UnlockAsync(new TestEntry("correct horse battery"));

        Assert.Equal(GateMode.Unlocked, rig.Gate.Mode);
        Assert.True(rig.Gate.IsShellVisible);
        Assert.False(rig.Gate.IsGateVisible);
    }

    [Fact]
    public async Task Locking_hides_everything_empties_the_command_bar_forgets_the_case_and_locks_the_vault()
    {
        var rig = await Unlocked();
        rig.Shell.CommandText = "bc1qexample";
        var openCase = new TestCase("5000-K1-26");
        rig.Session.Open(openCase);

        rig.Gate.LockCommand.Execute(null);

        Assert.Equal(GateMode.Locked, rig.Gate.Mode);
        Assert.False(rig.Gate.IsShellVisible);
        Assert.Equal(VaultState.Locked, rig.Vault.State);
        Assert.Equal(string.Empty, rig.Shell.CommandText);
        Assert.Null(rig.Session.Current);
        Assert.Equal(ShellViewModel.NoCaseText, rig.Shell.CaseTitle);
        Assert.False(rig.Gate.LockCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_lock_from_elsewhere_hides_the_shell_too()
    {
        var rig = await Unlocked();

        rig.Vault.Lock();

        Assert.Equal(GateMode.Locked, rig.Gate.Mode);
        Assert.False(rig.Gate.IsShellVisible);
    }

    [Fact]
    public async Task The_lock_screen_forgets_its_old_error_when_shown_again()
    {
        var rig = new GateRig(VaultState.Locked);
        await rig.LockScreen.UnlockAsync(new TestEntry("fel nyckel här"));
        await rig.LockScreen.UnlockAsync(new TestEntry("correct horse battery"));

        rig.Gate.Lock();

        Assert.False(rig.LockScreen.HasError);
    }

    [Fact]
    public async Task Inactivity_locks_after_the_chosen_time_and_activity_puts_it_off()
    {
        var rig = await Unlocked();

        rig.Clock.UtcNow += TimeSpan.FromMinutes(14);
        rig.Gate.CheckIdle();
        Assert.Equal(GateMode.Unlocked, rig.Gate.Mode);

        rig.Gate.Touch();
        rig.Clock.UtcNow += TimeSpan.FromMinutes(14);
        rig.Gate.CheckIdle();
        Assert.Equal(GateMode.Unlocked, rig.Gate.Mode);

        rig.Clock.UtcNow += TimeSpan.FromMinutes(1);
        rig.Gate.CheckIdle();
        Assert.Equal(GateMode.Locked, rig.Gate.Mode);
        Assert.Equal(VaultState.Locked, rig.Vault.State);
    }

    [Fact]
    public async Task The_time_without_activity_starts_anew_at_unlocking()
    {
        var rig = new GateRig(VaultState.Locked);
        rig.Clock.UtcNow += TimeSpan.FromHours(2);

        await rig.LockScreen.UnlockAsync(new TestEntry("correct horse battery"));
        rig.Gate.CheckIdle();

        Assert.Equal(GateMode.Unlocked, rig.Gate.Mode);
    }

    [Fact]
    public async Task Windows_locking_locks_chainsaw()
    {
        var rig = await Unlocked();

        rig.Gate.OnWindowsLocked();

        Assert.Equal(GateMode.Locked, rig.Gate.Mode);
        Assert.Equal(VaultState.Locked, rig.Vault.State);
    }

    [Fact]
    public void Windows_locking_during_an_unlock_still_locks_the_vault()
    {
        var rig = new GateRig(VaultState.Locked);

        rig.Gate.OnWindowsLocked();

        Assert.Equal(1, rig.Vault.LockCalls);
    }

    [Fact]
    public void A_vault_that_disappears_brings_back_the_setup()
    {
        var rig = new GateRig(VaultState.Locked);

        rig.Vault.Set(VaultState.NotCreated);

        Assert.Equal(GateMode.FirstRun, rig.Gate.Mode);
        Assert.False(rig.Gate.IsShellVisible);
    }

    [Fact]
    public void Before_the_vault_exists_there_is_nothing_to_lock()
    {
        var rig = new GateRig(VaultState.NotCreated);

        rig.Gate.Lock();
        rig.Gate.OnWindowsLocked();
        rig.Clock.UtcNow += TimeSpan.FromHours(1);
        rig.Gate.CheckIdle();

        Assert.Equal(GateMode.FirstRun, rig.Gate.Mode);
        Assert.Equal(0, rig.Vault.LockCalls);
    }

    [Fact]
    public async Task Locking_in_the_middle_of_the_setup_shows_the_lock_screen()
    {
        var rig = new GateRig(VaultState.NotCreated);
        rig.FirstRun.Name = "Anna";
        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));
        await rig.FirstRun.NextAsync(new TestEntry("correct horse battery"), new TestEntry("correct horse battery"));
        Assert.Equal(GateMode.FirstRun, rig.Gate.Mode);

        rig.Gate.Lock();

        Assert.Equal(GateMode.Locked, rig.Gate.Mode);
        Assert.Equal(VaultState.Locked, rig.Vault.State);
    }

    [Fact]
    public async Task A_lock_while_the_first_case_is_created_keeps_the_lock_screen()
    {
        var rig = new GateRig(VaultState.NotCreated);
        rig.FirstRun.Name = "Anna";
        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));
        await rig.FirstRun.NextAsync(new TestEntry("correct horse battery"), new TestEntry("correct horse battery"));
        rig.FirstRun.Later();
        rig.FirstRun.CaseNumber = "5000-K9-26";
        rig.Cases.DuringCreate = () => rig.Vault.Lock();

        await rig.FirstRun.NextAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.Equal(GateMode.Locked, rig.Gate.Mode);
        Assert.False(rig.Gate.IsShellVisible);
        Assert.Null(rig.Session.Current);
    }

    [Fact]
    public async Task A_stale_unlock_after_a_lock_does_not_show_the_shell()
    {
        var rig = new GateRig(VaultState.Locked);
        var posted = new List<Action>();
        var gate = new GateViewModel(
            rig.Vault, rig.LockScreen, rig.FirstRun, rig.Shell, rig.Session, rig.AutoLock, new QueueUi(posted));

        await rig.LockScreen.UnlockAsync(new TestEntry("correct horse battery"));
        rig.Vault.Lock();
        posted[0]();

        Assert.Equal(GateMode.Locked, gate.Mode);
        Assert.False(gate.IsShellVisible);
    }

    [Fact]
    public void The_shell_keys_wait_while_locked_but_the_lock_key_does_not_repeat()
    {
        var rig = new GateRig(VaultState.Locked);

        Assert.False(rig.Gate.IsShellVisible);
        Assert.False(rig.Gate.LockCommand.CanExecute(null));
    }

    private sealed class QueueUi(List<Action> posted) : IUiThread
    {
        public void Post(Action work) => posted.Add(work);
    }

    private static async Task<GateRig> Unlocked()
    {
        var rig = new GateRig(VaultState.Locked);
        await rig.LockScreen.UnlockAsync(new TestEntry("correct horse battery"));
        Assert.Equal(GateMode.Unlocked, rig.Gate.Mode);
        return rig;
    }
}
