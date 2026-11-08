using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// Gives a <see cref="GraphInk"/> its brush from <c>Themes/Colors.xaml</c> in a binding, so that a view draws a sort
/// in the graph's colour without the view model knowing WPF (the report's chips, sketch RP1b).
/// </summary>
public sealed class InkBrushConverter : IValueConverter
{
    /// <summary>Gets the one instance, used with <c>x:Static</c>.</summary>
    public static InkBrushConverter Instance { get; } = new();

    /// <summary>Finds the ink's brush among the application's resources.</summary>
    /// <param name="value">The ink.</param>
    /// <param name="targetType">Not used.</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>The brush, or <see langword="null"/> for anything that is no ink.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is GraphInk ink ? Application.Current?.TryFindResource(GraphPalette.KeyOf(ink)) : null;

    /// <summary>Not supported: a brush is never turned back into its ink.</summary>
    /// <param name="value">Not used.</param>
    /// <param name="targetType">Not used.</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
