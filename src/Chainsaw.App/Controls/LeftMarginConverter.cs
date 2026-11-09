using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Chainsaw.App.Controls;

/// <summary>
/// Turns a distance into a margin on the left only, so that a view model can say how far in something stands (the
/// trash view's tree rows and branch lines) without knowing about WPF's <see cref="Thickness"/>.
/// </summary>
public sealed class LeftMarginConverter : IValueConverter
{
    /// <summary>Gets the one instance, for <c>{x:Static}</c> in XAML.</summary>
    public static LeftMarginConverter Instance { get; } = new();

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double left ? new Thickness(left, 0, 0, 0) : new Thickness(0);

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Thickness margin ? margin.Left : 0d;
}
