namespace Chainsaw.App.Figure;

/// <summary>
/// One link of the lock screen's chain, in the logo's units (sketch ID8c, the user's decision 2026-11-07): the joint of
/// the logo, turned upright, and the two straight bars below it. Joints stacked one pitch apart make the chain. The
/// joint is <c>assets/logo/chainsaw-chain-joint.svg</c>; a test keeps the two the same.
/// </summary>
public static class ChainLink
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

    /// <summary>
    /// The link's two straight bars below the joint. They reach two units into the joints above and below, so that the
    /// pieces overlap and no seam shows between them; they are drawn as their own shape, so the overlap never cancels.
    /// </summary>
    public const string Bars = "M-61.5 58H-31.5V102.5H-61.5ZM31.1 58H61.1V102.5H31.1Z";

    /// <summary>The height of one joint.</summary>
    public const double JointHeight = 120;

    /// <summary>The straight link between two joints, so that the link's hole shows (decided 2026-11-07).</summary>
    public const double LinkStraight = 40;

    /// <summary>Where the bars end below the joint's centre, where the chain gives way to the helix.</summary>
    public const double BarsEnd = 100.5;

    /// <summary>Where a bar's centre lies across the link, on either side; the helix's strands start there.</summary>
    public const double BarCenter = 46;

    /// <summary>The width of a bar; the helix's strands keep it.</summary>
    public const double BarWidth = 30;

    /// <summary>Gets the distance from one joint to the next.</summary>
    public static double Pitch => JointHeight + LinkStraight;
}
