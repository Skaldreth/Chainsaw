using System.Globalization;

namespace Chainsaw.App.Navigation;

/// <summary>
/// The outline of an open submenu (sketch PN1): the button's round grows into a tab past the pill's edge and runs into
/// the menu, which hangs from the tab's top edge; a rounded inner corner joins them below the tab. One outline, so that
/// the surface, the edge and the shadow go round both as one shape. No WPF: the outline is path data that the view
/// parses, so that it is tested in Chainsaw.App.Tests.
/// </summary>
public static class SubmenuShape
{
    /// <summary>The height of the tab: the pill's button.</summary>
    public const double TabHeight = 38;

    /// <summary>
    /// The width of the tab, from the button's left edge to 6 px past the pill's right edge (the button sits 7 px in,
    /// the pill is 52 px wide).
    /// </summary>
    public const double TabWidth = 51;

    /// <summary>The width of the menu.</summary>
    public const double MenuWidth = 230;

    /// <summary>The space inside the menu around its rows.</summary>
    public const double Padding = 4;

    /// <summary>The height of a row.</summary>
    public const double RowHeight = 30;

    /// <summary>The height of a line between groups of rows, with its space above and below.</summary>
    public const double LineHeight = 9;

    /// <summary>The radius of the menu's corners and of the inner corner below the tab.</summary>
    public const double CornerRadius = 8;

    /// <summary>Gets the height of a menu with these rows.</summary>
    /// <param name="rows">The rows.</param>
    /// <returns>The rows' heights and the padding above and below.</returns>
    public static double MenuHeight(IEnumerable<SubmenuRow> rows) =>
        (2 * Padding) + rows.Sum(row => row.IsLine ? LineHeight : RowHeight);

    /// <summary>Gets the whole shape's width: the tab and the menu.</summary>
    public static double Width => TabWidth + MenuWidth;

    /// <summary>
    /// Gives the outline as path data: from the top of the tab's round along the top edge to the menu's top right
    /// corner, down and round the bottom, up the menu's left side to the inner corner, along the tab's bottom and
    /// round the button's half circle back to the start.
    /// </summary>
    /// <param name="menuHeight">The menu's height; at least the tab's and the corners.</param>
    /// <returns>The path data, in the shape's own coordinates with (0, 0) at the button's top left.</returns>
    public static string Outline(double menuHeight)
    {
        var h = Math.Max(menuHeight, TabHeight + (2 * CornerRadius));
        const double r = CornerRadius;
        const double w = TabWidth;
        const double t = TabHeight;
        const double round = TabHeight / 2;
        var x = Width;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"M{round},0 H{x - r} Q{x},0 {x},{r} V{h - r} Q{x},{h} {x - r},{h} H{w + r} Q{w},{h} {w},{h - r} " +
            $"V{t + r} Q{w},{t} {w - r},{t} H{round} A{round},{round} 0 0 1 {round},0 Z");
    }
}
