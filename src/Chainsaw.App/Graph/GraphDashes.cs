namespace Chainsaw.App.Graph;

/// <summary>
/// The graph's dashes in one place (sketch GR3a: dashed always means uncertain), so that the surface and the legend
/// behind the <em>?</em> draw them alike. Without WPF, so that the rule is tested.
/// </summary>
public static class GraphDashes
{
    /// <summary>The short dashes of an uncertain line, in pixels on the screen (sketch GR3a: 2 5).</summary>
    private static readonly double[] UncertainPixels = [2, 5];

    /// <summary>The long dashes of a line to another chain, in pixels on the screen (sketch GR3a: 7 5).</summary>
    private static readonly double[] OtherChainPixels = [7, 5];

    /// <summary>
    /// Gets the dashes of a frame that is uncertain or ends the trail, in multiples of the frame's thickness, as WPF
    /// measures them.
    /// </summary>
    public static IReadOnlyList<double> Frame { get; } = [3, 2];

    /// <summary>Gives a line's dashes in pixels on the screen.</summary>
    /// <param name="dash">The pattern.</param>
    /// <returns>The dashes, or <see langword="null"/> for a whole line.</returns>
    public static IReadOnlyList<double>? Pixels(EdgeDash dash) => dash switch
    {
        EdgeDash.Uncertain => UncertainPixels,
        EdgeDash.OtherChain => OtherChainPixels,
        _ => null,
    };

    /// <summary>
    /// Gives a line's dashes in multiples of its thickness, as WPF measures them: the sketch's pattern is in pixels,
    /// so a fixed pattern would grow with the line and a thick line would get long, heavy dashes.
    /// </summary>
    /// <param name="dash">The pattern.</param>
    /// <param name="width">The line's width in pixels.</param>
    /// <returns>The dashes, or <see langword="null"/> for a whole line or no width.</returns>
    public static IReadOnlyList<double>? ForLine(EdgeDash dash, double width) =>
        Pixels(dash) is { } pixels && width > 0 ? [.. pixels.Select(length => length / width)] : null;
}
