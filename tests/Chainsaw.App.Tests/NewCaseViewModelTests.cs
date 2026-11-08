using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

public sealed class NewCaseViewModelTests
{
    private const string Password = "ett långt lösenord";

    [Fact]
    public void Pasting_several_lines_adds_a_row_each_and_says_what_was_not_recognised()
    {
        var panel = Open();

        panel.Paste($"{Samples.Address}\n\n  {Samples.Txid}\r\n{Samples.Tron}\nhej");

        Assert.Equal(["₿", "₿", string.Empty], panel.Rows.Select(row => row.Glyph));
        Assert.Equal(["Adress", "Tx", "Adress"], panel.Rows.Select(row => row.KindText));
        Assert.True(panel.Rows[2].IsTron);
        Assert.Equal(PreviewState.NotBuilt, panel.Rows[2].Preview.State);
        Assert.Equal("TRON hämtas inte, inte byggt än", panel.Rows[2].LineText);
        Assert.Equal("4f2a4f2a…f2a4f2a", panel.Rows[1].Short);
        Assert.Equal("3", panel.RowCountText);
        Assert.Contains("hämtades inte", panel.ErrorText, StringComparison.Ordinal);
        Assert.Empty(panel.AddText);
    }

    [Fact]
    public void Running_text_with_a_hash_in_it_is_never_looked_up_but_a_list_with_commas_is()
    {
        var panel = Open();

        panel.Paste($"Filen har SHA-256 {Samples.Txid} enligt anteckningen\n{Samples.Address}, {Samples.Tron}");

        Assert.Equal(["Adress", "Adress"], panel.Rows.Select(row => row.KindText));
        Assert.DoesNotContain(panel.Rows, row => row.StartPoint.Value == Samples.Txid);
        Assert.StartsWith("En rad", panel.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void Pasting_nothing_recognised_goes_into_the_add_row()
    {
        var panel = Open();

        panel.Paste("hej då");

        Assert.Empty(panel.Rows);
        Assert.Equal("hej då", panel.AddText);
    }

    [Fact]
    public void Pasting_a_long_text_with_nothing_recognised_fills_the_add_row_only_as_far_as_its_field_goes()
    {
        var panel = Open();

        panel.Paste(string.Join('\n', Enumerable.Repeat("hej då", 100_000)));

        Assert.Empty(panel.Rows);
        Assert.Equal(NewCaseViewModel.MaximumAddLength, panel.AddText.Length);
    }

    [Fact]
    public void Enter_adds_what_is_written_also_when_it_is_not_recognised_and_the_same_value_once()
    {
        var panel = Open();

        panel.AddText = "hej";
        panel.CommitAddText();
        panel.AddText = Samples.Address;
        panel.CommitAddText();
        panel.Paste(Samples.Address);

        Assert.Equal(2, panel.Rows.Count);
        Assert.True(panel.Rows[0].IsNotRecognised);
        Assert.Equal(StartPointRowViewModel.NotRecognisedText, panel.Rows[0].LineText);
        Assert.Empty(panel.AddText);

        panel.Remove(panel.Rows[0]);

        Assert.Single(panel.Rows);
        Assert.True(panel.HasRows);
    }

    [Fact]
    public async Task A_recognised_value_is_added_when_typing_pauses()
    {
        var panel = Open();
        panel.TypingPause = TimeSpan.FromMilliseconds(20);

        panel.AddText = Samples.Address[..^1];
        panel.AddText = Samples.Address;
        await WaitUntil(() => panel.Rows.Count == 1);

        Assert.Single(panel.Rows);
        Assert.Empty(panel.AddText);
    }

    [Fact]
    public void The_case_number_shows_its_file_and_a_check_once_valid()
    {
        var panel = Open();

        Assert.False(panel.CanCreate);
        panel.CaseNumber = "2026-0117";

        Assert.True(panel.IsNumberValid);
        Assert.True(panel.CanCreate);
        Assert.Equal("2026-0117.cut", panel.FileName);
    }

    [Fact]
    public void Without_a_prefetch_a_row_waits_and_the_offline_note_shows_once_without_an_exit()
    {
        var panel = Open();
        panel.FollowExit(isLive: false, isWindows: true, "Windows");

        panel.Paste(Samples.Address);

        Assert.True(panel.Rows[0].IsWaiting);
        Assert.True(panel.IsOfflineNoteShown);
        Assert.Equal(NewCaseViewModel.OfflineText, panel.ExitChipText);

        panel.FollowExit(isLive: true, isWindows: true, "Windows");

        Assert.False(panel.IsOfflineNoteShown);
        Assert.Equal("via Windows", panel.ExitChipText);
    }

    [Fact]
    public async Task A_case_is_created_and_the_panel_closes_empty()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var panel = rig.NewCase;
        panel.Show();
        panel.CaseNumber = " 2026-0117 ";
        panel.CaseTitle = "Investeringsbedrägeri via chatt";
        panel.Paste(Samples.Address);

        await panel.CreateAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.False(panel.IsOpen);
        Assert.Empty(panel.CaseNumber);
        Assert.Equal("2026-0117 · Investeringsbedrägeri via chatt", rig.Shell.CaseTitle);
        Assert.Single(rig.Tracing.Added);
        Assert.Contains("skapades", rig.Shell.CommandAnswer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_line_creates_nothing_and_says_which()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var panel = rig.NewCase;
        panel.Show();
        panel.CaseNumber = "A1";
        panel.Paste(Samples.Address);
        panel.AddText = "hej";
        panel.CommitAddText();

        await panel.CreateAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.True(panel.IsOpen);
        Assert.Equal("hej känns inte igen som en adress eller en tx. Ta bort raden.", panel.ErrorText);
        Assert.Empty(rig.Cases.Created);
    }

    [Fact]
    public async Task A_case_number_is_needed()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.NewCase.Show();

        await rig.NewCase.CreateAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.True(rig.NewCase.HasError);
        Assert.Empty(rig.Cases.Created);
    }

    [Fact]
    public async Task A_shared_case_takes_both_password_fields_and_clears_them()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var panel = rig.NewCase;
        panel.Show();
        panel.CaseNumber = "A1";
        panel.IsShared = true;
        var password = new TestEntry(Password);
        var repeat = new TestEntry(Password);

        await panel.CreateAsync(password, repeat);

        Assert.Equal(Password, rig.Cases.Cases[0].SharingPassword);
        Assert.True(password.AllCleared && repeat.AllCleared);
        Assert.Empty(password.Text);
        Assert.Empty(repeat.Text);
    }

    [Fact]
    public async Task Two_different_passwords_create_nothing_and_are_cleared()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var panel = rig.NewCase;
        panel.Show();
        panel.CaseNumber = "A1";
        panel.IsShared = true;
        var password = new TestEntry(Password);
        var repeat = new TestEntry(Password + "!");

        await panel.CreateAsync(password, repeat);

        Assert.Empty(rig.Cases.Created);
        Assert.True(password.AllCleared && repeat.AllCleared);
        Assert.Contains("inte lika", panel.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_short_password_creates_nothing()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var panel = rig.NewCase;
        panel.Show();
        panel.CaseNumber = "A1";
        panel.IsShared = true;

        await panel.CreateAsync(new TestEntry("kort"), new TestEntry("kort"));

        Assert.Empty(rig.Cases.Created);
        Assert.Equal(CaseTexts.For(CaseError.PasswordTooShort), panel.ErrorText);
    }

    [Fact]
    public async Task Without_sharing_the_password_fields_are_not_read()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var panel = rig.NewCase;
        panel.Show();
        panel.CaseNumber = "A1";
        var password = new TestEntry(Password);

        await panel.CreateAsync(password, new TestEntry(Password));

        Assert.Empty(password.Taken);
        Assert.False(rig.Cases.Cases[0].HasSharingPassword);
    }

    [Fact]
    public async Task A_failed_creation_keeps_what_was_written()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.Cases.Failure = new CaseException(CaseError.AlreadyExists);
        var panel = rig.NewCase;
        panel.Show();
        panel.CaseNumber = "A1";
        panel.CaseTitle = "Titel";

        await panel.CreateAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.True(panel.IsOpen);
        Assert.Equal("Titel", panel.CaseTitle);
        Assert.Equal(CaseTexts.For(CaseError.AlreadyExists), panel.ErrorText);
    }

    [Fact]
    public void Cancel_closes_and_empties_the_panel()
    {
        var panel = Open();
        panel.CaseNumber = "A1";

        panel.Cancel();
        panel.Show();

        Assert.Empty(panel.CaseNumber);
    }

    [Fact]
    public void A_start_point_given_to_a_shown_panel_is_added_as_a_row()
    {
        var panel = Open();
        panel.Paste(Samples.Address);

        panel.Show(Samples.Txid);

        Assert.Equal(2, panel.Rows.Count);
    }

    [Fact]
    public void Locking_dismisses_the_panel_with_what_was_written()
    {
        var rig = new GateRig(VaultState.Unlocked);
        rig.NewCase.Show(caseNumber: "A1");

        rig.Gate.Lock();

        Assert.False(rig.NewCase.IsOpen);
        Assert.Empty(rig.NewCase.CaseNumber);
    }

    [Fact]
    public void Too_many_start_points_are_refused_rather_than_dropped()
    {
        var panel = Open();
        var addresses = Enumerable.Range(0, NewCaseViewModel.MaximumLines + 1)
            .Select(index => BitcoinAddress.FromWitness(
                0, Enumerable.Range(index, 20).Select(b => (byte)b).ToArray(), BitcoinNetwork.Mainnet).Value);

        panel.Paste(string.Join('\n', addresses));

        Assert.Equal(NewCaseViewModel.MaximumLines, panel.Rows.Count);
        Assert.Contains("Högst", panel.ErrorText, StringComparison.Ordinal);
    }

    private static NewCaseViewModel Open()
    {
        var panel = new GateRig(VaultState.Unlocked).NewCase;
        panel.Show();
        return panel;
    }

    private static async Task WaitUntil(Func<bool> condition)
    {
        for (var tries = 0; tries < 200 && !condition(); tries++)
        {
            await Task.Delay(10);
        }
    }
}
