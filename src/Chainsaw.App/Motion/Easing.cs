namespace Chainsaw.App.Motion;

/// <summary>
/// The motions that follow a fading speed: a glide that runs on with a speed fading by e every time constant, and an
/// exponential ease towards a target. Both are exact at any frame rate, as they work on the time between frames.
/// Taken over from Gawk (the user's decision 2026-11-07).
/// </summary>
public static class Easing
{
    /// <summary>Gets the factor a gliding speed keeps over a time, fading by e every time constant.</summary>
    /// <param name="seconds">The time.</param>
    /// <param name="tau">The time constant, in seconds.</param>
    /// <returns>The share of the speed that is left.</returns>
    public static double Fade(double seconds, double tau) => Math.Exp(-seconds / tau);

    /// <summary>
    /// Gets how far a glide moves in a time: the integral of the fading speed, so that the distance is the same however
    /// the time is cut into frames.
    /// </summary>
    /// <param name="speed">The speed at the start of the time, in pixels per second.</param>
    /// <param name="seconds">The time.</param>
    /// <param name="tau">The time constant, in seconds.</param>
    /// <returns>The distance in pixels.</returns>
    public static double GlideDistance(double speed, double seconds, double tau) => speed * tau * (1 - Fade(seconds, tau));

    /// <summary>
    /// Moves a value towards a target as an exponential ease: the distance left shrinks by e every time constant, and
    /// the value lands on the target once it is close enough.
    /// </summary>
    /// <param name="value">Where the value is.</param>
    /// <param name="target">Where it is going.</param>
    /// <param name="seconds">The time of the step.</param>
    /// <param name="tau">The time constant, in seconds.</param>
    /// <param name="snap">How close to the target the value lands on it.</param>
    /// <returns>The value after the step.</returns>
    public static double Toward(double value, double target, double seconds, double tau, double snap)
    {
        var remaining = target - value;
        return Math.Abs(remaining) < snap ? target : value + (remaining * (1 - Fade(seconds, tau)));
    }
}
