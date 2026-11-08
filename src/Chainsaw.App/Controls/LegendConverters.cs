using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// Turns dashes from the graph's tables (<see cref="GraphDashes"/>) into a shape's <c>StrokeDashArray</c>, so that the
/// legend's frames and lines are dashed as the graph's are.
/// </summary>
public sealed class DashArrayConverter : IValueConverter
{
    /// <summary>Gets the one instance, for <c>{x:Static}</c>.</summary>
    public static DashArrayConverter Instance { get; } = new();

    /// <summary>Gives the dashes as WPF takes them.</summary>
    /// <param name="value">The dashes, in multiples of the stroke's thickness, or nothing for a whole stroke.</param>
    /// <param name="targetType">The target's type.</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>The dashes, or nothing for a whole stroke.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is IEnumerable<double> dashes ? new DoubleCollection(dashes) : null;

    /// <summary>Not supported: dashes are never written back.</summary>
    /// <param name="value">The value.</param>
    /// <param name="targetType">The target's type.</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>Nothing.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Turns a mark into the brush of its disc, or with the parameter <c>Icon</c> of the icon on it, from the graph's own
/// table (<see cref="GraphPalette.MarkKeys"/>), so that the legend's marks look like the graph's.
/// </summary>
public sealed class MarkBrushConverter : IValueConverter
{
    /// <summary>The parameter that asks for the icon's brush rather than the disc's.</summary>
    public const string IconPart = "Icon";

    /// <summary>Gets the one instance, for <c>{x:Static}</c>.</summary>
    public static MarkBrushConverter Instance { get; } = new();

    /// <summary>Gives the mark's disc or icon brush from the application's resources.</summary>
    /// <param name="value">The mark.</param>
    /// <param name="targetType">The target's type.</param>
    /// <param name="parameter"><c>Icon</c> for the icon's brush; anything else gives the disc's.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>The brush, or nothing when the value is not a mark.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not NodeBadge badge)
        {
            return null;
        }

        var (disc, icon) = GraphPalette.MarkKeys(badge);
        var key = IconPart.Equals(parameter as string, StringComparison.Ordinal) ? icon : disc;
        return Application.Current?.TryFindResource(key) as Brush;
    }

    /// <summary>Not supported: a brush is never written back.</summary>
    /// <param name="value">The value.</param>
    /// <param name="targetType">The target's type.</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>Nothing.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
