using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Chainsaw.App.Controls;

/// <summary>
/// Finds an application resource by its key, so that a view model can name an icon (<c>Icon.Graph</c>) without knowing
/// WPF.
/// </summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    /// <summary>Gets the one instance, used with <c>x:Static</c>.</summary>
    public static ResourceKeyConverter Instance { get; } = new();

    /// <summary>Looks the key up among the application's resources.</summary>
    /// <param name="value">The resource key, a string.</param>
    /// <param name="targetType">Not used.</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>The resource, or <see langword="null"/> when there is no such key.</returns>
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? Application.Current?.TryFindResource(key) : null;

    /// <summary>Not supported: a resource is never turned back into its key.</summary>
    /// <param name="value">Not used.</param>
    /// <param name="targetType">Not used.</param>
    /// <param name="parameter">Not used.</param>
    /// <param name="culture">Not used.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
