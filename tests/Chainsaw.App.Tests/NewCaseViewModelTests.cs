using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

public sealed class NewCaseViewModelTests
{
    private const string Password = "ett långt lösenord";

    [Fact]
    public void Each_line_is_recognised_under_the_field_and_TRON_is_toned()
    {
        var panel = Open();

        panel.StartPointsText = $"{Samples.Address}\n\n  {Samples.Txid}\r\n{Samples.Tron}\nhej";

        Assert.Equal(["BTC", "BTC", "TRON", "?"], panel.StartPoints.Select(line => line.ChainLabel));
        Assert.Equal("Bitcoin-adress", panel.StartPoints[0].KindText);
        Assert.Equal("Bitcoin-tx", panel.StartPoints[1].KindText);
        Assert.Equal("TRON-adress · inte byggt än", panel.StartPoints[2].KindText);
        Assert.False(panel.StartPoints[2].IsSupported);
        Assert.True(panel.StartPoints[3].IsUnknown);
        Assert.Equal("4f2a4f2a…f2a4f2a", panel.StartPoints[1].Short);
    }

    [Fact]
    public async Task A_case_is_created_and_the_panel_closes_empty()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var panel = rig.NewCase;
        panel.Show();
        panel.CaseNumber = " 2026-0117 ";
        panel.CaseTitle = "Investeringsbedrägeri via chatt";
        panel.StartPointsText = Samples.Address;

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
        panel.StartPointsText = Samples.Address + "\nhej";

        await panel.CreateAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.True(panel.IsOpen);
        Assert.Equal("Startpunkt 2 känns inte igen som en adress eller en tx.", panel.ErrorText);
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
    public void A_start_point_given_to_a_shown_panel_is_added_as_a_line()
    {
        var panel = Open();
        panel.StartPointsText = Samples.Address;

        panel.Show(Samples.Txid);

        Assert.Equal(2, panel.StartPoints.Count);
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
    public async Task Too_many_start_points_are_refused_rather_than_dropped()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var panel = rig.NewCase;
        panel.Show();
        panel.CaseNumber = "A1";
        panel.StartPointsText = string.Join('\n', Enumerable.Repeat(Samples.Address, NewCaseViewModel.MaximumLines + 1));

        await panel.CreateAsync(new TestEntry(string.Empty), new TestEntry(string.Empty));

        Assert.Empty(rig.Cases.Created);
        Assert.True(panel.HasError);
    }

    private static NewCaseViewModel Open()
    {
        var panel = new GateRig(VaultState.Unlocked).NewCase;
        panel.Show();
        return panel;
    }
}
