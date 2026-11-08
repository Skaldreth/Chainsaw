using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Views;

/// <summary>
/// The trash in the lower left corner (sketch SO1a). It only draws what its <see cref="TrashBinViewModel"/> says: the
/// size, whether it is hot, the ring wave and the dot. A click opens the trash's view.
/// </summary>
public sealed partial class TrashBin : UserControl
{
    /// <summary>
    /// The bin's centre in the corner, from the left and from the bottom: 18 px from the edges like the pill, plus the
    /// rest radius.
    /// </summary>
    public const double CentreInset = 18 + TrashBinViewModel.RestRadius;

    /// <summary>How much larger than the area the ring wave starts.</summary>
    private const double WaveExtra = 30;

    /// <summary>How long one ring wave takes.</summary>
    private static readonly Duration WaveTime = new(TimeSpan.FromMilliseconds(1300));

    /// <summary>The model, once given.</summary>
    private TrashBinViewModel? _model;

    /// <summary>Whether the ring wave goes.</summary>
    private bool _waving;

    /// <summary>Creates the bin.</summary>
    public TrashBin()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as TrashBinViewModel);
    }

    /// <summary>Raised when the bin is clicked, so that the trash's view opens.</summary>
    public event EventHandler? Opened;

    /// <summary>Gives the bin's centre in another element's coordinates, so that a drag can say how near it is.</summary>
    /// <param name="relativeTo">The element.</param>
    /// <returns>The centre.</returns>
    public Point CentreIn(UIElement relativeTo) =>
        TranslatePoint(new Point(CentreInset, ActualHeight - CentreInset), relativeTo);

    /// <summary>Gives the distance from a point in another element to the bin's centre.</summary>
    /// <param name="point">The point.</param>
    /// <param name="relativeTo">The element the point is in.</param>
    /// <returns>The distance, in pixels.</returns>
    public double DistanceFrom(Point point, UIElement relativeTo) => (point - CentreIn(relativeTo)).Length;

    /// <summary>Follows a model.</summary>
    /// <param name="model">The model, or <see langword="null"/>.</param>
    private void Attach(TrashBinViewModel? model)
    {
        if (_model is not null)
        {
            _model.PropertyChanged -= OnModelChanged;
        }

        _model = model;
        if (model is not null)
        {
            model.PropertyChanged += OnModelChanged;
        }

        Place();
    }

    /// <summary>Draws again when the model changes.</summary>
    /// <param name="sender">The model.</param>
    /// <param name="e">What changed.</param>
    private void OnModelChanged(object? sender, PropertyChangedEventArgs e) => Place();

    /// <summary>Places and colours the area, the icon, the dot and the ring wave by the model.</summary>
    private void Place()
    {
        var model = _model ?? new TrashBinViewModel();
        var radius = model.Radius;
        var centre = new Point(CentreInset, Height - CentreInset);
        Body.Width = Body.Height = 2 * radius;
        Body.CornerRadius = new CornerRadius(radius);
        Canvas.SetLeft(Body, centre.X - radius);
        Canvas.SetTop(Body, centre.Y - radius);

        var active = model.IsGrowing;
        Body.Background = Brush(model.IsHot ? "Brush.SurfaceSelected" : Ground(model));
        Body.BorderBrush = Brush(model.IsHot ? "Brush.Text" : "Brush.BorderStrong");
        Body.BorderThickness = new Thickness(EdgeOf(model));
        Dashed.Visibility = active && !model.IsHot ? Visibility.Visible : Visibility.Collapsed;
        Icon.Size = model.IconSize;
        Icon.Foreground = Brush(active || model.IsViewOpen ? "Brush.Text" : "Brush.TextMuted");
        DropWord.Visibility = model.IsHot ? Visibility.Visible : Visibility.Collapsed;

        Dot.Visibility = model.HasThrows && !active && !model.IsViewOpen ? Visibility.Visible : Visibility.Collapsed;
        Canvas.SetLeft(Dot, centre.X + TrashBinViewModel.RestRadius - Dot.Width - 3);
        Canvas.SetTop(Dot, centre.Y - TrashBinViewModel.RestRadius + 3);

        foreach (var wave in new[] { WaveOne, WaveTwo })
        {
            wave.Width = wave.Height = (2 * radius) + WaveExtra;
            Canvas.SetLeft(wave, centre.X - (wave.Width / 2));
            Canvas.SetTop(wave, centre.Y - (wave.Height / 2));
        }

        Wave(model.IsHot);
    }

    /// <summary>
    /// Gives the area's edge: bright when hot, none while it grows (the dashed ring stands there), a hairline at rest.
    /// </summary>
    /// <param name="model">The model.</param>
    /// <returns>The edge's thickness.</returns>
    private static double EdgeOf(TrashBinViewModel model)
    {
        if (model.IsHot)
        {
            return 1.5;
        }

        return model.IsGrowing ? 0 : 1;
    }

    /// <summary>Gives the area's ground at rest or while it grows.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The brush's key.</returns>
    private static string Ground(TrashBinViewModel model)
    {
        if (model.IsGrowing)
        {
            return "Brush.Surface";
        }

        return model.IsViewOpen ? "Brush.SurfaceSelected" : "Brush.SurfaceRaised";
    }

    /// <summary>
    /// Starts or stops the ring wave: two rings that grow out and fade, half a beat apart. With animations off in
    /// Windows a faint still ring stands instead.
    /// </summary>
    /// <param name="on">Whether it goes.</param>
    private void Wave(bool on)
    {
        if (on == _waving)
        {
            return;
        }

        _waving = on;
        var waves = new[] { (WaveOne, WaveOneScale, TimeSpan.Zero), (WaveTwo, WaveTwoScale, WaveTime.TimeSpan / 2) };
        foreach (var (wave, scale, delay) in waves)
        {
            wave.BeginAnimation(OpacityProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            wave.Opacity = 0;
            if (!on)
            {
                continue;
            }

            if (!SystemParameters.ClientAreaAnimation)
            {
                wave.Opacity = delay == TimeSpan.Zero ? 0.22 : 0;
                continue;
            }

            var grow = new DoubleAnimation(0.55, 1.45, WaveTime)
            {
                BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut },
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            wave.BeginAnimation(
                OpacityProperty,
                new DoubleAnimation(0.6, 0, WaveTime) { BeginTime = delay, RepeatBehavior = RepeatBehavior.Forever });
        }
    }

    /// <summary>Opens the trash's view.</summary>
    /// <param name="sender">The area.</param>
    /// <param name="e">The click.</param>
    private void OnBodyClicked(object sender, MouseButtonEventArgs e)
    {
        Opened?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    /// <summary>Finds a brush of the theme.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The brush.</returns>
    private Brush Brush(string key) => (Brush)FindResource(key);
}
