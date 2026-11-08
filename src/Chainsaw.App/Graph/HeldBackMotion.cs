namespace Chainsaw.App.Graph;

/// <summary>The steps of the circular blast that opens and closes the selection view (sketch EX1).</summary>
public enum BlastPhase
{
    /// <summary>The graph as usual.</summary>
    None,

    /// <summary>The graph is sent out in a ring from the clue, and the address moves to the middle.</summary>
    Out,

    /// <summary>The selection view: the dots come out from the address.</summary>
    View,

    /// <summary>The graph comes back.</summary>
    Back,
}

/// <summary>Where a node is during the blast: how far it has moved, how large and how clear it is.</summary>
/// <param name="Travel">How far along its way out it is, from 0 (home) to 1 (gone).</param>
/// <param name="Scale">Its size, 1 at home.</param>
/// <param name="Opacity">How clear it is.</param>
public readonly record struct Flight(double Travel, double Scale, double Opacity)
{
    /// <summary>Gets a node at home.</summary>
    public static Flight Home { get; } = new(0, 1, 1);
}

/// <summary>
/// The timings and easings of the selection view and the pile's ring (sketches EX1 and EX2b, from the HTML's script):
/// the blast out in 720 ms, the dots in 760 ms, the way back in 820 ms, and the pile fanned out in 560 ms. Without WPF,
/// so that it is tested.
/// </summary>
public static class HeldBackMotion
{
    /// <summary>How long the graph takes to go out.</summary>
    public static readonly TimeSpan OutTime = TimeSpan.FromMilliseconds(720);

    /// <summary>How long the dots take to come out.</summary>
    public static readonly TimeSpan ViewTime = TimeSpan.FromMilliseconds(760);

    /// <summary>How long the graph takes to come back.</summary>
    public static readonly TimeSpan BackTime = TimeSpan.FromMilliseconds(820);

    /// <summary>How long a pile takes to fan out or fold.</summary>
    public static readonly TimeSpan FanTime = TimeSpan.FromMilliseconds(560);

    /// <summary>How long one sonar wave takes to swell out (sketch EX2: 2,2 s).</summary>
    public static readonly TimeSpan SonarTime = TimeSpan.FromMilliseconds(2200);

    /// <summary>How far a node flies out, in pixels on screen.</summary>
    public const double FlyDistance = 1150;

    /// <summary>How much smaller a node is when it has flown out.</summary>
    private const double FlyShrink = 0.35;

    /// <summary>How long a node's flight out lasts, in milliseconds.</summary>
    private const double FlyMilliseconds = 480;

    /// <summary>How much later a node far from the clue starts, in milliseconds per pixel.</summary>
    private const double DelayPerPixel = 0.35;

    /// <summary>The cubic ease in.</summary>
    /// <param name="t">The time, from 0 to 1.</param>
    /// <returns>The eased value.</returns>
    public static double EaseIn(double t) => t * t * t;

    /// <summary>The cubic ease out.</summary>
    /// <param name="t">The time, from 0 to 1.</param>
    /// <returns>The eased value.</returns>
    public static double EaseOut(double t) => 1 - Math.Pow(1 - t, 3);

    /// <summary>The cubic ease in and out.</summary>
    /// <param name="t">The time, from 0 to 1.</param>
    /// <returns>The eased value.</returns>
    public static double EaseInOut(double t) =>
        t < 0.5 ? 4 * t * t * t : 1 - (Math.Pow((-2 * t) + 2, 3) / 2);

    /// <summary>The ease that overshoots a little and settles (the tiles landing in the ring).</summary>
    /// <param name="t">The time, from 0 to 1.</param>
    /// <returns>The eased value.</returns>
    public static double EaseBack(double t)
    {
        const double Overshoot = 1.4;
        return 1 + ((Overshoot + 1) * Math.Pow(t - 1, 3)) + (Overshoot * Math.Pow(t - 1, 2));
    }

    /// <summary>Clamps a value between 0 and 1.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The clamped value.</returns>
    public static double Unit(double value) => Math.Clamp(value, 0, 1);

    /// <summary>Gives a point between two.</summary>
    /// <param name="from">The start.</param>
    /// <param name="to">The end.</param>
    /// <param name="t">How far, from 0 to 1 (beyond for an overshoot).</param>
    /// <returns>The point.</returns>
    public static GraphPoint Lerp(GraphPoint from, GraphPoint to, double t) =>
        new(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));

    /// <summary>
    /// Gives where a node is as the graph goes out: the nearer the clue, the sooner it flies, so the graph leaves in a
    /// ring from the clue.
    /// </summary>
    /// <param name="distance">The node's distance from the clue on screen.</param>
    /// <param name="k">How far the step has come, from 0 to 1.</param>
    /// <returns>The flight.</returns>
    public static Flight Out(double distance, double k)
    {
        var t = EaseIn(Unit(((k * OutTime.TotalMilliseconds) - (distance * DelayPerPixel)) / FlyMilliseconds));
        return new Flight(t, 1 - (FlyShrink * t), 1 - t);
    }

    /// <summary>Gives where a node is as the graph comes back: the nearer the clue, the sooner it lands.</summary>
    /// <param name="distance">The node's distance from the clue on screen.</param>
    /// <param name="k">How far the step has come, from 0 to 1.</param>
    /// <returns>The flight.</returns>
    public static Flight Back(double distance, double k)
    {
        var t = EaseOut(Unit((k * 1.25) - (distance / 2200)));
        return new Flight(1 - t, 0.65 + (FlyShrink * t), t);
    }

    /// <summary>
    /// Gives how clear the graph's lines are in a step: they fade first going out, and come last back.
    /// </summary>
    /// <param name="phase">The step.</param>
    /// <param name="k">How far it has come.</param>
    /// <returns>The opacity.</returns>
    public static double LineOpacity(BlastPhase phase, double k) => phase switch
    {
        BlastPhase.Out => 1 - Unit(k * 3),
        BlastPhase.View => 0,
        BlastPhase.Back => Unit((k - 0.6) * 2.5),
        _ => 1,
    };

    /// <summary>Gives how far a dot has come out from the address in the view's first moment.</summary>
    /// <param name="index">The dot's place in the layout.</param>
    /// <param name="count">How many dots.</param>
    /// <param name="k">How far the step has come.</param>
    /// <returns>From 0 (at the address) to 1 (in place).</returns>
    public static double DotArrival(int index, int count, double k) =>
        k >= 1 ? 1 : EaseOut(Unit((k - ((double)index / Math.Max(1, count) * 0.45)) / 0.55));

    /// <summary>Gives how clear the guides and the view's chrome are as the dots come out.</summary>
    /// <param name="k">How far the step has come.</param>
    /// <returns>The opacity.</returns>
    public static double Settle(double k) => k >= 1 ? 1 : Unit((k - 0.4) / 0.6);

    /// <summary>
    /// Gives a blast ring's diameter and opacity (sketch EX1: two rings from the clue, the second a little later).
    /// </summary>
    /// <param name="ring">The ring, 0 or 1.</param>
    /// <param name="k">How far the blast has come.</param>
    /// <returns>The diameter on screen and the opacity.</returns>
    public static (double Diameter, double Opacity) Ring(int ring, double k)
    {
        var delay = ring == 0 ? 0 : 0.16;
        var t = Unit((k - delay) / (1 - delay));
        return (24 + (2300 * EaseOut(t)), 0.55 * (1 - t));
    }

    /// <summary>
    /// Gives where a tile is as a pile fans out (sketch EX2b): staggered, landing with a little overshoot.
    /// </summary>
    /// <param name="index">The tile's place.</param>
    /// <param name="count">How many tiles, so that the last too has landed when the pile is open.</param>
    /// <param name="open">How open the pile is, from 0 to 1.</param>
    /// <returns>How far along its way (eased), its opacity and its size.</returns>
    public static (double Travel, double Opacity, double Scale) Tile(int index, int count, double open)
    {
        var stagger = Math.Min(0.035, 0.5 / Math.Max(1, count));
        var t = Unit((open * 1.5) - (index * stagger));
        var eased = EaseBack(t);
        return (eased, Unit(t * 3), 0.6 + (0.4 * Unit(eased)));
    }

    /// <summary>
    /// Gives a sonar wave's swell and opacity at a moment (sketch EX2: from its size to 2,6 times, fading).
    /// </summary>
    /// <param name="phase">Where in the wave, from 0 to 1.</param>
    /// <returns>The scale and the opacity.</returns>
    public static (double Scale, double Opacity) Sonar(double phase)
    {
        var t = EaseOut(Unit(phase));
        return (1 + (1.6 * t), 0.8 * (1 - t));
    }
}
