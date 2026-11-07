namespace Chainsaw.App.Motion;

/// <summary>
/// Where the cards of a pile land when it is fanned out (sketches EX2b and GR8a): evenly on ellipses around the
/// pile, at most <see cref="FirstRingCapacity"/> on the first and more on each ring outside it, the first card just
/// right of straight above. The rings shrink together when the space is small, so that every card stays inside it.
/// No WPF, so that the graph's collection node and the start page's piles share it and it is tested.
/// </summary>
public static class FanOut
{
    /// <summary>How many cards the first ring holds.</summary>
    public const int FirstRingCapacity = 12;

    /// <summary>How many more cards each ring outside the first holds.</summary>
    public const int MoreEachRing = 4;

    /// <summary>The first ring's half width, as in the sketch.</summary>
    public const double FirstRadiusX = 360;

    /// <summary>The first ring's half height, as in the sketch.</summary>
    public const double FirstRadiusY = 200;

    /// <summary>How much wider, in half width, each ring is than the one inside it.</summary>
    public const double RingStepX = 110;

    /// <summary>How much higher, in half height, each ring is than the one inside it.</summary>
    public const double RingStepY = 60;

    /// <summary>How far each ring is turned against the one inside it, so that the cards do not stand in rows.</summary>
    public const double RingTurn = 0.2;

    /// <summary>The fewest cards a ring is laid out for, so that one or two cards do not stand on top of each other.</summary>
    private const int FewestSpread = 3;

    /// <summary>Gives where each card's centre lands.</summary>
    /// <param name="count">How many cards.</param>
    /// <param name="centre">The pile's centre, which the cards stand around.</param>
    /// <param name="reach">
    /// How far from the centre a card's centre may stand, in each direction: the space's half size less half a card
    /// and a margin.
    /// </param>
    /// <returns>The centres, in the cards' order.</returns>
    public static IReadOnlyList<(double X, double Y)> Slots(
        int count, (double X, double Y) centre, (double X, double Y) reach)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var rings = RingsFor(count);
        if (rings.Count == 0)
        {
            return [];
        }

        var outer = rings.Count - 1;
        var shrink = Math.Min(1, Math.Min(
            Math.Max(0, reach.X) / (FirstRadiusX + (RingStepX * outer)),
            Math.Max(0, reach.Y) / (FirstRadiusY + (RingStepY * outer))));
        var slots = new List<(double X, double Y)>(count);
        for (var ring = 0; ring < rings.Count; ring++)
        {
            var onRing = rings[ring];
            var spread = Math.Max(onRing, FewestSpread);
            var radiusX = (FirstRadiusX + (RingStepX * ring)) * shrink;
            var radiusY = (FirstRadiusY + (RingStepY * ring)) * shrink;
            for (var index = 0; index < onRing; index++)
            {
                var angle = (-Math.PI / 2) + (((index + 0.5) / spread) * 2 * Math.PI) + (ring * RingTurn);
                slots.Add((centre.X + (radiusX * Math.Cos(angle)), centre.Y + (radiusY * Math.Sin(angle))));
            }
        }

        return slots;
    }

    /// <summary>Gives how many cards stand on each ring, the first ring first.</summary>
    /// <param name="count">How many cards.</param>
    /// <returns>The cards on each ring; none for no cards.</returns>
    public static IReadOnlyList<int> RingsFor(int count)
    {
        var rings = new List<int>();
        var left = count;
        for (var capacity = FirstRingCapacity; left > 0; capacity += MoreEachRing)
        {
            var onRing = Math.Min(left, capacity);
            rings.Add(onRing);
            left -= onRing;
        }

        return rings;
    }
}
