using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Chainsaw.App.Controls;

/// <summary>Shows an element when a value is <see langword="false"/> and collapses it when it is true.</summary>
public sealed class InvertVisibility : IValueConverter
{
    /// <summary>Gets the one instance, for <c>{x:Static}</c> in XAML.</summary>
    public static InvertVisibility Instance { get; } = new();

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is Visibility.Collapsed;
}
