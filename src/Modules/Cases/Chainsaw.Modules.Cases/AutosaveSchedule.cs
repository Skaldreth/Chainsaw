namespace Chainsaw.Modules.Cases;

/// <summary>
/// When an open case writes its working copy back into the case file by itself: once the case has been quiet for a
/// while after a write, and at the latest a fixed time after the first write that has not been saved, so that a case
/// that is written to all the time is still saved now and then.
/// </summary>
/// <remarks>
/// <para>
/// The schedule only decides when; the case saves, under its own gate, when the timer calls it back (see
/// <c>Case</c>). The schedule is thread-safe on its own, since the timer's callback asks it to wait again when the
/// case is busy, without holding the case's gate.
/// </para>
/// <para>
/// The timer runs on the given <see cref="TimeProvider"/>, so that the tests move time on themselves instead of
/// waiting.
/// </para>
/// </remarks>
internal sealed class AutosaveSchedule : IDisposable
{
    /// <summary>
    /// The shortest wait the timer is given. A wait of zero could run the callback at once on the thread that asked,
    /// which holds the case's gate after a write; the callback should come afterwards, on a thread of its own.
    /// </summary>
    internal static readonly TimeSpan MinimumWait = TimeSpan.FromMilliseconds(1);

    /// <summary>The longest wait before a save is tried again after saves that failed one after another.</summary>
    internal static readonly TimeSpan MaximumFailureWait = TimeSpan.FromMinutes(10);

    /// <summary>How long to wait before trying again when the case was busy with something else.</summary>
    internal static readonly TimeSpan BusyWait = TimeSpan.FromSeconds(1);

    /// <summary>Guards every field below and the timer.</summary>
    private readonly Lock _sync = new();

    /// <summary>The time the schedule follows.</summary>
    private readonly TimeProvider _time;

    /// <summary>How long the case must be quiet after a write before it is saved.</summary>
    private readonly TimeSpan _quietPeriod;

    /// <summary>
    /// The longest a write waits to be saved while writes keep coming, and how long a failed save waits before the
    /// next attempt.
    /// </summary>
    private readonly TimeSpan _maximumDelay;

    /// <summary>The timer that calls the case back; stopped until a save is due.</summary>
    private readonly ITimer _timer;

    /// <summary>Whether the case has opened, so that the schedule may run; nothing is saved before that.</summary>
    private bool _started;

    /// <summary>Whether the schedule has been disposed; the timer is then never set again.</summary>
    private bool _disposed;

    /// <summary>Whether the working copy holds writes that the case file does not.</summary>
    private bool _dirty;

    /// <summary>When the last write was committed.</summary>
    private DateTimeOffset _lastWrite;

    /// <summary>When the case is saved at the latest, counted from the first write that has not been saved.</summary>
    private DateTimeOffset _deadline;

    /// <summary>How many saves have failed one after another since the last that succeeded.</summary>
    private int _failures;

    /// <summary>Before this time no save is tried, after one that failed.</summary>
    private DateTimeOffset _notBefore = DateTimeOffset.MinValue;

    /// <summary>Creates a stopped schedule.</summary>
    /// <param name="time">The time to follow.</param>
    /// <param name="quietPeriod">How long the case must be quiet after a write before it is saved.</param>
    /// <param name="maximumDelay">
    /// The longest a write waits to be saved, and the wait after a failed save; at least
    /// <paramref name="quietPeriod"/>.
    /// </param>
    /// <param name="due">Called on the timer's thread when a save may be due.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A period is not positive, or the delay is shorter than the quiet.
    /// </exception>
    public AutosaveSchedule(TimeProvider time, TimeSpan quietPeriod, TimeSpan maximumDelay, Action due)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(due);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(quietPeriod, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumDelay, quietPeriod);
        _time = time;
        _quietPeriod = quietPeriod;
        _maximumDelay = maximumDelay;
        _timer = time.CreateTimer(_ => due(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Gets whether the working copy holds writes that have not been saved.</summary>
    public bool IsDirty
    {
        get
        {
            lock (_sync)
            {
                return _dirty;
            }
        }
    }

    /// <summary>Lets the schedule run once the case has opened; a write made while opening is scheduled then.</summary>
    public void Start()
    {
        lock (_sync)
        {
            _started = true;
            ArmForNextSave();
        }
    }

    /// <summary>
    /// Records a committed write and moves the save to after the quiet period, but never past the deadline.
    /// </summary>
    public void Written()
    {
        lock (_sync)
        {
            var now = _time.GetUtcNow();
            if (!_dirty)
            {
                _dirty = true;
                _deadline = now + _maximumDelay;
            }

            _lastWrite = now;
            ArmForNextSave();
        }
    }

    /// <summary>Records that the case file now holds everything, and stops the timer.</summary>
    /// <returns><see langword="true"/> when saves had failed before this one, so that the case can say so.</returns>
    public bool Saved()
    {
        lock (_sync)
        {
            var hadFailed = _failures > 0;
            _dirty = false;
            _notBefore = DateTimeOffset.MinValue;
            _failures = 0;
            Arm(Timeout.InfiniteTimeSpan);
            return hadFailed;
        }
    }

    /// <summary>
    /// Asks, when the timer has called back, whether the case should be saved now. When it should not yet, the timer
    /// is set for the time it will be.
    /// </summary>
    /// <returns><see langword="true"/> when there are unsaved writes and their time has come.</returns>
    public bool IsDue()
    {
        lock (_sync)
        {
            if (!_dirty || !_started)
            {
                return false;
            }

            if (_time.GetUtcNow() >= NextSave())
            {
                return true;
            }

            ArmForNextSave();
            return false;
        }
    }

    /// <summary>The case was busy when the timer called back; try again a little later.</summary>
    public void Busy()
    {
        lock (_sync)
        {
            if (_dirty && _started)
            {
                Arm(Max(NextSave() - _time.GetUtcNow(), BusyWait));
            }
        }
    }

    /// <summary>
    /// The save failed; the writes stay unsaved and the next attempt waits the maximum delay, doubled for each
    /// failure in a row up to <see cref="MaximumFailureWait"/>.
    /// </summary>
    /// <returns>How many saves have failed in a row, this one included.</returns>
    public int Failed()
    {
        lock (_sync)
        {
            _failures++;
            _notBefore = _time.GetUtcNow() + FailureWait(_failures);
            ArmForNextSave();
            return _failures;
        }
    }

    /// <summary>Stops the timer for good; later calls change nothing.</summary>
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Dispose();
        }
    }

    /// <summary>
    /// Gives the wait after a run of failed saves: the maximum delay, doubled for each failure after the first, but
    /// never more than <see cref="MaximumFailureWait"/>, so that a file that stays locked is not tried every minute.
    /// </summary>
    /// <param name="failures">The failures in a row, at least one.</param>
    /// <returns>The wait.</returns>
    private TimeSpan FailureWait(int failures)
    {
        var wait = _maximumDelay;
        for (var failure = 1; failure < failures && wait < MaximumFailureWait; failure++)
        {
            wait *= 2;
        }

        return wait < MaximumFailureWait ? wait : MaximumFailureWait;
    }

    /// <summary>Gives the larger of two spans.</summary>
    /// <param name="first">One span.</param>
    /// <param name="second">The other.</param>
    /// <returns>The larger.</returns>
    private static TimeSpan Max(TimeSpan first, TimeSpan second) => first > second ? first : second;

    /// <summary>
    /// Gives when the next save is due: after the quiet period or at the deadline, whichever comes first, but not
    /// before a failed save may be tried again. Called with <see cref="_sync"/> held.
    /// </summary>
    /// <returns>The time.</returns>
    private DateTimeOffset NextSave()
    {
        var quiet = _lastWrite + _quietPeriod;
        var due = quiet < _deadline ? quiet : _deadline;
        return due > _notBefore ? due : _notBefore;
    }

    /// <summary>Sets the timer for the next save when one is pending; called with <see cref="_sync"/> held.</summary>
    private void ArmForNextSave()
    {
        if (_dirty && _started)
        {
            Arm(NextSave() - _time.GetUtcNow());
        }
    }

    /// <summary>Sets the timer, unless the schedule has been disposed; called with <see cref="_sync"/> held.</summary>
    /// <param name="wait">The wait; infinite stops the timer, any other is at least <see cref="MinimumWait"/>.</param>
    private void Arm(TimeSpan wait)
    {
        if (_disposed)
        {
            return;
        }

        _timer.Change(wait == Timeout.InfiniteTimeSpan ? wait : Max(wait, MinimumWait), Timeout.InfiniteTimeSpan);
    }
}
