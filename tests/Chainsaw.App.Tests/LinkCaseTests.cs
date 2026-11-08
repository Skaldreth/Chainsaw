using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// Sketch LN1a: a link that waited while Chainsaw was locked comes at the top of the case picker after the unlock,
/// to be laid in a case or to start a new one; the lock screen only says that a link waits.
/// </summary>
public sealed class LinkCaseTests
{
    private static readonly string Link = "chainsaw:bitcoin/" + Samples.Address;

    [Fact]
    public void The_lock_screen_says_that_a_link_waits_and_nothing_more()
    {
        var rig = new GateRig(VaultState.Locked, withCasePicker: true);

        rig.Links.Receive(Link);

        Assert.True(rig.LockScreen.HasWaitingLink);
        Assert.DoesNotContain(Samples.Address, LockViewModel.LinkWaitingText, StringComparison.Ordinal);
        Assert.DoesNotContain(Samples.Address, LockViewModel.LinkWaitingNote, StringComparison.Ordinal);
    }

    [Fact]
    public async Task After_the_unlock_the_picker_folds_out_with_the_link_at_the_top()
    {
        var rig = new GateRig(VaultState.Locked, withCasePicker: true);
        rig.Cases.AddFile("2026-0098", minutesAgo: 60);
        rig.Links.Receive(Link);

        await rig.LockScreen.UnlockAsync(new TestEntry("correct horse battery"));
        await rig.CasePicker!.Listing;

        var picker = rig.CasePicker;
        Assert.False(rig.LockScreen.HasWaitingLink);
        Assert.True(picker.IsOpen);
        Assert.True(picker.HasLink);
        Assert.Equal(Samples.Address, picker.LinkValue);
        Assert.Equal("Bitcoin", picker.LinkChainText);
        Assert.Equal("Länk från ett annat program · 12:00", picker.LinkArrivedText);
        Assert.Equal("Lägg i ärende", picker.ListHeading);
        Assert.Equal("Nytt ärende med adressen …", picker.NewCaseText);
        Assert.Equal("Lägg i · Enter", picker.Rows.Single().EnterHint);
        Assert.False(rig.NewCase.IsOpen);
        Assert.Empty(rig.Tracing.Added);
    }

    [Fact]
    public async Task Lagg_i_arende_opens_the_case_and_lays_the_value_in_it_with_a_line_in_the_journal()
    {
        var rig = await UnlockedWithLinkAsync();
        var picker = rig.CasePicker!;

        await picker.OpenChosenAsync();

        Assert.False(picker.IsOpen);
        Assert.False(picker.HasLink);
        var added = Assert.Single(rig.Tracing.Added);
        Assert.Equal("2026-0098", CaseSession.NumberOf(rig.Session.Current!));
        Assert.Same(rig.Session.Current, added.Case);
        var journal = ((TestCase)rig.Session.Current!).Journal;
        Assert.Contains(journal, draft => draft.Kind == AppJournalKinds.StartPointFromLink);
    }

    [Fact]
    public async Task Nytt_arende_med_adressen_opens_the_new_case_panel_with_the_value()
    {
        var rig = await UnlockedWithLinkAsync();

        rig.CasePicker!.NewCase();

        Assert.False(rig.CasePicker.IsOpen);
        Assert.True(rig.NewCase.IsOpen);
        Assert.Equal(Samples.Address, rig.NewCase.Rows.Single().StartPoint.Value);
        Assert.Empty(rig.Tracing.Added);
    }

    [Fact]
    public async Task Esc_throws_the_link_away()
    {
        var rig = await UnlockedWithLinkAsync();

        rig.CasePicker!.Close();
        rig.Shell.OpenCasePicker();

        Assert.False(rig.CasePicker.HasLink);
        Assert.Equal("Senaste", rig.CasePicker.ListHeading);
        Assert.Empty(rig.Tracing.Added);
        Assert.False(rig.NewCase.IsOpen);
    }

    [Fact]
    public async Task A_link_while_open_still_goes_to_the_bar_or_the_new_case_panel()
    {
        var rig = new GateRig(VaultState.Unlocked, withCasePicker: true);

        rig.Links.Receive(Link);
        await Task.Yield();

        Assert.False(rig.CasePicker!.HasLink);
        Assert.True(rig.NewCase.IsOpen);
    }

    [Fact]
    public void An_interrupted_replacement_of_the_file_key_is_a_warning_once_per_opening()
    {
        var rig = new GateRig(VaultState.Unlocked);
        var openCase = new TestCase("A1") { FileKeyReplacementInterrupted = true };

        rig.Session.Open(openCase);
        rig.Session.Open(openCase);

        var toast = Assert.Single(rig.Toaster.Toasts, item => item.Kind == ToastKind.Warning);
        Assert.Equal(ShellViewModel.ReplacementInterruptedText, toast.Text);
    }

    /// <summary>A rig with one case file, unlocked after a link came while it was locked.</summary>
    /// <returns>The rig, with the picker out and listed.</returns>
    private static async Task<GateRig> UnlockedWithLinkAsync()
    {
        var rig = new GateRig(VaultState.Locked, withCasePicker: true);
        rig.Cases.AddFile("2026-0098", minutesAgo: 60);
        rig.Links.Receive(Link);
        await rig.LockScreen.UnlockAsync(new TestEntry("correct horse battery"));
        await rig.CasePicker!.Listing;
        return rig;
    }
}
