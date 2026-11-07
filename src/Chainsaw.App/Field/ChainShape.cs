namespace Chainsaw.App.Field;

/// <summary>
/// The chain of the lock screen in characters (sketch ID7a, the user's decision 2026-11-07): the joint of the logo,
/// turned upright, repeated straight down the left part of the page with 40 units of straight link between the
/// joints, still. The joint is <c>assets/logo/chainsaw-chain-joint.svg</c>; a test keeps the two the same.
/// </summary>
public static class ChainShape
{
    /// <summary>
    /// One joint, in the logo's units around its centre: the band of the logo where the two links meet. At its top
    /// and bottom only the straight sides of a link remain, so joints stacked one pitch apart run together.
    /// </summary>
    public const string Joint =
        "M-61.5 -60.5 -61.5 -55.6 -61.5 -44.8 -61.5 -34 -61.4 -23.8 -61.3 -14.7 -61.2 -7.2 -61 -1.8 -60.8 " +
        "0.8 -60.1 4.1 -59.4 7.2 -58.5 10.2 -57.5 13 -56.4 15.6 -55.1 18.2 -53.7 20.7 -52.1 23.2 -50.3 " +
        "25.6 -48.3 28 -46.1 30.5 -43.7 33 -41.1 35.4 -38.6 37.7 -36.1 39.7 -33.6 41.6 -31 43.2 -28.4 " +
        "44.7 -25.7 46 -22.9 47.1 -20 48.1 -17 48.9 -13.8 49.7 -10.4 50.3 -9 50.4 -7.4 50.6 -5.5 50.6 " +
        "-3.5 50.7 -1.4 50.7 0.8 50.7 3 50.6 5.2 50.5 7.2 50.4 9 50.2 10.7 50 12 49.8 16.2 49 16.2 32.9 " +
        "16.2 29.4 16.2 26.5 16.2 24.1 16.1 22.2 16.1 20.6 16 19.4 15.9 18.5 15.8 17.9 15.7 17.5 15.5 " +
        "17.4 15.3 17.4 15.1 17.5 13.5 18.3 11.7 19 9.7 19.5 7.6 20 5.3 20.4 3 20.7 0.6 20.8 -1.7 20.8 -4 " +
        "20.7 -6.2 20.5 -8.3 20.1 -10.2 19.7 -12.6 18.8 -14.9 17.8 -17.1 16.6 -19.1 15.3 -21 13.8 -22.8 " +
        "12.1 -24.4 10.4 -25.9 8.4 -27.3 6.4 -28.5 4.2 -29.5 1.9 -30.4 -0.6 -30.7 -1.4 -30.9 -2.5 -31.1 " +
        "-3.7 -31.2 -5.4 -31.3 -7.5 -31.4 -10.3 -31.5 -13.8 -31.5 -18.2 -31.6 -23.5 -31.6 -30 -31.6 -37.7 " +
        "-31.6 -46.7 -31.6 -60.5 31.2 -60.5 31.2 -60.3 33.7 -59 36.1 -57.8 38.5 -56.5 40.9 -55 43.3 -53.4 " +
        "45.6 -51.8 47.9 -50.1 50.1 -48.3 52.2 -46.4 54.2 -44.6 56 -42.7 57.7 -40.8 59.3 -38.9 61 -36.7 " +
        "61 -60.5ZM-16.4 -33.5 -16.4 -30.3 -16.4 -27.6 -16.4 -25.3 -16.4 -23.3 -16.3 -21.7 -16.3 -20.5 " +
        "-16.2 -19.5 -16.1 -18.8 -16 -18.4 -15.9 -18.1 -15.7 -18 -15.6 -18.1 -14.1 -18.9 -12.3 -19.5 " +
        "-10.4 -20.1 -8.2 -20.6 -6 -20.9 -3.7 -21.2 -1.4 -21.4 0.9 -21.4 3.1 -21.3 5.3 -21.1 7.3 -20.8 " +
        "9.1 -20.4 11.5 -19.5 13.9 -18.5 16.1 -17.3 18.2 -16 20.1 -14.6 21.9 -13 23.6 -11.3 25.1 -9.5 " +
        "26.5 -7.5 27.7 -5.4 28.8 -3.1 29.7 -0.7 30 0.2 30.2 1.2 30.4 2.3 30.6 3.9 30.7 5.9 30.8 8.5 30.9 " +
        "11.9 31 16.2 31 21.5 31 28 31 35.9 31 45.2 31.1 52 31.1 58.2 31 60.5 -31.7 60.5 -31.7 59.8 -34.9 " +
        "58.2 -37 57.1 -39.4 55.7 -41.9 54 -44.5 52.2 -47.1 50.3 -49.7 48.3 -52.2 46.2 -54.5 44.1 -56.6 " +
        "42.2 -58.4 40.3 -59.8 38.6 -60.8 37.2 -61 37.8 -61.2 40.1 -61.4 44 -61.5 49 -61.6 54.8 -61.6 " +
        "60.5 61 60.5 61 57.4 61 46 61 34.6 60.9 23.8 60.7 14.1 60.5 6 60.2 0 59.8 -3.3 58.6 -8 57 -12.6 " +
        "55.1 -17.1 52.9 -21.3 50.4 -25.3 47.6 -29.1 44.4 -32.7 41 -36 37.4 -39 33.4 -41.8 29.2 -44.3 " +
        "24.8 -46.5 22.1 -47.6 19.3 -48.5 16.4 -49.4 13.3 -50 10.2 -50.6 7 -51 3.8 -51.3 0.6 -51.4 -2.6 " +
        "-51.4 -5.8 -51.2 -9 -50.9 -12.1 -50.4 -16.4 -49.6 -16.4 -33.5Z";

    /// <summary>The straight sides of a link between two joints: its two bars, below the joint.</summary>
    public const string LinkBars = "M-61.5 60H-31.5V100.5H-61.5ZM31.1 60H61.1V100.5H31.1Z";

    /// <summary>The height of one joint, in the logo's units.</summary>
    public const double JointHeight = 120;

    /// <summary>The straight link between two joints, so that the link's hole shows (decided 2026-11-07).</summary>
    public const double LinkStraight = 40;

    /// <summary>The chain's scale: the logo's units to pixels on the page.</summary>
    public const double Scale = 0.9;

    /// <summary>Where the chain's centre line runs, in pixels from the page's left edge.</summary>
    public const double CenterX = 330;

    /// <summary>How wide the strip is that the chain is drawn in, in pixels from the left edge.</summary>
    public const double RegionWidth = 740;

    /// <summary>How much of a cell the chain must cover before the cell shows the chain instead of the field.</summary>
    public const double CoverThreshold = 0.08;

    /// <summary>The joint and the link's bars, filled together.</summary>
    private static readonly PathPolygons Link = PathPolygons.Parse(Joint).Union(PathPolygons.Parse(LinkBars));

    /// <summary>Gets the distance from one joint to the next, in pixels.</summary>
    public static double Pitch => (JointHeight + LinkStraight) * Scale;

    /// <summary>
    /// Tells how much of each cell of the page the chain covers, as the share of nine points spread over the cell.
    /// The chain stands still, so this is worked out once per size of the page.
    /// </summary>
    /// <param name="columns">The number of columns of cells.</param>
    /// <param name="rows">The number of rows of cells.</param>
    /// <param name="cellWidth">The width of a cell, in pixels.</param>
    /// <param name="cellHeight">The height of a cell, in pixels.</param>
    /// <returns>The cover of every cell, row by row, from 0 to 1.</returns>
    public static float[] Cover(int columns, int rows, double cellWidth, double cellHeight)
    {
        var cover = new float[columns * rows];
        var used = Math.Min(columns, (int)Math.Ceiling(RegionWidth / cellWidth));
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < used; column++)
            {
                cover[row * columns + column] = CellCover(column, row, cellWidth, cellHeight);
            }
        }

        return cover;
    }

    /// <summary>Tells whether a point on the page is on the chain.</summary>
    /// <param name="x">The horizontal position, in pixels.</param>
    /// <param name="y">The vertical position, in pixels.</param>
    /// <returns><see langword="true"/> when the chain covers the point.</returns>
    public static bool Contains(double x, double y)
    {
        // The first joint stands two pitches above the page, as in the sketch, so the chain enters from above.
        var first = -2 * Pitch;
        var nearest = (int)Math.Floor((y - first) / Pitch);
        var localX = (x - CenterX) / Scale;
        for (var joint = nearest - 1; joint <= nearest + 1; joint++)
        {
            var localY = (y - (first + joint * Pitch)) / Scale;
            if (Link.Contains(localX, localY))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Gives the share of nine points in a cell that the chain covers.</summary>
    /// <param name="column">The cell's column.</param>
    /// <param name="row">The cell's row.</param>
    /// <param name="cellWidth">The width of a cell.</param>
    /// <param name="cellHeight">The height of a cell.</param>
    /// <returns>The cover, from 0 to 1.</returns>
    private static float CellCover(int column, int row, double cellWidth, double cellHeight)
    {
        var hits = 0;
        for (var j = 0; j < 3; j++)
        {
            for (var i = 0; i < 3; i++)
            {
                // The middle of the pixel the sketch samples, within the strip.
                var x = Math.Min(RegionWidth - 1, Math.Floor((column + (i + 0.5) / 3) * cellWidth)) + 0.5;
                var y = Math.Floor((row + (j + 0.5) / 3) * cellHeight) + 0.5;
                hits += Contains(x, y) ? 1 : 0;
            }
        }

        return hits / 9f;
    }
}
