using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

public sealed class LockViewModelTests
{
    [Fact]
    public async Task The_right_master_key_opens_the_vault_and_is_cleared_afterwards()
    {
        var rig = new GateRig(VaultState.Locked);
        var entry = new TestEntry("correct horse battery");

        var opened = await rig.LockScreen.UnlockAsync(entry);

        Assert.True(opened);
        Assert.Equal(VaultState.Unlocked, rig.Vault.State);
        Assert.Equal(string.Empty, entry.Text);
        Assert.True(entry.AllCleared);
        Assert.False(rig.LockScreen.HasError);
        Assert.False(rig.LockScreen.IsBusy);
    }

    [Fact]
    public async Task A_wrong_master_key_is_said_honestly_and_cleared()
    {
        var rig = new GateRig(VaultState.Locked);
        var entry = new TestEntry("wrong horse battery");

        var opened = await rig.LockScreen.UnlockAsync(entry);

        Assert.False(opened);
        Assert.Equal("Fel huvudnyckel. Valvet öppnades inte.", rig.LockScreen.ErrorText);
        Assert.Equal(VaultState.Locked, rig.Vault.State);
        Assert.True(entry.AllCleared);
        Assert.Equal("Lås upp", rig.LockScreen.UnlockLabel);
    }

    [Fact]
    public async Task An_empty_field_asks_for_the_key_without_asking_the_vault()
    {
        var rig = new GateRig(VaultState.Locked);

        await rig.LockScreen.UnlockAsync(new TestEntry(string.Empty));

        Assert.Equal(LockViewModel.EmptyKeyText, rig.LockScreen.ErrorText);
        Assert.Equal(0, rig.Vault.UnlockCalls);
    }

    [Theory]
    [InlineData(VaultError.Corrupt, "Valvet är skadat eller har ändrats utanför Chainsaw.")]
    [InlineData(VaultError.UnsupportedVersion, "Valvet är skrivet av en nyare version av Chainsaw.")]
    [InlineData(VaultError.Locked, "")]
    public async Task Every_reason_the_vault_did_not_open_has_its_own_text(VaultError error, string text)
    {
        var rig = new GateRig(VaultState.Locked);
        rig.Vault.Failure = new VaultException(error);
        var entry = new TestEntry("correct horse battery");

        await rig.LockScreen.UnlockAsync(entry);

        Assert.Equal(text, rig.LockScreen.ErrorText);
        Assert.True(entry.AllCleared);
    }

    [Fact]
    public async Task A_folder_that_cannot_be_read_is_said_without_crashing()
    {
        var rig = new GateRig(VaultState.Locked);
        rig.Vault.Failure = new IOException("disk");
        var entry = new TestEntry("correct horse battery");

        await rig.LockScreen.UnlockAsync(entry);

        Assert.StartsWith("Valvet gick inte att läsa", rig.LockScreen.ErrorText, StringComparison.Ordinal);
        Assert.True(entry.AllCleared);
    }

    [Fact]
    public async Task The_error_never_repeats_the_key()
    {
        var rig = new GateRig(VaultState.Locked);

        await rig.LockScreen.UnlockAsync(new TestEntry("hemlig nyckel 123"));

        Assert.DoesNotContain("hemlig", rig.LockScreen.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_exit_line_follows_the_exit()
    {
        var rig = new GateRig(VaultState.Locked);
        Assert.Equal(LockViewModel.ExitWaitsText, rig.LockScreen.ExitText);

        rig.Exit.Set(new ExitStatus(ExitState.Connected, Guid.NewGuid(), null));
        Assert.Equal(LockViewModel.ExitStaysText, rig.LockScreen.ExitText);

        rig.Exit.Set(ExitStatus.NotConnected);
        Assert.Equal(LockViewModel.ExitWaitsText, rig.LockScreen.ExitText);
    }

    [Fact]
    public void The_corner_shows_the_short_commit_and_the_lines_show_the_automatic_lock()
    {
        var rig = new GateRig(VaultState.Locked);

        Assert.Equal("Om Chainsaw · abcdef0", rig.LockScreen.AboutText);
        Assert.Contains("asciify-engine (MIT)", rig.LockScreen.AboutDetails, StringComparison.Ordinal);
        Assert.Equal("Låses automatiskt efter 15 minuter utan aktivitet.", rig.LockScreen.AutoLockText);
    }

    [Fact]
    public void The_view_model_holds_no_property_for_the_key()
    {
        var names = typeof(LockViewModel).GetProperties().Select(p => p.Name);

        Assert.DoesNotContain(names, n => n.Contains("Key", StringComparison.OrdinalIgnoreCase)
            || n.Contains("Password", StringComparison.OrdinalIgnoreCase));
    }
}
