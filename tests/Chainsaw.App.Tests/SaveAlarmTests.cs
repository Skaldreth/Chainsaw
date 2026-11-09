using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// The warning in the toaster when the open case cannot be saved (sketch TO1a): it comes after some failures in a row,
/// once, offers <em>Försök igen</em>, and goes when a save succeeds.
/// </summary>
public sealed class SaveAlarmTests
{
    private readonly CaseSession _session = new();

    private readonly Toaster _toaster = new(new InlineUi());

    private readonly TestCase _case = new("2026-0001");

    private SaveAlarm Alarm()
    {
        var alarm = new SaveAlarm(_session, _toaster, new InlineUi());
        _session.Open(_case);
        return alarm;
    }

    private IEnumerable<Toast> Standing => _toaster.Toasts.Where(toast => !toast.IsLeaving);

    [Fact]
    public void A_few_failures_say_nothing()
    {
        var alarm = Alarm();

        for (var failures = 1; failures < SaveAlarm.FailuresBeforeWarning; failures++)
        {
            _case.SayHowSavesGo(failures);
        }

        Assert.False(alarm.IsWarning);
        Assert.Empty(_toaster.Toasts);
    }

    [Fact]
    public void Enough_failures_in_a_row_warn_once_with_try_again()
    {
        var alarm = Alarm();

        _case.SayHowSavesGo(SaveAlarm.FailuresBeforeWarning);
        _case.SayHowSavesGo(SaveAlarm.FailuresBeforeWarning + 1);

        Assert.True(alarm.IsWarning);
        var toast = Assert.Single(_toaster.Toasts);
        Assert.Equal(ToastKind.Warning, toast.Kind);
        Assert.Equal(SaveAlarm.WarningTitle, toast.Title);
        Assert.Equal(SaveAlarm.RetryText, toast.ActionText);
        Assert.True(toast.IsSticky);
    }

    [Fact]
    public void A_save_that_succeeds_takes_the_warning_away_and_says_so()
    {
        var alarm = Alarm();
        _case.SayHowSavesGo(SaveAlarm.FailuresBeforeWarning);

        _case.SayHowSavesGo(0);

        Assert.False(alarm.IsWarning);
        var saved = Assert.Single(Standing);
        Assert.Equal(SaveAlarm.SavedTitle, saved.Title);
        Assert.Equal(ToastKind.Success, saved.Kind);
    }

    [Fact]
    public void A_success_without_a_warning_says_nothing()
    {
        Alarm();
        _case.SayHowSavesGo(1);

        _case.SayHowSavesGo(0);

        Assert.Empty(_toaster.Toasts);
    }

    [Fact]
    public async Task Try_again_saves_and_says_it_went_well()
    {
        var alarm = Alarm();
        _case.SayHowSavesGo(SaveAlarm.FailuresBeforeWarning);

        _toaster.Toasts[0].Request.Action!();
        await alarm.Retrying;

        Assert.Equal(1, _case.Saves);
        Assert.False(alarm.IsWarning);
        Assert.Equal(SaveAlarm.SavedTitle, Assert.Single(Standing, toast => toast.Kind == ToastKind.Success).Title);
    }

    [Fact]
    public async Task Try_again_that_fails_brings_the_warning_back()
    {
        var alarm = Alarm();
        _case.SayHowSavesGo(SaveAlarm.FailuresBeforeWarning);
        _case.SaveFailure = new IOException("Filen är låst.");

        _toaster.Toasts[0].Request.Action!();
        _toaster.Dismiss(_toaster.Toasts[0]);
        await alarm.Retrying;

        Assert.True(alarm.IsWarning);
        Assert.Equal(SaveAlarm.WarningTitle, Assert.Single(Standing).Title);
    }

    [Fact]
    public void A_warning_the_user_closed_comes_back_at_the_next_failure()
    {
        var alarm = Alarm();
        _case.SayHowSavesGo(SaveAlarm.FailuresBeforeWarning);

        _toaster.Remove(_toaster.Toasts[0]);
        Assert.False(alarm.IsWarning);
        _case.SayHowSavesGo(SaveAlarm.FailuresBeforeWarning + 1);

        Assert.True(alarm.IsWarning);
        Assert.Equal(SaveAlarm.WarningTitle, Assert.Single(_toaster.Toasts).Title);
    }

    [Fact]
    public async Task Try_again_that_fails_for_any_reason_brings_the_warning_back()
    {
        var alarm = Alarm();
        _case.SayHowSavesGo(SaveAlarm.FailuresBeforeWarning);
        _case.SaveFailure = new InvalidOperationException("Något annat.");

        _toaster.Toasts[0].Request.Action!();
        await alarm.Retrying;

        Assert.True(alarm.IsWarning);
    }

    [Fact]
    public void Another_case_takes_the_warning_away_and_the_old_case_is_no_longer_heard()
    {
        var alarm = Alarm();
        _case.SayHowSavesGo(SaveAlarm.FailuresBeforeWarning);

        _session.Open(new TestCase("2026-0002"));
        _case.SayHowSavesGo(SaveAlarm.FailuresBeforeWarning + 1);

        Assert.False(alarm.IsWarning);
        Assert.Empty(Standing);
    }

    [Fact]
    public async Task A_file_replaced_outside_chainsaw_closes_the_case_and_offers_its_work_as_a_case_of_its_own()
    {
        var store = new TestCaseStore
        {
            Conflict = new WorkingCopyConflict(
                "2026-0001", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "2026-0001-B"),
        };
        var workflow = new CaseWorkflow(store, new TestTracing(), new TestVault(VaultState.Unlocked), _session);
        ConflictOffer? offered = null;
        workflow.ConflictFound += (_, offer) => offered = offer;
        var alarm = new SaveAlarm(_session, _toaster, new InlineUi(), workflow);
        _session.Open(_case);
        _case.SayHowSavesGo(SaveAlarm.FailuresBeforeWarning);

        _case.SayFileReplaced();
        await alarm.KeepingBoth;

        Assert.Null(_session.Current);
        Assert.True(_case.Disposed > 0);
        Assert.Equal("2026-0001-B", offered?.Conflict.SuggestedBranchNumber);
        Assert.True(offered?.FileReplaced);
        Assert.False(alarm.IsWarning);
        Assert.Empty(Standing);
    }

    [Fact]
    public async Task A_file_replaced_without_a_conflict_to_offer_is_said_in_the_toaster()
    {
        var workflow = new CaseWorkflow(
            new TestCaseStore(), new TestTracing(), new TestVault(VaultState.Unlocked), _session);
        var alarm = new SaveAlarm(_session, _toaster, new InlineUi(), workflow);
        _session.Open(_case);

        _case.SayFileReplaced();
        await alarm.KeepingBoth;

        Assert.Null(_session.Current);
        var toast = Assert.Single(Standing);
        Assert.Equal(ToastKind.Warning, toast.Kind);
        Assert.EndsWith(CaseTexts.For(CaseError.Conflict), toast.Text, StringComparison.Ordinal);
    }
}
