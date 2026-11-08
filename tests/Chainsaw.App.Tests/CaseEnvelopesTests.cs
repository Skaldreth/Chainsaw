using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// Sketch ÄR1b in the window: a colleague's file opened with its case password, the case's properties with the tab
/// Kuvert, and the choice of branch after a crash.
/// </summary>
public sealed class CaseEnvelopesTests
{
    private static readonly TimeZoneInfo Zone =
        TimeZoneInfo.CreateCustomTimeZone("Chainsaw/Test", TimeSpan.FromHours(1), "Test", "Test");

    private readonly GateRig _rig = new(VaultState.Unlocked);

    private CaseDialogsViewModel Dialogs(TestCasePicker? files = null) => new(
        _rig.Workflow,
        new CasePasswordViewModel(_rig.Workflow),
        new CasePropertiesViewModel(_rig.Session, _rig.Clock, Zone),
        new CaseBranchViewModel(_rig.Workflow, _rig.Clock, Zone),
        files);

    // ===== A colleague's file =====

    [Fact]
    public async Task A_case_this_vault_cannot_open_asks_for_its_case_password()
    {
        var dialogs = Dialogs();
        _rig.Cases.AddFile("A1", 10);
        _rig.Cases.OpenError = CaseError.NoAccess;
        _rig.Cases.FileEnvelopes.AddRange(
            [new(CaseEnvelopeKind.OtherVault), new(CaseEnvelopeKind.SharingPassword), new(CaseEnvelopeKind.Recovery)]);
        var shown = 0;
        dialogs.Shown += (_, _) => shown++;

        var reply = await _rig.Workflow.OpenAsync("A1");

        Assert.True(reply.IsError);
        Assert.Equal(1, shown);
        var password = dialogs.Password;
        Assert.True(password.IsOpen);
        Assert.Equal("A1", password.CaseNumber);
        Assert.StartsWith("I mappen Ärenden", password.FileText, StringComparison.Ordinal);
        Assert.Equal(
            ["Ett annat valv", "Ärendelösenord", "Organisationen", "Du"],
            password.Envelopes.Select(chip => chip.Label));
        Assert.True(password.Envelopes[1].IsLit);
        Assert.True(password.Envelopes[3].IsPending);
    }

    [Fact]
    public async Task Turning_the_own_envelope_off_takes_away_the_dashed_card()
    {
        var dialogs = Dialogs();
        await dialogs.Password.ShowAsync(Offer());

        dialogs.Password.ToggleAdd();

        Assert.False(dialogs.Password.AddOwnEnvelope);
        Assert.DoesNotContain(dialogs.Password.Envelopes, chip => chip.IsPending);
    }

    [Fact]
    public async Task The_right_password_opens_the_file_adds_an_envelope_and_is_cleared()
    {
        var dialogs = Dialogs();
        await dialogs.Password.ShowAsync(Offer());
        var entry = new TestEntry(TestCaseStore.CasePassword);

        await dialogs.Password.OpenAsync(entry);

        Assert.True(dialogs.Password.IsDone);
        Assert.Equal(CasePasswordViewModel.DoneText, dialogs.Password.DoneMessage);
        Assert.True(entry.AllCleared);
        var opened = Assert.IsType<TestCase>(_rig.Session.Current);
        Assert.True(opened.HasOwnEnvelope);
        Assert.Contains(dialogs.Password.Envelopes, chip => chip.IsAdded);
        Assert.Equal((Offer().Path, TestCaseStore.CasePassword), Assert.Single(_rig.Cases.Imported));
    }

    [Fact]
    public async Task A_wrong_password_says_so_changes_nothing_and_keeps_the_open_case()
    {
        _rig.Cases.AddFile("A2", 10);
        await _rig.Workflow.OpenAsync("A2");
        var before = _rig.Session.Current;
        var dialogs = Dialogs();
        await dialogs.Password.ShowAsync(Offer());
        var entry = new TestEntry("ett fel lösenord här");

        await dialogs.Password.OpenAsync(entry);

        Assert.True(dialogs.Password.IsWrong);
        Assert.False(dialogs.Password.IsDone);
        Assert.True(dialogs.Password.IsOpen);
        Assert.True(entry.AllCleared);
        Assert.Same(before, _rig.Session.Current);
        Assert.Equal(0, ((TestCase)before!).Disposed);
    }

    [Fact]
    public async Task Without_the_own_envelope_the_case_opens_and_says_the_password_is_needed_again()
    {
        var dialogs = Dialogs();
        await dialogs.Password.ShowAsync(Offer());
        dialogs.Password.ToggleAdd();

        await dialogs.Password.OpenAsync(new TestEntry(TestCaseStore.CasePassword));

        Assert.True(dialogs.Password.IsDone);
        Assert.Equal(CasePasswordViewModel.OpenedWithoutEnvelopeText, dialogs.Password.DoneMessage);
        Assert.False(((TestCase)_rig.Session.Current!).HasOwnEnvelope);
    }

    [Fact]
    public async Task An_envelope_that_could_not_be_added_is_said_but_the_case_stays_open()
    {
        var dialogs = Dialogs();
        await dialogs.Password.ShowAsync(Offer());
        _rig.Cases.ImportAccessFailure = new IOException("disken är full");

        await dialogs.Password.OpenAsync(new TestEntry(TestCaseStore.CasePassword));

        Assert.True(dialogs.Password.IsDone);
        Assert.Equal(CasePasswordViewModel.OpenedWithoutEnvelopeText, dialogs.Password.DoneMessage);
        Assert.Contains("kuvert lades inte till", dialogs.Password.ErrorText, StringComparison.Ordinal);
        Assert.NotNull(_rig.Session.Current);
        Assert.DoesNotContain(dialogs.Password.Envelopes, chip => chip.IsAdded);
    }

    [Fact]
    public async Task An_empty_password_is_not_tried()
    {
        var dialogs = Dialogs();
        await dialogs.Password.ShowAsync(Offer());

        await dialogs.Password.OpenAsync(new TestEntry(string.Empty));

        Assert.True(dialogs.Password.IsWrong);
        Assert.Empty(_rig.Cases.Imported);
    }

    [Fact]
    public async Task A_file_outside_the_folder_is_offered_for_its_password_and_copied_in()
    {
        var file = Path.Combine(Path.GetTempPath(), "chainsaw-offer-" + Guid.NewGuid().ToString("N"), "B7.cut");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllBytesAsync(file, new byte[4096]);
        try
        {
            var dialogs = Dialogs(new TestCasePicker(file));

            var problem = await dialogs.PickAndOpenFileAsync();

            Assert.Equal(string.Empty, problem);
            Assert.True(dialogs.Password.IsOpen);
            Assert.Equal("B7", dialogs.Password.CaseNumber);
            Assert.Contains("kopieras till Ärenden", dialogs.Password.FileText, StringComparison.Ordinal);
            Assert.Contains("4 kB", dialogs.Password.FileText, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(file)!, recursive: true);
        }
    }

    [Fact]
    public async Task A_chosen_file_that_is_no_case_is_said()
    {
        var dialogs = Dialogs(new TestCasePicker(Path.Combine(Path.GetTempPath(), "anteckning.txt")));

        var problem = await dialogs.PickAndOpenFileAsync();

        Assert.Equal(CaseWorkflow.NotCaseFileText, problem);
        Assert.False(dialogs.IsAnyOpen);
    }

    [Fact]
    public async Task Cancelling_the_file_dialog_does_nothing()
    {
        var dialogs = Dialogs(new TestCasePicker(null));

        Assert.Equal(string.Empty, await dialogs.PickAndOpenFileAsync());
        Assert.False(dialogs.IsAnyOpen);
    }

    // ===== The properties =====

    [Fact]
    public async Task The_tab_Kuvert_shows_every_envelope_as_a_card()
    {
        var opened = await OpenCaseAsync();
        opened.OtherEnvelopes.AddRange(
        [
            new(CaseEnvelopeKind.OtherVault),
            new(CaseEnvelopeKind.Recovery, new string('a', 64), RecoveryEnvelopeKey.BuiltIn),
            new(CaseEnvelopeKind.Unknown),
        ]);
        var dialogs = Dialogs();

        await dialogs.ShowPropertiesAsync();

        var properties = dialogs.Properties;
        Assert.True(properties.IsOpen);
        Assert.True(properties.IsEnvelopesTab);
        Assert.Equal(
            [
                EnvelopeCardKind.Yours, EnvelopeCardKind.OtherVault, EnvelopeCardKind.NoSharing,
                EnvelopeCardKind.Recovery, EnvelopeCardKind.Unknown,
            ],
            properties.Cards.Select(card => card.Kind));
        var recovery = properties.Cards[3];
        Assert.True(recovery.IsOrganisation);
        Assert.StartsWith("Fingeravtryck AAAA AAAA", recovery.Text, StringComparison.Ordinal);
        Assert.Contains("inbyggd", recovery.Subtitle, StringComparison.Ordinal);
        Assert.True(properties.Cards[2].CanCreate);
    }

    [Fact]
    public void A_file_without_a_recovery_envelope_says_so_honestly()
    {
        var cards = CasePropertiesViewModel.CardsFor([new(CaseEnvelopeKind.Yours)]);

        var missing = Assert.Single(cards, card => card.Kind == EnvelopeCardKind.NoRecovery);
        Assert.True(missing.IsDashed);
    }

    [Fact]
    public void Revoked_and_other_recovery_keys_say_what_happens_to_them()
    {
        var cards = CasePropertiesViewModel.CardsFor(
        [
            new(CaseEnvelopeKind.Recovery, new string('b', 64), RecoveryEnvelopeKey.Revoked),
            new(CaseEnvelopeKind.Recovery, new string('c', 64), RecoveryEnvelopeKey.Other),
        ]);

        Assert.Contains(cards, card => card.Subtitle.StartsWith("Återkallad", StringComparison.Ordinal));
        Assert.Contains(cards, card => card.Subtitle.EndsWith("ligger kvar", StringComparison.Ordinal));
        Assert.Single(cards, card => card.Kind == EnvelopeCardKind.YoursMissing);
    }

    [Fact]
    public async Task A_case_password_is_created_when_both_fields_agree_and_the_fields_are_cleared()
    {
        var opened = await OpenCaseAsync();
        var dialogs = Dialogs();
        await dialogs.ShowPropertiesAsync();
        var card = dialogs.Properties.Cards.Single(item => item.Kind == EnvelopeCardKind.NoSharing);
        dialogs.Properties.EditPassword(card);
        Assert.Same(card, dialogs.Properties.EditingCard);
        var first = new TestEntry("ett nytt långt lösenord");
        var repeat = new TestEntry("ett nytt långt lösenord");

        await dialogs.Properties.SetPasswordAsync(first, repeat);

        Assert.Equal("ett nytt långt lösenord", opened.SharingPassword);
        Assert.True(first.AllCleared);
        Assert.True(repeat.AllCleared);
        Assert.Contains(dialogs.Properties.Cards, item => item.Kind == EnvelopeCardKind.SharingPassword);
        Assert.Null(dialogs.Properties.EditingCard);
    }

    [Fact]
    public async Task Passwords_that_differ_change_nothing()
    {
        var opened = await OpenCaseAsync();
        var dialogs = Dialogs();
        await dialogs.ShowPropertiesAsync();
        var first = new TestEntry("ett nytt långt lösenord");
        var repeat = new TestEntry("ett annat långt lösenord");

        await dialogs.Properties.SetPasswordAsync(first, repeat);

        Assert.Equal(CasePropertiesViewModel.MismatchText, dialogs.Properties.ErrorText);
        Assert.Null(opened.SharingPassword);
        Assert.True(first.AllCleared);
        Assert.True(repeat.AllCleared);
    }

    [Fact]
    public async Task A_short_password_is_refused()
    {
        var opened = await OpenCaseAsync();
        var dialogs = Dialogs();
        await dialogs.ShowPropertiesAsync();

        await dialogs.Properties.SetPasswordAsync(new TestEntry("kort"), new TestEntry("kort"));

        Assert.Equal(CaseTexts.For(CaseError.PasswordTooShort), dialogs.Properties.ErrorText);
        Assert.Null(opened.SharingPassword);
    }

    [Fact]
    public async Task The_case_password_cannot_be_removed_without_an_own_envelope()
    {
        var opened = await OpenCaseAsync();
        opened.SetSharingPassword("ett långt lösenord som finns");
        opened.HasOwnEnvelope = false;
        var dialogs = Dialogs();
        await dialogs.ShowPropertiesAsync();
        Assert.True(dialogs.Properties.Cards.Single(card => card.Kind == EnvelopeCardKind.YoursMissing).CanAddOwn);

        await dialogs.Properties.RemovePasswordAsync();

        Assert.Equal(CaseTexts.For(CaseError.WouldLoseAccess), dialogs.Properties.ErrorText);
        Assert.NotNull(opened.SharingPassword);

        await dialogs.Properties.AddOwnEnvelopeAsync();
        await dialogs.Properties.RemovePasswordAsync();

        Assert.Null(opened.SharingPassword);
        Assert.Equal(EnvelopeCardKind.Yours, dialogs.Properties.Cards[0].Kind);
        Assert.Contains(dialogs.Properties.Cards, card => card.Kind == EnvelopeCardKind.NoSharing);
    }

    [Fact]
    public async Task The_title_is_saved_and_the_title_bar_follows()
    {
        var opened = await OpenCaseAsync();
        var dialogs = Dialogs();
        await dialogs.ShowPropertiesAsync();

        dialogs.Properties.TitleText = "Investeringsbedrägeri via chatt";
        await dialogs.Properties.SaveTitleAsync();

        Assert.Equal("Investeringsbedrägeri via chatt", opened.Title);
        Assert.Equal("A1 · Investeringsbedrägeri via chatt", _rig.Session.DisplayName);
    }

    [Fact]
    public async Task The_properties_close_when_the_case_is_closed()
    {
        await OpenCaseAsync();
        var dialogs = Dialogs();
        await dialogs.ShowPropertiesAsync();

        await _rig.Workflow.CloseAsync();

        Assert.False(dialogs.Properties.IsOpen);
        Assert.Empty(dialogs.Properties.Cards);
    }

    [Fact]
    public async Task Without_an_open_case_the_properties_do_not_open()
    {
        var dialogs = Dialogs();

        await dialogs.ShowPropertiesAsync();

        Assert.False(dialogs.Properties.IsOpen);
    }

    // ===== After a crash =====

    [Fact]
    public async Task A_working_copy_in_conflict_offers_the_two_branches()
    {
        var dialogs = Dialogs();
        _rig.Cases.AddFile("A1", 10);
        _rig.Cases.OpenError = CaseError.Conflict;
        _rig.Cases.Conflict = Conflict();

        await _rig.Workflow.OpenAsync("A1");

        var branch = dialogs.Branch;
        Assert.True(branch.IsOpen);
        Assert.Equal("A1 gick åt två håll", branch.Heading);
        Assert.Equal("7 nov 13:00", branch.FileWhen);
        Assert.Equal("6 nov 18:51", branch.CopyWhen);
        Assert.True(branch.ChoosesCopy);
        Assert.Equal("Spara som A1-B", branch.GoText);
    }

    [Fact]
    public async Task Choosing_the_file_opens_it_with_the_working_copy_set_aside()
    {
        var dialogs = Dialogs();
        dialogs.Branch.Show(Conflict());
        dialogs.Branch.PickFile();
        Assert.Equal("Öppna filen", dialogs.Branch.GoText);

        await dialogs.Branch.GoAsync();

        Assert.Equal(("A1", (string?)null), Assert.Single(_rig.Cases.Branches));
        Assert.False(dialogs.Branch.IsOpen);
        Assert.Equal("A1", _rig.Session.Current!.Info.CaseNumber);
    }

    [Fact]
    public async Task Choosing_the_working_copy_saves_it_under_the_new_number()
    {
        var dialogs = Dialogs();
        dialogs.Branch.Show(Conflict());
        dialogs.Branch.BranchNumber = " A1-kopia ";

        await dialogs.Branch.GoAsync();

        Assert.Equal(("A1", (string?)"A1-kopia"), Assert.Single(_rig.Cases.Branches));
        Assert.Equal("A1-kopia", CaseSession.NumberOf(_rig.Session.Current!));
    }

    [Fact]
    public async Task An_invalid_new_number_cannot_be_chosen()
    {
        var dialogs = Dialogs();
        dialogs.Branch.Show(Conflict());
        dialogs.Branch.BranchNumber = "A1/B";

        Assert.False(dialogs.Branch.CanGo);
        await dialogs.Branch.GoAsync();

        Assert.Empty(_rig.Cases.Branches);
        Assert.True(dialogs.Branch.IsOpen);
    }

    [Fact]
    public async Task A_failed_choice_is_said_and_the_dialog_stays()
    {
        var dialogs = Dialogs();
        dialogs.Branch.Show(Conflict());
        _rig.Cases.Failure = new CaseException(CaseError.AlreadyExists);

        await dialogs.Branch.GoAsync();

        Assert.True(dialogs.Branch.IsOpen);
        Assert.Equal(CaseTexts.For(CaseError.AlreadyExists), dialogs.Branch.ErrorText);
    }

    [Fact]
    public void Only_one_dialog_is_shown_at_a_time()
    {
        var dialogs = Dialogs();
        dialogs.Branch.Show(Conflict());

        dialogs.CloseAll();

        Assert.False(dialogs.IsAnyOpen);
    }

    // ===== Words =====

    [Theory]
    [InlineData(96, "96 byte")]
    [InlineData(4096, "4 kB")]
    [InlineData(4_404_019, "4,2 MB")]
    public void Sizes_are_told_in_Swedish(long bytes, string expected) =>
        Assert.Equal(expected, EnvelopeWords.Size(bytes));

    [Fact]
    public void A_fingerprint_is_written_in_groups_of_four()
    {
        Assert.Equal("3F2A 91C0 7B", EnvelopeWords.GroupFingerprint("3f2a91c07b"));
    }

    [Fact]
    public void A_branch_case_is_named_after_its_file()
    {
        var branch = new TestCase("A1-B");

        Assert.Equal("A1-B", CaseSession.NumberOf(branch));
    }

    private static CaseFileOffer Offer() => new(Path.Combine("Ärenden", "A1.cut"), "A1", 4_404_019, InFolder: true);

    private static WorkingCopyConflict Conflict() => new(
        "A1",
        new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 11, 6, 17, 51, 0, TimeSpan.Zero),
        "A1-B");

    private async Task<TestCase> OpenCaseAsync()
    {
        _rig.Cases.AddFile("A1", 10);
        await _rig.Workflow.OpenAsync("A1");
        return (TestCase)_rig.Session.Current!;
    }
}

/// <summary>A file dialog that gives one path, or none as when the user cancelled.</summary>
internal sealed class TestCasePicker(string? path) : ICaseFilePicker
{
    public string? PickCaseFile() => path;
}
