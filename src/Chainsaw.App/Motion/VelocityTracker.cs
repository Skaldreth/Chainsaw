namespace Chainsaw.App.Motion;

/// <summary>
/// The speed of the pointer when a drag is let go, for the glide that follows: measured over its last moments only,
/// and none when the pointer had stopped before the release, so that something put down carefully stays where it was
/// put. Taken over from Gawk (the user's decision 2026-11-07: the same soft pan as in Gawk).
/// </summary>
/// <param name="maxSpeed">The fastest speed reported, in pixels per second, against a fling that throws far off.</param>
public sealed class VelocityTracker(double maxSpeed = 5000)
{
    /// <summary>The time the speed is measured over, in milliseconds.</summary>
    public const int WindowMs = 80;

    /// <summary>How long the pointer may rest before the release and still be gliding, in milliseconds.</summary>
    public const int StillMs = 50;

    /// <summary>The pointer's last moments: when, and where.</summary>
    private readonly List<(int Time, double X, double Y)> _samples = [];

    /// <summary>Starts a new drag at a point.</summary>
    /// <param name="time">The time of the event in milliseconds, as input events give it; it may wrap.</param>
    /// <param name="x">Where the pointer is across.</param>
    /// <param name="y">Where the pointer is down.</param>
    public void Start(int time, double x, double y)
    {
        _samples.Clear();
        _samples.Add((time, x, y));
    }

    /// <summary>Adds where the pointer is now.</summary>
    /// <param name="time">The time of the event in milliseconds.</param>
    /// <param name="x">Where the pointer is across.</param>
    /// <param name="y">Where the pointer is down.</param>
    public void Add(int time, double x, double y)
    {
        _samples.Add((time, x, y));

        // Only the last moments matter; the times may wrap, so differences are used.
        var drop = 0;
        while (drop < _samples.Count - 1 && unchecked(time - _samples[drop].Time) > WindowMs)
        {
            drop++;
        }

        _samples.RemoveRange(0, drop);
    }

    /// <summary>Gets the speed of the pointer when it was let go, in pixels per second.</summary>
    /// <param name="time">The time of the release.</param>
    /// <returns>The speed across and down; none when the pointer had come to rest.</returns>
    public (double X, double Y) Release(int time)
    {
        if (_samples.Count < 2 || unchecked(time - _samples[^1].Time) > StillMs)
        {
            return (0, 0);
        }

        var (firstTime, firstX, firstY) = _samples[0];
        var (lastTime, lastX, lastY) = _samples[^1];
        var elapsed = unchecked(lastTime - firstTime);
        if (elapsed <= 0)
        {
            return (0, 0);
        }

        var x = (lastX - firstX) * 1000.0 / elapsed;
        var y = (lastY - firstY) * 1000.0 / elapsed;
        var speed = Math.Sqrt((x * x) + (y * y));
        return speed > maxSpeed ? (x * maxSpeed / speed, y * maxSpeed / speed) : (x, y);
    }
}
