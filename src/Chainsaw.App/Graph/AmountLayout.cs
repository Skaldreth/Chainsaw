namespace Chainsaw.App.Graph;

/// <summary>Where a line's amount is written.</summary>
/// <param name="Anchor">The point the amount's first row is centred on, on the line.</param>
/// <param name="Box">The room the amount takes, its value and swap pill included.</param>
public sealed record AmountSpot(GraphPoint Anchor, GraphRect Box);

/// <summary>
/// Finds where each line's amount is written so that no two amounts lie on each other and no amount lies on a node
/// (TODO 34, principle 7): halfway along the line when there is room, else the nearest free place along it. A line
/// with no free place keeps its amount for hovering, as before (CLAUDE.md, <em>Grafen använder ytan</em>).
/// </summary>
/// <remarks>
/// The room an amount takes is estimated from its characters, a little on the large side, so that the layout can be
/// worked out and tested without WPF; the canvas measures the real text, which is never larger, and centres it on the
/// same anchor.
/// </remarks>
public static class AmountLayout
{
    /// <summary>The widest a character of an amount is at 11 px, a digit in Segoe UI with a little to spare.</summary>
    public const double CharWidth = 6.4;

    /// <summary>The token's round badge before the amount (sketch TK1a): 14 px across.</summary>
    public const double BadgeSize = 14;

    /// <summary>The gap between the token's badge and the amount.</summary>
    public const double BadgeGap = 4;

    /// <summary>The room a token's badge before the amount takes, with the gap after it (sketch TK1a).</summary>
    public const double GlyphWidth = BadgeSize + BadgeGap;

    /// <summary>The height of the amount's row at 11 px.</summary>
    public const double TextHeight = 15;

    /// <summary>The height of the value in the currency, small under the amount.</summary>
    public const double ValueHeight = 13;

    /// <summary>The room the mark before a missing value takes (sketch KU1a): a 10 px icon and its gap.</summary>
    public const double GapMarkWidth = 13;

    /// <summary>The height of a swap's pill on its line (sketch TK1a: 26 px).</summary>
    public const double SwapHeight = 26;

    /// <summary>The room a pair of token badges with the arrow between them takes in a swap's pill.</summary>
    public const double SwapPairWidth = BadgeSize + 5 + 12 + 5 + BadgeSize + 1;

    /// <summary>The space inside a swap's pill at its ends, and between two pairs in it.</summary>
    public const double SwapPadding = 8;

    /// <summary>The space beside the text inside the amount's dark box.</summary>
    public const double PaddingX = 5;

    /// <summary>The space above and below the text inside the amount's dark box.</summary>
    public const double PaddingY = 1;

    /// <summary>
    /// How far an amount keeps from a node's frame: the marks in the corner and the chain's name under it reach this
    /// far outside it.
    /// </summary>
    public const double NodeMargin = 8;

    /// <summary>How much narrower the value's characters are than the amount's, at 9,5 px instead of 11.</summary>
    private const double ValueScale = 9.5 / 11;

    /// <summary>How far apart along a line the places an amount is tried at are, as a part of the line.</summary>
    private const double AlongStep = 0.04;

    /// <summary>How many places along a line an amount is tried at: the middle and nine to each side, 0,14 to 0,86.</summary>
    private const int Tries = 19;

    /// <summary>
    /// How far above or below its line an amount may stand when no place on the line is free: just clear of the line,
    /// so that it still reads as the line's.
    /// </summary>
    private const double BesideLine = 11;

    /// <summary>Where across the line an amount is tried: on it first, then just above it, then just below it.</summary>
    private static readonly double[] Beside = [0, -BesideLine, BesideLine];

    /// <summary>The places along a line an amount is tried at, from the middle outwards.</summary>
    private static readonly double[] Along =
        [.. Enumerable.Range(0, Tries).Select(step => 0.5 + (Side(step) * AlongStep))];

    /// <summary>Gives the room an amount takes when its first row is centred on a point.</summary>
    /// <param name="edge">The line.</param>
    /// <param name="anchor">The point.</param>
    /// <returns>The box, its value included; a swap's pill stands on the line (<see cref="SwapBox"/>).</returns>
    public static GraphRect BoxAt(EdgeView edge, GraphPoint anchor)
    {
        ArgumentNullException.ThrowIfNull(edge);
        var glyph = edge.Glyph.Length > 0 ? GlyphWidth : 0;
        var text = (edge.Label.Length * CharWidth) + glyph;
        var mark = edge.Value is null && edge.ValueGap is not null ? GapMarkWidth : 0;
        var value = (edge.ValueLine.Length * CharWidth * ValueScale) + mark;
        var width = Math.Max(text, value) + (2 * PaddingX);
        var height = TextHeight + (edge.HasValueLine ? ValueHeight : 0);
        return new GraphRect(
            anchor.X - (width / 2), anchor.Y - (TextHeight / 2) - PaddingY, width, height + (2 * PaddingY));
    }

    /// <summary>
    /// Gives where a swap's pill stands (sketch TK1a): on its line, halfway along it, so that the money is seen to
    /// change token on the way and the line runs into and out of the pill.
    /// </summary>
    /// <param name="edge">The line.</param>
    /// <param name="curve">Its curve.</param>
    /// <returns>The pill's box, or <see langword="null"/> when the line carries no swap.</returns>
    public static GraphRect? SwapBox(EdgeView edge, EdgeCurve curve)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(curve);
        var pairs = edge.SwapPairs.Count;
        if (pairs == 0)
        {
            return null;
        }

        var width = (2 * SwapPadding) + (pairs * SwapPairWidth) + ((pairs - 1) * SwapPadding);
        var middle = curve.Middle;
        return new GraphRect(middle.X - (width / 2), middle.Y - (SwapHeight / 2), width, SwapHeight);
    }

    /// <summary>
    /// Places every line's amount: the traced lines first, since the victims' money must be read, then the rest in
    /// the graph's order. The swaps' pills stand on their lines first, so that no amount lies on one.
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="places">Each node's top-left corner.</param>
    /// <param name="curves">Each line's curve.</param>
    /// <returns>The amounts that have a free place, by the line's key; a line without one is missing.</returns>
    public static IReadOnlyDictionary<string, AmountSpot> Place(
        GraphScene scene,
        IReadOnlyDictionary<string, GraphPoint> places,
        IReadOnlyDictionary<string, EdgeCurve> curves)
    {
        var spots = new Dictionary<string, AmountSpot>(StringComparer.Ordinal);
        var taken = Taken(scene, places, curves, spots);
        PlaceInto(scene.Edges, curves, taken, spots);
        return spots;
    }

    /// <summary>
    /// Gives the room taken around the amounts: the nodes with their margin, the swaps' pills and the amounts already
    /// placed. A drag keeps it and changes only what moved (<see cref="SceneLayout.Move"/>).
    /// </summary>
    /// <param name="scene">The graph.</param>
    /// <param name="places">Each node's top-left corner.</param>
    /// <param name="curves">Each line's curve.</param>
    /// <param name="spots">The amounts placed so far.</param>
    /// <returns>The room taken.</returns>
    public static RoomGrid Taken(
        GraphScene scene,
        IReadOnlyDictionary<string, GraphPoint> places,
        IReadOnlyDictionary<string, EdgeCurve> curves,
        IReadOnlyDictionary<string, AmountSpot> spots)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(curves);
        ArgumentNullException.ThrowIfNull(spots);
        var taken = new RoomGrid();
        foreach (var (key, place) in places)
        {
            taken.Add(NodeRoom(scene, key, place));
        }

        foreach (var edge in scene.Edges)
        {
            if (curves.TryGetValue(edge.Key, out var curve) && SwapBox(edge, curve) is { } pill)
            {
                taken.Add(pill);
            }
        }

        foreach (var spot in spots.Values)
        {
            taken.Add(spot.Box);
        }

        return taken;
    }

    /// <summary>Gives the room a node keeps free of amounts: its frame and the marks around it.</summary>
    /// <param name="scene">The graph.</param>
    /// <param name="key">The node's key.</param>
    /// <param name="place">Its top-left corner.</param>
    /// <returns>The room.</returns>
    public static GraphRect NodeRoom(GraphScene scene, string key, GraphPoint place)
    {
        ArgumentNullException.ThrowIfNull(scene);
        return scene.FrameOf(key, place).Inflate(NodeMargin);
    }

    /// <summary>
    /// Places the amounts of some lines where they touch nothing taken, the traced first since the victims' money must
    /// be read, then in the order given, and takes their room.
    /// </summary>
    /// <param name="lines">The lines, in the graph's order; one without a text or a curve is passed over.</param>
    /// <param name="curves">Each line's curve.</param>
    /// <param name="taken">The room taken, which the amounts placed are added to.</param>
    /// <param name="spots">Where the amounts placed are written, by the line's key.</param>
    public static void PlaceInto(
        IEnumerable<EdgeView> lines,
        IReadOnlyDictionary<string, EdgeCurve> curves,
        RoomGrid taken,
        IDictionary<string, AmountSpot> spots)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(curves);
        ArgumentNullException.ThrowIfNull(taken);
        ArgumentNullException.ThrowIfNull(spots);
        var order = lines
            .Where(edge => edge.Label.Length > 0 && curves.ContainsKey(edge.Key))
            .OrderByDescending(edge => edge.IsTraced);
        foreach (var edge in order)
        {
            if (FreeSpot(edge, curves[edge.Key], taken) is { } spot)
            {
                spots[edge.Key] = spot;
                taken.Add(spot.Box);
            }
        }
    }

    /// <summary>Gives the steps from the middle of a line in the order they are tried: 0, −1, +1, −2, +2 …</summary>
    /// <param name="step">The try, from 0.</param>
    /// <returns>How many steps from the middle, and to which side.</returns>
    private static int Side(int step) => step % 2 == 0 ? step / 2 : -((step + 1) / 2);

    /// <summary>Gives the first place along a line where its amount touches nothing taken.</summary>
    /// <param name="edge">The line.</param>
    /// <param name="curve">Its curve.</param>
    /// <param name="taken">The room already taken.</param>
    /// <returns>The place, or <see langword="null"/> when there is none.</returns>
    private static AmountSpot? FreeSpot(EdgeView edge, EdgeCurve curve, RoomGrid taken)
    {
        foreach (var beside in Beside)
        {
            foreach (var t in Along)
            {
                var onLine = curve.At(t);
                var anchor = onLine with { Y = onLine.Y + beside };
                var box = BoxAt(edge, anchor);
                if (!taken.Touches(box))
                {
                    return new AmountSpot(anchor, box);
                }
            }
        }

        return null;
    }
}
