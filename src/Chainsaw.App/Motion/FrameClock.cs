namespace Chainsaw.App.Motion;

/// <summary>
/// The time between the frames of a motion, from the rendering time of each frame (WPF's
/// <c>RenderingEventArgs.RenderingTime</c>). Taken over from Gawk (the user's decision 2026-11-07).
/// </summary>
/// <remarks>
/// The first frame counts as a frame at 60 Hz rather than waiting for a second one, which would hold the motion still
/// for a frame as it starts. A frame raised twice counts once, and a long frame counts as at most a tenth of a second,
/// so that one hitch does not make the motion jump.
/// </remarks>
public sealed class FrameClock
{
    /// <summary>The time of the first frame: a frame at 60 Hz.</summary>
    public const double FirstFrameSeconds = 1.0 / 60;

    /// <summary>The longest time a frame counts as.</summary>
    public const double MaxFrameSeconds = 0.1;

    /// <summary>The rendering time of the previous frame, or <see langword="null"/> before the first.</summary>
    private TimeSpan? _last;

    /// <summary>Starts anew, so that the next frame is a first frame.</summary>
    public void Reset() => _last = null;

    /// <summary>Gets the time since the previous frame.</summary>
    /// <param name="renderingTime">The rendering time of the frame.</param>
    /// <returns>The seconds, or <see langword="null"/> when the frame was already counted.</returns>
    public double? Tick(TimeSpan renderingTime)
    {
        var seconds = _last is { } last ? (renderingTime - last).TotalSeconds : FirstFrameSeconds;
        if (seconds <= 0)
        {
            return null;
        }

        _last = renderingTime;
        return Math.Min(seconds, MaxFrameSeconds);
    }
}
