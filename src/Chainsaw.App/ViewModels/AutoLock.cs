using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The automatic lock after inactivity: the window tells it whenever the user does something in Chainsaw, and asks
/// now and then whether the time without activity has run out. It also says whether Chainsaw locks with Windows. Both
/// are chosen in the settings (sketch ST1a, page Låsning).
/// </summary>
/// <param name="clock">The clock.</param>
/// <param name="timeout">How long Chainsaw may be left alone before it locks.</param>
public sealed class AutoLock(IClock clock, TimeSpan timeout)
{
    /// <summary>The shortest time that is accepted, so that Chainsaw never locks while it is being used.</summary>
    private static readonly TimeSpan Shortest = TimeSpan.FromMinutes(1);

    /// <summary>When the user last did something.</summary>
    private DateTimeOffset _lastActivity = clock.UtcNow;

    /// <summary>
    /// How many of Windows' own dialogs are open over Chainsaw, during which it never locks by itself.
    /// </summary>
    private int _holds;

    /// <summary>Raised after the time or the lock with Windows has changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Gets how long Chainsaw may be left alone before it locks.</summary>
    public TimeSpan Timeout { get; private set; } = timeout;

    /// <summary>
    /// Gets whether Chainsaw locks when Windows is locked. Sleep and a disconnected session always lock.
    /// </summary>
    public bool LocksWithWindows { get; private set; } = true;

    /// <summary>Gets the line on the lock screen that says when Chainsaw locks itself.</summary>
    public string Description
    {
        get
        {
            var minutes = (int)Math.Round(Timeout.TotalMinutes);
            var unit = minutes == 1 ? "minut" : "minuter";
            return $"Låses automatiskt efter {minutes} {unit} utan aktivitet.";
        }
    }

    /// <summary>Changes the time without activity before Chainsaw locks; it counts from the last activity.</summary>
    /// <param name="timeout">The new time, at least a minute.</param>
    /// <exception cref="ArgumentOutOfRangeException">The time is shorter than a minute.</exception>
    public void Change(TimeSpan timeout)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(timeout, Shortest);
        Timeout = timeout;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Changes whether Chainsaw locks when Windows is locked.</summary>
    /// <param name="locks">Whether it locks.</param>
    public void LockWithWindows(bool locks)
    {
        LocksWithWindows = locks;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Records that the user did something now.</summary>
    public void Touch() => _lastActivity = clock.UtcNow;

    /// <summary>Tells whether the time without activity has run out.</summary>
    /// <returns><see langword="true"/> when Chainsaw should lock.</returns>
    public bool IsDue() => _holds == 0 && clock.UtcNow - _lastActivity >= Timeout;

    /// <summary>
    /// Holds the automatic lock while one of Windows' own dialogs is open, for example the file dialog: what the user
    /// does there never reaches the window, and a lock behind an open dialog would leave the dialog's result to run
    /// after it. Ctrl+L, Windows' lock and sleep still lock. The time starts again when the hold ends.
    /// </summary>
    /// <returns>The hold; disposing it ends it, once.</returns>
    public IDisposable Hold()
    {
        _holds++;
        return new Release(this);
    }

    /// <summary>Ends a hold of the automatic lock, once, and counts the time from then.</summary>
    /// <param name="owner">The automatic lock.</param>
    private sealed class Release(AutoLock owner) : IDisposable
    {
        /// <summary>Whether the hold has ended.</summary>
        private bool _done;

        /// <summary>Ends the hold, and counts the time without activity from now.</summary>
        public void Dispose()
        {
            if (_done)
            {
                return;
            }

            _done = true;
            owner._holds--;
            owner.Touch();
        }
    }
}
