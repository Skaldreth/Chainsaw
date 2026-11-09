using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// A master-key change that completes while or after Chainsaw locks (decided 2026-11-09): Chainsaw is locked and the
/// lock screen says who changed the key, until the next unlock or until it is dismissed.
/// </summary>
public sealed class KeyChangeNoticeTests
{
    private const string Current = "correct horse battery";

    private const string Next = "a much longer new key";

    private static Task ChangeAsync(GateRig rig)
    {
        rig.Settings.Open();
        rig.Settings.ToggleChangeKey();
        return rig.Settings.ChangeMasterKeyAsync(new TestEntry(Current), new TestEntry(Next), new TestEntry(Next));
    }

    [Fact]
    public void The_notice_names_the_one_who_changed_the_key_or_no_one()
    {
        Assert.Equal(
            "Huvudnyckeln ändrades av Alex Berg. Du blir nu utloggad.", LockViewModel.KeyChangedText(" Alex Berg "));
        Assert.Equal("Huvudnyckeln ändrades. Du blir nu utloggad.", LockViewModel.KeyChangedText(null));
        Assert.Equal(LockViewModel.KeyChangedUnnamedText, LockViewModel.KeyChangedText("  "));
    }

    [Fact]
    public async Task A_lock_during_the_change_shows_who_changed_it_on_the_lock_screen()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Profiles.Save(new OperatorProfile("Alex Berg", "Utredare", "Kryptogruppen"));
        rig.Vault.DuringChange = () => rig.Vault.Lock();

        await ChangeAsync(rig);

        Assert.Equal(Next, rig.Vault.ChangedTo);
        Assert.Equal(GateMode.Locked, rig.Gate.Mode);
        Assert.Equal("Huvudnyckeln ändrades av Alex Berg. Du blir nu utloggad.", rig.LockScreen.NoticeText);
        Assert.True(rig.LockScreen.HasNotice);
        Assert.False(rig.Settings.IsOpen);
    }

    [Fact]
    public async Task Without_a_profile_the_notice_names_no_one()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Vault.DuringChange = () => rig.Vault.Lock();

        await ChangeAsync(rig);

        Assert.Equal(LockViewModel.KeyChangedUnnamedText, rig.LockScreen.NoticeText);
    }

    [Fact]
    public async Task A_change_that_completes_when_the_automatic_lock_is_due_locks_and_says_so()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Profiles.Save(new OperatorProfile("Alex Berg", "Utredare", "Kryptogruppen"));
        rig.Vault.DuringChange = () => rig.Clock.UtcNow += TimeSpan.FromMinutes(16);

        await ChangeAsync(rig);

        Assert.Equal(GateMode.Locked, rig.Gate.Mode);
        Assert.Equal(VaultState.Locked, rig.Vault.State);
        Assert.Equal("Huvudnyckeln ändrades av Alex Berg. Du blir nu utloggad.", rig.LockScreen.NoticeText);
    }

    [Fact]
    public async Task A_change_without_a_lock_is_answered_in_the_settings_and_stays_unlocked()
    {
        var rig = new GateRig(VaultState.Unlocked);

        await ChangeAsync(rig);

        Assert.Equal(GateMode.Unlocked, rig.Gate.Mode);
        Assert.Equal(SettingsViewModel.KeyChangedText, rig.Settings.KeyText);
        Assert.False(rig.LockScreen.HasNotice);
    }

    [Fact]
    public async Task A_wrong_current_key_during_a_lock_says_nothing_on_the_lock_screen()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Vault.DuringChange = () => rig.Vault.Lock();
        rig.Settings.Open();
        rig.Settings.ToggleChangeKey();

        await rig.Settings.ChangeMasterKeyAsync(
            new TestEntry("the wrong current key"), new TestEntry(Next), new TestEntry(Next));

        Assert.Null(rig.Vault.ChangedTo);
        Assert.False(rig.LockScreen.HasNotice);
    }

    [Fact]
    public async Task The_notice_stays_through_another_lock_and_a_wrong_key_and_goes_with_the_unlock()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Vault.DuringChange = () => rig.Vault.Lock();
        await ChangeAsync(rig);

        rig.Gate.OnWindowsLocked();
        await rig.LockScreen.UnlockAsync(new TestEntry(Current));
        Assert.True(rig.LockScreen.HasNotice);

        Assert.True(await rig.LockScreen.UnlockAsync(new TestEntry(Next)));
        Assert.False(rig.LockScreen.HasNotice);
    }

    [Fact]
    public async Task The_notice_can_be_dismissed()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Vault.DuringChange = () => rig.Vault.Lock();
        await ChangeAsync(rig);

        rig.LockScreen.DismissNoticeCommand.Execute(null);

        Assert.False(rig.LockScreen.HasNotice);
        Assert.Equal(GateMode.Locked, rig.Gate.Mode);
    }
}
