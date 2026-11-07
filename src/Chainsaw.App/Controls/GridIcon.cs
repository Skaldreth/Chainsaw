using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Chainsaw.App.Controls;

/// <summary>
/// An icon from <c>Themes/Icons.xaml</c>, drawn on its 24 x 24 grid and scaled to <see cref="Size"/>. The line is
/// always 1.5 px on screen whatever the size, because a fixed line would be scaled with the icon, land between pixels
/// and look blurred (the rule from Gawk).
/// </summary>
public sealed class GridIcon : Control
{
    /// <summary>The width of the icons' grid.</summary>
    public const double GridSize = 24;

    /// <summary>The line's width on screen, in pixels.</summary>
    public const double ScreenStroke = 1.5;

    /// <summary>Identifies <see cref="Data"/>.</summary>
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(GridIcon), new FrameworkPropertyMetadata(null));

    /// <summary>Identifies <see cref="Size"/>.</summary>
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size), typeof(double), typeof(GridIcon),
        new FrameworkPropertyMetadata(20d, FrameworkPropertyMetadataOptions.AffectsMeasure, OnSizeChanged));

    /// <summary>Identifies <see cref="Stroke"/>, the line's width on the grid.</summary>
    private static readonly DependencyPropertyKey StrokePropertyKey = DependencyProperty.RegisterReadOnly(
        nameof(Stroke), typeof(double), typeof(GridIcon), new FrameworkPropertyMetadata(StrokeFor(20d)));

    /// <summary>Identifies <see cref="Stroke"/>.</summary>
    public static readonly DependencyProperty StrokeProperty = StrokePropertyKey.DependencyProperty;

    /// <summary>Sets the default style from <c>Themes/Controls.xaml</c>.</summary>
    static GridIcon() =>
        DefaultStyleKeyProperty.OverrideMetadata(typeof(GridIcon), new FrameworkPropertyMetadata(typeof(GridIcon)));

    /// <summary>Gets or sets the icon's shape, a geometry on the 24 x 24 grid.</summary>
    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>Gets or sets the icon's size on screen in pixels: 20 in the rail, 16 in buttons, at least 12.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    /// <summary>Gets the line's width on the grid that gives <see cref="ScreenStroke"/> on screen.</summary>
    public double Stroke => (double)GetValue(StrokeProperty);

    /// <summary>Works out the line's width on the grid for a size on screen: 1.8 at 20 px, 2.25 at 16 px.</summary>
    /// <param name="size">The icon's size on screen.</param>
    /// <returns>The width to draw with on the 24 x 24 grid.</returns>
    public static double StrokeFor(double size) => ScreenStroke * GridSize / Math.Max(size, 1);

    /// <summary>Keeps the line's width in step with the size.</summary>
    /// <param name="d">The icon.</param>
    /// <param name="e">The new size.</param>
    private static void OnSizeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        d.SetValue(StrokePropertyKey, StrokeFor((double)e.NewValue));
}
