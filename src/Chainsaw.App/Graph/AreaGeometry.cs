namespace Chainsaw.App.Graph;

/// <summary>What on an area's chip is under the pointer.</summary>
public enum AreaPart
{
    /// <summary>The chip itself: chooses the area.</summary>
    Chip,

    /// <summary>The pen beside a chosen own area's chip: renames it.</summary>
    Rename,

    /// <summary>The bin beside a chosen own area's chip: removes it.</summary>
    Remove,
}

/// <summary>Where an area is drawn: its frame around its actors and its chip on the edge.</summary>
/// <param name="Area">The area.</param>
/// <param name="Frame">The rounded frame.</param>
/// <param name="Chip">The chip with the icon, at the top right corner.</param>
public sealed record AreaPlace(AreaView Area, GraphRect Frame, GraphRect Chip)
{
    /// <summary>Gets the pen beside the chip, shown when an own area is chosen.</summary>
    public GraphRect RenameButton => new(Chip.Right + AreaGeometry.ToolGap, Chip.Y, Chip.Height, Chip.Height);

    /// <summary>Gets the bin beside the pen.</summary>
    public GraphRect RemoveButton =>
        new(RenameButton.Right + AreaGeometry.ToolGap, Chip.Y, Chip.Height, Chip.Height);
}

/// <summary>
/// Works out where the areas are drawn behind the actors (sketch KF1b) and what on them is under the pointer, without
/// WPF, so that it is tested.
/// </summary>
public static class AreaGeometry
{
    /// <summary>The space between an area's edge and its actors.</summary>
    public const double Padding = 14;

    /// <summary>The space of an area that lies inside another's fill, drawn as an edge close to its actors.</summary>
    public const double InnerPadding = 7;

    /// <summary>The space of an own area, which lies outside the others.</summary>
    public const double OwnPadding = 24;

    /// <summary>The space of a time area, the outermost.</summary>
    public const double TimePadding = 30;

    /// <summary>The corner radius of an area.</summary>
    public const double Radius = 16;

    /// <summary>The chip's height.</summary>
    public const double ChipHeight = 20;

    /// <summary>The chip's width with its icon.</summary>
    public const double ChipWidth = 24;

    /// <summary>What an own area's person mark adds to the chip.</summary>
    public const double PersonWidth = 12;

    /// <summary>The chip's distance in from the frame's right edge.</summary>
    public const double ChipInset = 14;

    /// <summary>The space between the chip and its tools.</summary>
    public const double ToolGap = 3;

    /// <summary>Places the areas around their actors' places; an area with no placed actor is left out.</summary>
    /// <param name="areas">The areas.</param>
    /// <param name="places">The actors' places (top-left corners).</param>
    /// <param name="frameOf">
    /// Each actor's frame at its place, as wide as it is drawn (sketch GR5); <see langword="null"/> for the graph's
    /// width.
    /// </param>
    /// <returns>The places, in the areas' order.</returns>
    public static IReadOnlyList<AreaPlace> Place(
        IReadOnlyList<AreaView> areas,
        IReadOnlyDictionary<string, GraphPoint> places,
        Func<string, GraphPoint, GraphRect>? frameOf = null)
    {
        ArgumentNullException.ThrowIfNull(areas);
        ArgumentNullException.ThrowIfNull(places);
        frameOf ??= (_, place) => GraphSizes.NodeFrame(place);
        var result = new List<AreaPlace>();
        foreach (var area in areas)
        {
            var frames = area.Nodes
                .Where(places.ContainsKey)
                .Select(node => frameOf(node, places[node]))
                .ToList();
            if (frames.Count == 0)
            {
                continue;
            }

            var frame = frames.Skip(1).Aggregate(frames[0], (all, one) => all.Union(one)).Inflate(PaddingOf(area));
            var width = ChipWidth + (area.IsOwn ? PersonWidth : 0);
            var chip = new GraphRect(
                frame.Right - ChipInset - width, frame.Y - (ChipHeight / 2), width, ChipHeight);
            result.Add(new AreaPlace(area, frame, chip));
        }

        return result;
    }

    /// <summary>Gives the space between an area and its actors.</summary>
    /// <param name="area">The area.</param>
    /// <returns>The padding.</returns>
    public static double PaddingOf(AreaView area)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (area.Line == AreaLine.Dotted)
        {
            return TimePadding;
        }

        if (area.IsOwn)
        {
            return OwnPadding;
        }

        return area.IsFilled ? Padding : InnerPadding;
    }

    /// <summary>
    /// Gives the area chip, or a chosen own area's tool, under a point; the last drawn wins, as it lies on top.
    /// </summary>
    /// <param name="placed">The placed areas.</param>
    /// <param name="point">The point on the surface.</param>
    /// <param name="chosen">The chosen area's key, whose tools are shown when it is an own area.</param>
    /// <returns>The area and the part, or <see langword="null"/>.</returns>
    public static (AreaView Area, AreaPart Part)? HitTest(
        IReadOnlyList<AreaPlace> placed, GraphPoint point, string? chosen)
    {
        ArgumentNullException.ThrowIfNull(placed);
        for (var i = placed.Count - 1; i >= 0; i--)
        {
            var place = placed[i];
            if (place.Area.IsOwn && place.Area.Key == chosen)
            {
                if (place.RenameButton.Contains(point))
                {
                    return (place.Area, AreaPart.Rename);
                }

                if (place.RemoveButton.Contains(point))
                {
                    return (place.Area, AreaPart.Remove);
                }
            }

            if (place.Chip.Contains(point))
            {
                return (place.Area, AreaPart.Chip);
            }
        }

        return null;
    }
}
