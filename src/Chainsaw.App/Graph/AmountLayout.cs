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

    /// <summary>The room a token's mark before the amount takes, with the gap after it (sketch TK1a).</summary>
    public const double GlyphWidth = 11;

    /// <summary>The height of the amount's row at 11 px.</summary>
    public const double TextHeight = 15;

    /// <summary>The height of the value in the currency, small under the amount.</summary>
    public const double ValueHeight = 13;

    /// <summary>The room a swap's pill under the amount takes, with its gap (sketch TK1a).</summary>
    public const double SwapHeight = 20;

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

    /// <summary>The side of a cell in the grid that finds what is near an amount, in the surface's units.</summary>
    private const double Cell = 160;

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
    /// <returns>The box, its value and swap pill included.</returns>
    public static GraphRect BoxAt(EdgeView edge, GraphPoint anchor)
    {
        ArgumentNullException.ThrowIfNull(edge);
        var glyph = edge.Glyph.Length > 0 ? GlyphWidth : 0;
        var text = (edge.Label.Length * CharWidth) + glyph;
        var value = edge.Value is { } worth ? worth.Text.Length * CharWidth * ValueScale : 0;
        var width = Math.Max(text, value) + (2 * PaddingX);
        var height = TextHeight + (edge.Value is null ? 0 : ValueHeight) + (edge.Swap is null ? 0 : SwapHeight);
        return new GraphRect(
            anchor.X - (width / 2), anchor.Y - (TextHeight / 2) - PaddingY, width, height + (2 * PaddingY));
    }

    /// <summary>
    /// Places every line's amount: the traced lines first, since the victims' money must be read, then the rest in
    /// the graph's order.
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
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(curves);
        var taken = new RoomGrid();
        foreach (var place in places.Values)
        {
            taken.Add(GraphSizes.NodeFrame(place).Inflate(NodeMargin));
        }

        var spots = new Dictionary<string, AmountSpot>(StringComparer.Ordinal);
        var order = scene.Edges
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

        return spots;
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

    /// <summary>
    /// The room taken on the surface, in a grid of cells, so that an amount is only compared with what is near it and
    /// placing stays quick on a large graph while a node is dragged (principle 6).
    /// </summary>
    private sealed class RoomGrid
    {
        /// <summary>The rectangles in each cell they reach into.</summary>
        private readonly Dictionary<(int Column, int Row), List<GraphRect>> _cells = [];

        /// <summary>Takes a rectangle's room.</summary>
        /// <param name="rect">The rectangle.</param>
        public void Add(GraphRect rect)
        {
            foreach (var cell in CellsOf(rect))
            {
                if (!_cells.TryGetValue(cell, out var list))
                {
                    list = [];
                    _cells[cell] = list;
                }

                list.Add(rect);
            }
        }

        /// <summary>Says whether a rectangle overlaps or touches any room taken.</summary>
        /// <param name="rect">The rectangle.</param>
        /// <returns><see langword="true"/> when it does.</returns>
        public bool Touches(GraphRect rect) =>
            CellsOf(rect).Any(cell => _cells.TryGetValue(cell, out var list) && list.Any(rect.Intersects));

        /// <summary>Gives the cells a rectangle reaches into.</summary>
        /// <param name="rect">The rectangle.</param>
        /// <returns>The cells.</returns>
        private static IEnumerable<(int Column, int Row)> CellsOf(GraphRect rect)
        {
            var (left, right) = (Index(rect.X), Index(rect.Right));
            var (top, bottom) = (Index(rect.Y), Index(rect.Bottom));
            for (var column = left; column <= right; column++)
            {
                for (var row = top; row <= bottom; row++)
                {
                    yield return (column, row);
                }
            }
        }

        /// <summary>Gives the cell a coordinate falls in.</summary>
        /// <param name="value">The coordinate.</param>
        /// <returns>The cell's index.</returns>
        private static int Index(double value) => (int)Math.Floor(value / Cell);
    }
}
