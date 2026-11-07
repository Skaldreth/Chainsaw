using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The automatic lock after inactivity: the window tells it whenever the user does something in Chainsaw, and asks
/// now and then whether the time without activity has run out.
/// </summary>
/// <param name="clock">The clock.</param>
/// <param name="timeout">How long Chainsaw may be left alone before it locks.</param>
public sealed class AutoLock(IClock clock, TimeSpan timeout)
{
    /// <summary>When the user last did something.</summary>
    private DateTimeOffset _lastActivity = clock.UtcNow;

    /// <summary>Gets how long Chainsaw may be left alone before it locks.</summary>
    public TimeSpan Timeout { get; } = timeout;

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

    /// <summary>Records that the user did something now.</summary>
    public void Touch() => _lastActivity = clock.UtcNow;

    /// <summary>Tells whether the time without activity has run out.</summary>
    /// <returns><see langword="true"/> when Chainsaw should lock.</returns>
    public bool IsDue() => clock.UtcNow - _lastActivity >= Timeout;
}
