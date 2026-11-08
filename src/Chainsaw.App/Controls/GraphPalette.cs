using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// Gives the graph's meanings their brushes from <c>Themes/Colors.xaml</c>: each <see cref="GraphInk"/> and each mark's
/// disc, so that a colour is set in one place.
/// </summary>
public static class GraphPalette
{
    /// <summary>Gives the resource key of an ink's brush.</summary>
    /// <param name="ink">The ink.</param>
    /// <returns>The key.</returns>
    public static string KeyOf(GraphInk ink) => ink switch
    {
        GraphInk.Accent => "Brush.Accent",
        GraphInk.CashOut => "Brush.Kind.CashOut",
        GraphInk.Spent => "Brush.Kind.Spent",
        GraphInk.Risk => "Brush.Kind.Risk",
        GraphInk.Sanctioned => "Brush.Kind.Sanctioned",
        GraphInk.OtherChain => "Brush.Kind.OtherChain",
        GraphInk.Cluster => "Brush.Kind.Cluster",
        GraphInk.Unknown => "Brush.Kind.Unknown",
        GraphInk.StartMark => "Brush.Mark.Start",
        GraphInk.Freeze => "Brush.Mark.Freeze",
        _ => "Brush.Graph.Untraced",
    };

    /// <summary>Gives an ink's brush.</summary>
    /// <param name="owner">The element whose resources are searched.</param>
    /// <param name="ink">The ink.</param>
    /// <returns>The brush.</returns>
    public static Brush Brush(FrameworkElement owner, GraphInk ink) => Find(owner, KeyOf(ink));

    /// <summary>Gives a mark's disc and the brush of the icon on it.</summary>
    /// <param name="owner">The element whose resources are searched.</param>
    /// <param name="badge">The mark.</param>
    /// <returns>The disc's brush and the icon's.</returns>
    public static (Brush Disc, Brush Icon) Mark(FrameworkElement owner, NodeBadge badge)
    {
        var (disc, icon) = MarkKeys(badge);
        return (Find(owner, disc), Find(owner, icon));
    }

    /// <summary>Gives the resource keys of a mark's disc and of the icon on it.</summary>
    /// <param name="badge">The mark.</param>
    /// <returns>The disc's key and the icon's.</returns>
    public static (string Disc, string Icon) MarkKeys(NodeBadge badge) => badge switch
    {
        NodeBadge.CashOutFound => ("Brush.Kind.CashOut", "Brush.Mark.OnCashOut"),
        NodeBadge.Sanctioned => ("Brush.Kind.Sanctioned", "Brush.Mark.OnSanctioned"),
        NodeBadge.StartPoint => ("Brush.Mark.Start", "Brush.Background"),
        NodeBadge.CanBeFrozen => ("Brush.Mark.Freeze", "Brush.Mark.OnFreeze"),
        NodeBadge.Watched => ("Brush.Accent", "Brush.Mark.OnAccent"),
        NodeBadge.Reported => ("Brush.Kind.Risk", "Brush.Mark.OnSanctioned"),
        _ => ("Brush.Mark.OtherCase", "Brush.Text"),
    };

    /// <summary>Finds a brush, or a neutral grey if the theme lacks it, so that drawing never fails.</summary>
    /// <param name="owner">The element whose resources are searched.</param>
    /// <param name="key">The resource key.</param>
    /// <returns>The brush.</returns>
    public static Brush Find(FrameworkElement owner, string key) =>
        owner.TryFindResource(key) as Brush ?? Brushes.Gray;
}

/// <summary>Turns a <see cref="GraphInk"/> into its brush in XAML, for the inspector's and the legend's icons.</summary>
public sealed class GraphInkConverter : IValueConverter
{
    /// <summary>Gets the one instance, for <c>{x:Static}</c>.</summary>
    public static GraphInkConverter Instance { get; } = new();

    /// <summary>Gives the ink's brush from the application's resources.</summary>
    /// <param name="value">The ink.</param>
    /// <param name="targetType">The target's type.</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>The brush, or nothing when the value is not an ink.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is GraphInk ink ? Application.Current?.TryFindResource(GraphPalette.KeyOf(ink)) : null;

    /// <summary>Not supported: an ink is never written back.</summary>
    /// <param name="value">The value.</param>
    /// <param name="targetType">The target's type.</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>Nothing.</returns>
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
