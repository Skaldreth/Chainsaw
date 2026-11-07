using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>How a pile's cards are fanned out around it (sketches EX1a and EX1b, two modes of the same view).</summary>
public enum PileFanMode
{
    /// <summary>A spiral around the pile, the largest amounts nearest (EX1a).</summary>
    Spiral,

    /// <summary>Arcs away from the actor in time order, the earliest at the top (EX1b).</summary>
    TimeArc,
}

/// <summary>
/// Lays a pile's cards out around it (sketches EX1a and EX1b), without overlap, so that the user can see them and pick
/// some out. Works on the surface's units, without WPF.
/// </summary>
public static class PileFanLayout
{
    /// <summary>A card's width.</summary>
    public const double CardWidth = 112;

    /// <summary>A card's height.</summary>
    public const double CardHeight = 30;

    /// <summary>The space kept between two cards.</summary>
    public const double CardGap = 6;

    /// <summary>
    /// How much wider than high the spiral is: cards are wide and low, so the turns lie wider apart sideways than up
    /// and down.
    /// </summary>
    private const double SpiralAspect = 3;

    /// <summary>The spiral's first radius (up and down), so that its first turn clears the pile's own node.</summary>
    private const double SpiralStart = 46;

    /// <summary>
    /// How much the spiral's radius (up and down) grows per turn: enough that the turns clear each other also on the
    /// diagonals, where they lie closest.
    /// </summary>
    private const double SpiralTurnGap = 56;

    /// <summary>The small step the spiral is walked in, in radians, to find the next free place.</summary>
    private const double SpiralStep = 0.01;

    /// <summary>The first arc's radius in the time arc.</summary>
    private const double ArcStart = 150;

    /// <summary>How far the time arc spreads from straight out, up and down, in radians (about 75°).</summary>
    private const double ArcSpread = 1.3;

    /// <summary>Gives every card's top-left corner around the pile.</summary>
    /// <param name="cards">The cards to lay out, in the order they are given.</param>
    /// <param name="centre">The pile's centre.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="side">The pile's side, which way the time arc opens.</param>
    /// <returns>The places by the cards' keys.</returns>
    public static IReadOnlyDictionary<string, GraphPoint> Place(
        IReadOnlyList<PileCard> cards, GraphPoint centre, PileFanMode mode, PileSide side)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var centres = Centres(cards.Count, mode, side);
        var places = new Dictionary<string, GraphPoint>(StringComparer.Ordinal);
        for (var index = 0; index < cards.Count; index++)
        {
            var (x, y) = centres[index];
            places[cards[index].Key] = new GraphPoint(
                centre.X + x - (CardWidth / 2), centre.Y + y - (CardHeight / 2));
        }

        return places;
    }

    /// <summary>
    /// Gives where the centres of a number of cards lie around a pile, relative to the pile's centre, without overlap;
    /// the graph's piles and the start page's piles of cases (sketch GR8a) share it, so that they fan out alike.
    /// </summary>
    /// <param name="count">How many cards.</param>
    /// <param name="mode">The mode.</param>
    /// <param name="side">The pile's side, which way the time arc opens.</param>
    /// <returns>The centres, in the cards' order.</returns>
    public static IReadOnlyList<(double X, double Y)> Centres(int count, PileFanMode mode, PileSide side) =>
        mode == PileFanMode.Spiral ? Spiral(count) : TimeArc(count, side == PileSide.In ? -1 : 1);

    /// <summary>Gives a card's frame at its place.</summary>
    /// <param name="place">The card's top-left corner.</param>
    /// <returns>The frame.</returns>
    public static GraphRect CardFrame(GraphPoint place) => new(place.X, place.Y, CardWidth, CardHeight);

    /// <summary>
    /// Gives the centres along a wide spiral, each the first place along it that clears the card before; the turns
    /// lie far enough apart that they never meet.
    /// </summary>
    /// <param name="count">How many.</param>
    /// <returns>The centres, relative to the pile's.</returns>
    private static List<(double X, double Y)> Spiral(int count)
    {
        var centres = new List<(double X, double Y)>(count);
        var angle = 0.0;
        while (centres.Count < count)
        {
            var radius = SpiralStart + (SpiralTurnGap * angle / (2 * Math.PI));
            var point = (X: SpiralAspect * radius * Math.Cos(angle), Y: radius * Math.Sin(angle));
            if (centres.Count == 0 || Clears(centres[^1], point))
            {
                centres.Add(point);
            }

            angle += SpiralStep;
        }

        return centres;
    }

    /// <summary>
    /// Gives the centres on arcs away from the actor: down each arc from the top, each the first place that clears the
    /// card before, then the next arc further out.
    /// </summary>
    /// <param name="count">How many.</param>
    /// <param name="direction">Which way the arcs open: −1 to the left, 1 to the right.</param>
    /// <returns>The centres, relative to the pile's.</returns>
    private static List<(double X, double Y)> TimeArc(int count, int direction)
    {
        var centres = new List<(double X, double Y)>(count);
        var radius = ArcStart;
        while (centres.Count < count)
        {
            (double X, double Y)? previous = null;
            for (var angle = -ArcSpread; angle <= ArcSpread && centres.Count < count; angle += SpiralStep)
            {
                var point = (X: direction * radius * Math.Cos(angle), Y: radius * Math.Sin(angle));
                if (previous is null || Clears(previous.Value, point))
                {
                    centres.Add(point);
                    previous = point;
                }
            }

            radius += CardWidth + CardGap;
        }

        return centres;
    }

    /// <summary>Says whether a card at one centre clears a card at another.</summary>
    /// <param name="first">The first centre.</param>
    /// <param name="second">The second centre.</param>
    /// <returns><see langword="true"/> when they do not overlap.</returns>
    private static bool Clears((double X, double Y) first, (double X, double Y) second) =>
        Math.Abs(first.X - second.X) >= CardWidth + CardGap || Math.Abs(first.Y - second.Y) >= CardHeight + CardGap;
}
