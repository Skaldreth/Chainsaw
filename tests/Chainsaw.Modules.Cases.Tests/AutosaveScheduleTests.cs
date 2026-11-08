using Microsoft.Extensions.Time.Testing;

namespace Chainsaw.Modules.Cases.Tests;

/// <summary>When the schedule calls the case back, and what it says when asked whether a save is due.</summary>
public sealed class AutosaveScheduleTests : IDisposable
{
    private static readonly TimeSpan Quiet = TimeSpan.FromSeconds(5);

    private static readonly TimeSpan Maximum = TimeSpan.FromSeconds(60);

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(1);

    private readonly FakeTimeProvider _time = new();

    private readonly AutosaveSchedule _schedule;

    private int _calls;

    public AutosaveScheduleTests() => _schedule = new AutosaveSchedule(_time, Quiet, Maximum, () => _calls++);

    public void Dispose() => _schedule.Dispose();

    [Fact]
    public void Nothing_is_scheduled_before_the_case_has_started()
    {
        _schedule.Written();
        _time.Advance(Maximum);

        Assert.Equal(0, _calls);
        Assert.False(_schedule.IsDue());
        Assert.True(_schedule.IsDirty);
    }

    [Fact]
    public void A_write_made_while_opening_is_scheduled_when_the_case_starts()
    {
        _schedule.Written();
        _time.Advance(Quiet);
        _schedule.Start();

        _time.Advance(Tick);

        Assert.Equal(1, _calls);
        Assert.True(_schedule.IsDue());
    }

    [Fact]
    public void The_callback_comes_after_the_quiet_period_and_the_save_is_then_due()
    {
        _schedule.Start();
        _schedule.Written();

        _time.Advance(Quiet - Tick);
        Assert.Equal(0, _calls);
        Assert.False(_schedule.IsDue());

        _time.Advance(Tick);
        Assert.Equal(1, _calls);
        Assert.True(_schedule.IsDue());
    }

    [Fact]
    public void A_save_stops_the_timer()
    {
        _schedule.Start();
        _schedule.Written();
        _schedule.Saved();

        _time.Advance(Maximum);

        Assert.Equal(0, _calls);
        Assert.False(_schedule.IsDirty);
        Assert.False(_schedule.IsDue());
    }

    [Fact]
    public void A_callback_that_comes_too_early_sets_the_timer_for_the_right_time()
    {
        _schedule.Start();
        _schedule.Written();
        _time.Advance(Quiet - TimeSpan.FromSeconds(1));

        // As when the timer fired just before a new write moved the save on.
        Assert.False(_schedule.IsDue());
        _time.Advance(TimeSpan.FromSeconds(1));

        Assert.Equal(1, _calls);
        Assert.True(_schedule.IsDue());
    }

    [Fact]
    public void A_busy_case_is_asked_again_after_the_busy_wait()
    {
        _schedule.Start();
        _schedule.Written();
        _time.Advance(Quiet);
        _schedule.Busy();

        _time.Advance(AutosaveSchedule.BusyWait - Tick);
        Assert.Equal(1, _calls);

        _time.Advance(Tick);
        Assert.Equal(2, _calls);
    }

    [Fact]
    public void A_busy_case_without_unsaved_writes_is_not_asked_again()
    {
        _schedule.Start();
        _schedule.Busy();

        _time.Advance(Maximum);

        Assert.Equal(0, _calls);
    }

    [Fact]
    public void A_failed_save_is_tried_again_after_the_maximum_delay_also_when_new_writes_come()
    {
        _schedule.Start();
        _schedule.Written();
        _time.Advance(Quiet);
        _schedule.Failed();
        _schedule.Written();

        _time.Advance(Maximum - Tick);
        Assert.False(_schedule.IsDue());

        _time.Advance(Tick);
        Assert.True(_schedule.IsDue());
    }

    [Fact]
    public void Failures_in_a_row_wait_twice_as_long_each_time_up_to_ten_minutes()
    {
        _schedule.Start();
        _schedule.Written();
        TimeSpan[] waits =
        [
            TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(8),
            TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10),
        ];

        foreach (var wait in waits)
        {
            _schedule.Failed();
            _time.Advance(wait - Tick);
            Assert.False(_schedule.IsDue());
            _time.Advance(Tick);
            Assert.True(_schedule.IsDue());
        }
    }

    [Fact]
    public void A_successful_save_ends_the_run_of_failures()
    {
        _schedule.Start();
        _schedule.Written();
        _schedule.Failed();
        _schedule.Failed();
        _schedule.Saved();
        _schedule.Written();
        _schedule.Failed();

        _time.Advance(Maximum);

        Assert.True(_schedule.IsDue());
    }

    [Fact]
    public void Failures_are_counted_in_a_row_and_a_save_says_whether_it_ended_a_run_of_them()
    {
        _schedule.Start();
        _schedule.Written();

        Assert.Equal(1, _schedule.Failed());
        Assert.Equal(2, _schedule.Failed());
        Assert.True(_schedule.Saved());
        Assert.False(_schedule.Saved());
        Assert.Equal(1, _schedule.Failed());
    }

    [Fact]
    public void A_successful_save_after_a_failure_lets_the_next_write_be_saved_after_the_quiet_period()
    {
        _schedule.Start();
        _schedule.Written();
        _schedule.Failed();
        _schedule.Saved();
        _schedule.Written();

        _time.Advance(Quiet);

        Assert.True(_schedule.IsDue());
    }

    [Fact]
    public void A_disposed_schedule_never_calls_back()
    {
        _schedule.Start();
        _schedule.Written();
        _schedule.Dispose();
        _schedule.Written();
        _schedule.Busy();
        _schedule.Failed();

        _time.Advance(Maximum * 2);

        Assert.Equal(0, _calls);
    }

    [Fact]
    public void The_periods_must_make_sense()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AutosaveSchedule(_time, TimeSpan.Zero, Maximum, () => { }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new AutosaveSchedule(_time, Quiet, Quiet - Tick, () => { }));
    }
}
