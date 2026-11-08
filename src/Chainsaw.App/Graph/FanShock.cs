using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>A ring of the circular shock at a moment: how far out it is and how visible.</summary>
/// <param name="Radius">The ring's radius, in screen pixels.</param>
/// <param name="Opacity">How visible it is.</param>
public readonly record struct ShockRing(double Radius, double Opacity);

/// <summary>
/// The circular shock and the centring that open a pile, the same in the graph and on the start page (sketches EX2b
/// and GR8a): two rings leave the pile one after the other, growing fast and slowing as they fade, while the pile
/// glides to the middle. The start page's <c>FanOutLayer</c> animates these values with WPF; the graph's surface steps
/// them on its frames. Without WPF, so that it is tested.
/// </summary>
public static class FanShock
{
    /// <summary>How many rings leave the pile.</summary>
    public const int RingCount = 2;

    /// <summary>The radius a ring starts from.</summary>
    public const double StartRadius = 12;

    /// <summary>The radius a ring reaches, past the edges of any window.</summary>
    public const double EndRadius = 1150;

    /// <summary>How visible a ring is as it leaves the pile.</summary>
    public const double RingOpacity = 0.55;

    /// <summary>How long a ring takes to grow out and fade.</summary>
    public static readonly TimeSpan RingTime = TimeSpan.FromMilliseconds(720);

    /// <summary>How long after the first ring the second one leaves.</summary>
    public static readonly TimeSpan SecondRingDelay = TimeSpan.FromMilliseconds(115);

    /// <summary>How long the pile takes to glide to the middle.</summary>
    public static readonly TimeSpan GlideTime = TimeSpan.FromMilliseconds(560);

    /// <summary>Gets how long the whole shock lasts, until the last ring has faded.</summary>
    public static TimeSpan Length => Delay(RingCount - 1) + RingTime;

    /// <summary>Gives when a ring leaves the pile.</summary>
    /// <param name="index">The ring, from 0.</param>
    /// <returns>Its delay after the first.</returns>
    public static TimeSpan Delay(int index) => SecondRingDelay * index;

    /// <summary>Gives a ring as it is some time after the shock began.</summary>
    /// <param name="index">The ring, from 0.</param>
    /// <param name="elapsed">The time since the shock began.</param>
    /// <returns>The ring, or <see langword="null"/> before it leaves and after it has faded.</returns>
    public static ShockRing? Ring(int index, TimeSpan elapsed)
    {
        var t = (elapsed - Delay(index)) / RingTime;
        if (t is < 0 or >= 1)
        {
            return null;
        }

        var grown = 1 - Math.Pow(1 - t, 3);
        return new ShockRing(StartRadius + ((EndRadius - StartRadius) * grown), RingOpacity * (1 - t));
    }

    /// <summary>
    /// Gives the viewport on its way to putting the pile in the middle of the screen: the surface is panned, never
    /// zoomed, eased in and out as the start page's pile glides.
    /// </summary>
    /// <param name="from">The viewport when the pile began to open.</param>
    /// <param name="pile">The pile's centre on the surface.</param>
    /// <param name="middle">The middle of the screen.</param>
    /// <param name="share">How far the glide has come, from 0 to 1.</param>
    /// <returns>The viewport.</returns>
    public static Viewport Centring(Viewport from, GraphPoint pile, GraphPoint middle, double share)
    {
        ArgumentNullException.ThrowIfNull(from);
        var onScreen = from.ToScreen(pile);
        var eased = HeldBackMotion.EaseInOut(HeldBackMotion.Unit(share));
        return from.Pan((middle.X - onScreen.X) * eased, (middle.Y - onScreen.Y) * eased);
    }
}
