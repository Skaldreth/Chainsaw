using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>The case picker under the case in the title bar (sketch ÄV1a), without WPF.</summary>
public sealed class CasePickerTests
{
    /// <summary>The cases, the session, the verifier and the picker, with three case files and none open.</summary>
    private sealed class Rig
    {
        public Rig()
        {
            Store.AddFile("2026-0098", minutesAgo: 60 * 24 * 10);
            Store.AddFile("2026-0114", minutesAgo: 30);
            Store.AddFile("2025-0301", minutesAgo: 60 * 24 * 3);
            Workflow = new CaseWorkflow(Store, new TestTracing(), new TestVault(VaultState.Unlocked), Session);
            Picker = new CasePickerViewModel(
                Session, Workflow, Verifier, new InlineUi(), new TestClock(), TimeZoneInfo.Utc);
        }

        public TestCaseStore Store { get; } = new();

        public CaseSession Session { get; } = new();

        public TestVerifier Verifier { get; } = new();

        public CaseWorkflow Workflow { get; }

        public CasePickerViewModel Picker { get; }

        /// <summary>Opens the picker and waits for the case files it lists.</summary>
        public async Task OpenAsync()
        {
            Picker.Open();
            await Picker.Listing;
        }

        /// <summary>The case numbers the list shows, in order.</summary>
        public IEnumerable<string> Numbers => Picker.Rows.Select(row => row.CaseNumber);
    }

    [Fact]
    public async Task The_cases_are_listed_newest_first_with_when_they_changed()
    {
        var rig = new Rig();

        await rig.OpenAsync();

        Assert.True(rig.Picker.IsOpen);
        Assert.Equal(["2026-0114", "2025-0301", "2026-0098"], rig.Numbers);
        Assert.Equal("ändrad i dag 11:30", rig.Picker.Rows[0].ChangedText);
        Assert.Equal("3 ärenden", rig.Picker.CountText);
    }

    [Fact]
    public async Task The_search_filters_on_the_case_number_while_typing()
    {
        var rig = new Rig();
        await rig.OpenAsync();

        rig.Picker.SearchText = "2026";

        Assert.Equal(["2026-0114", "2026-0098"], rig.Numbers);
        Assert.Equal("2 ärenden", rig.Picker.CountText);

        rig.Picker.SearchText = "301";

        Assert.Equal(["2025-0301"], rig.Numbers);
        Assert.Equal("1 ärende", rig.Picker.CountText);
        Assert.Equal("2025-0301", rig.Picker.Chosen?.CaseNumber);
    }

    [Fact]
    public async Task Nothing_matching_is_said()
    {
        var rig = new Rig();
        await rig.OpenAsync();

        rig.Picker.SearchText = "9999";

        Assert.True(rig.Picker.IsEmpty);
        Assert.Equal(CasePickerViewModel.NoMatchText, rig.Picker.EmptyText);
        Assert.Null(rig.Picker.Chosen);
    }

    [Fact]
    public async Task A_click_chooses_and_a_second_click_opens_and_folds_in()
    {
        var rig = new Rig();
        await rig.OpenAsync();
        var row = rig.Picker.Rows[2];

        await rig.Picker.PressAsync(row);

        Assert.True(row.IsChosen);
        Assert.True(row.ShowsEnterHint);
        Assert.Null(rig.Session.Current);

        await rig.Picker.PressAsync(row);

        Assert.Equal("2026-0098", rig.Session.Current?.Info.CaseNumber);
        Assert.False(rig.Picker.IsOpen);
    }

    [Fact]
    public async Task The_arrows_move_the_choice_within_the_list_and_enter_opens_it()
    {
        var rig = new Rig();
        await rig.OpenAsync();

        rig.Picker.Move(1);
        rig.Picker.Move(1);
        rig.Picker.Move(1);
        rig.Picker.Move(-1);

        Assert.Equal("2025-0301", rig.Picker.Chosen?.CaseNumber);

        await rig.Picker.OpenChosenAsync();

        Assert.Equal("2025-0301", rig.Session.Current?.Info.CaseNumber);
    }

    [Fact]
    public async Task The_open_case_is_tagged_and_shown_as_open_now()
    {
        var rig = new Rig();
        rig.Session.Open(new TestCase("2026-0098"));

        await rig.OpenAsync();

        Assert.True(rig.Picker.HasOpenCase);
        Assert.Equal("2026-0098", rig.Picker.OpenName);
        Assert.Equal(["2026-0098"], rig.Picker.Rows.Where(row => row.IsOpenCase).Select(row => row.CaseNumber));
        Assert.Equal(CasePickerViewModel.NotVerifiedText, rig.Picker.VerifyText);
    }

    [Fact]
    public async Task A_new_title_is_shown_as_open_now()
    {
        var rig = new Rig();
        var opened = new TestCase("2026-0098");
        rig.Session.Open(opened);
        await rig.OpenAsync();

        opened.SetTitle("Investeringsbedrägeri via chatt");
        rig.Session.Refresh();

        Assert.Equal("2026-0098 · Investeringsbedrägeri via chatt", rig.Picker.OpenName);
    }

    [Fact]
    public async Task Stang_closes_the_case_and_the_picker_stays_out()
    {
        var rig = new Rig();
        rig.Session.Open(new TestCase("2026-0098"));
        await rig.OpenAsync();

        await rig.Picker.CloseCaseAsync();

        Assert.Null(rig.Session.Current);
        Assert.False(rig.Picker.HasOpenCase);
        Assert.True(rig.Picker.IsOpen);
        Assert.DoesNotContain(rig.Picker.Rows, row => row.IsOpenCase);
    }

    [Fact]
    public async Task Verifiera_says_nothing_changed()
    {
        var rig = new Rig();
        rig.Session.Open(new TestCase("2026-0098"));

        await rig.Picker.VerifyAsync();

        Assert.Equal(CasePickerViewModel.VerifiedText, rig.Picker.VerifyText);
        Assert.False(rig.Picker.IsVerifyFailed);
        Assert.False(rig.Picker.IsVerifying);
    }

    [Fact]
    public async Task Verifiera_says_where_a_chain_breaks()
    {
        var rig = new Rig();
        rig.Session.Open(new TestCase("2026-0098"));
        rig.Verifier.Result = new CaseVerification(
            DateTimeOffset.UnixEpoch, [CaseProblem.DatabaseUnsound], []);

        await rig.Picker.VerifyAsync();

        Assert.True(rig.Picker.IsVerifyFailed);
        Assert.Equal(Commands.VerificationText.For(CaseProblem.DatabaseUnsound), rig.Picker.VerifyText);
    }

    [Fact]
    public async Task Another_opening_has_not_been_verified()
    {
        var rig = new Rig();
        rig.Session.Open(new TestCase("2026-0098"));
        await rig.Picker.VerifyAsync();

        rig.Session.Open(new TestCase("2026-0114"));

        Assert.Equal(CasePickerViewModel.NotVerifiedText, rig.Picker.VerifyText);
    }

    [Fact]
    public async Task Nytt_arende_folds_in_and_asks_for_the_panel()
    {
        var rig = new Rig();
        await rig.OpenAsync();
        var asked = 0;
        rig.Picker.NewCaseWanted += (_, _) => asked++;

        rig.Picker.NewCaseCommand.Execute(null);

        Assert.Equal(1, asked);
        Assert.False(rig.Picker.IsOpen);
    }

    [Fact]
    public async Task A_case_that_cannot_be_opened_is_said_in_the_picker()
    {
        var rig = new Rig();
        await rig.OpenAsync();
        rig.Store.Files.RemoveAll(file => file.CaseNumber == "2026-0114");

        await rig.Picker.OpenChosenAsync();

        Assert.True(rig.Picker.HasError);
        Assert.True(rig.Picker.IsOpen);
        Assert.Null(rig.Session.Current);
    }
}
