using System.Globalization;
using System.Windows.Data;

namespace Chainsaw.App.Controls;

/// <summary>Turns <see langword="true"/> into <see langword="false"/> and back, such as busy into enabled.</summary>
public sealed class InvertConverter : IValueConverter
{
    /// <summary>Gets the one instance, for <c>{x:Static}</c> in XAML.</summary>
    public static InvertConverter Instance { get; } = new();

    /// <inheritdoc />
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;

    /// <inheritdoc />
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
