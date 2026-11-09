using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;
using Chainsaw.App.Graph;
using static Chainsaw.App.Controls.Freezing;

namespace Chainsaw.App.Controls;

/// <summary>
/// Fans a pile out over a surface, the same way in the graph and on the start page (sketches EX1, EX2b and GR8a): the
/// surface is sent out in a circular shock from the pile, the pile glides to the middle, a backdrop dims everything
/// else, and the pile's cards spring out around it, laid out as the graph's piles are
/// (<see cref="PileFanLayout"/>), with faint spokes to them.
/// Folding it again runs the other way. It only places and moves; what the cards are, and what a click or a drag on
/// them does, belongs to whoever opens it. Everything stands still when Windows has animations turned off.
/// </summary>
public sealed class FanOutLayer : Canvas
{
    /// <summary>Identifies <see cref="BackdropBrush"/>.</summary>
    public static readonly DependencyProperty BackdropBrushProperty = DependencyProperty.Register(
        nameof(BackdropBrush), typeof(Brush), typeof(FanOutLayer), new PropertyMetadata(Brushes.Black));

    /// <summary>Identifies <see cref="LineBrush"/>.</summary>
    public static readonly DependencyProperty LineBrushProperty = DependencyProperty.Register(
        nameof(LineBrush), typeof(Brush), typeof(FanOutLayer), new PropertyMetadata(Brushes.Gray));

    /// <summary>How long the shock's rings take to grow out and fade, as in the graph (<see cref="FanShock"/>).</summary>
    private static readonly Duration ShockTime = new(FanShock.RingTime);

    /// <summary>How long the pile takes to glide to the middle, and the surface to be pushed out.</summary>
    private static readonly Duration GlideTime = new(FanShock.GlideTime);

    /// <summary>How long the backdrop takes to dim the surface.</summary>
    private static readonly Duration DimTime = new(TimeSpan.FromMilliseconds(200));

    /// <summary>When the first card leaves the pile, once the pile is nearly in the middle.</summary>
    private static readonly TimeSpan CardsStart = TimeSpan.FromMilliseconds(260);

    /// <summary>How much later each card leaves than the one before it.</summary>
    private static readonly TimeSpan CardStagger = TimeSpan.FromMilliseconds(22);

    /// <summary>How long a card takes to spring out to its place.</summary>
    private static readonly Duration CardTime = new(TimeSpan.FromMilliseconds(420));

    /// <summary>How long folding takes.</summary>
    private static readonly Duration FoldTime = new(TimeSpan.FromMilliseconds(240));

    /// <summary>The ease the pile glides with and the surface is pushed with.</summary>
    private static readonly IEasingFunction Glide = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });

    /// <summary>The ease the shock's rings grow with: fast out, slowing as they fade.</summary>
    private static readonly IEasingFunction Shock = Frozen(new CubicEase { EasingMode = EasingMode.EaseOut });

    /// <summary>The ease a card springs out with: a little past its place and back.</summary>
    private static readonly IEasingFunction Spring =
        Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 });

    /// <summary>How far the surface is pushed out from the pile: a little, a shock and not a zoom.</summary>
    private const double PushScale = FanShock.PushScale;

    /// <summary>How small a card is as it leaves the pile.</summary>
    private const double CardStartScale = 0.6;

    /// <summary>The line width of the shock's rings.</summary>
    private const double LineWidth = 1.5;

    /// <summary>The line width of the spokes, thin as the graph's.</summary>
    private const double SpokeWidth = 1;

    /// <summary>The room kept free under the rings, for a line of help under them.</summary>
    private const double FootRoom = 44;

    /// <summary>The dim backdrop over the surface, which a click on folds the pile.</summary>
    private readonly Rectangle _backdrop = new();

    /// <summary>The faint spokes from the pile to its cards.</summary>
    private readonly Path _spokes = new() { IsHitTestVisible = false };

    /// <summary>The cards, each with the place its centre stands at.</summary>
    private readonly List<(FrameworkElement Card, Point Slot)> _cards = [];

    /// <summary>The pile in the middle, or <see langword="null"/> while nothing is fanned out.</summary>
    private FrameworkElement? _centre;

    /// <summary>The surface that was pushed out, and the transform it had before.</summary>
    private (UIElement Surface, Transform Before)? _pushed;

    /// <summary>Whether the mouse went down on the backdrop, so that only a whole click there folds the pile.</summary>
    private bool _backdropPressed;

    /// <summary>Creates the layer, empty and hidden.</summary>
    public FanOutLayer()
    {
        Visibility = Visibility.Collapsed;
        ClipToBounds = true;
        Focusable = true;
        FocusVisualStyle = null;
        _spokes.StrokeThickness = SpokeWidth;
        _backdrop.MouseLeftButtonDown += (_, _) => _backdropPressed = true;
        _backdrop.MouseLeftButtonUp += (_, e) =>
        {
            if (_backdropPressed)
            {
                _backdropPressed = false;
                BackdropClicked?.Invoke(this, e);
            }
        };
        SizeChanged += (_, _) => FitBackdrop();
    }

    /// <summary>Raised when the backdrop is clicked: usually the pile is folded.</summary>
    public event EventHandler<MouseButtonEventArgs>? BackdropClicked;

    /// <summary>Gets or sets the backdrop's brush, which dims the surface.</summary>
    public Brush BackdropBrush
    {
        get => (Brush)GetValue(BackdropBrushProperty);
        set => SetValue(BackdropBrushProperty, value);
    }

    /// <summary>Gets or sets the colour of the shock's rings and the spokes.</summary>
    public Brush LineBrush
    {
        get => (Brush)GetValue(LineBrushProperty);
        set => SetValue(LineBrushProperty, value);
    }

    /// <summary>Gets whether a pile is fanned out.</summary>
    public bool IsOpen => _centre is not null;

    /// <summary>Gets the pile's bounds in the middle, where a card dropped goes back into the pile.</summary>
    public Rect CentreBounds => _centre is null
        ? Rect.Empty
        : new Rect(GetLeft(_centre), GetTop(_centre), _centre.Width, _centre.Height);

    /// <summary>Whether Windows lets things move: everything stands still when animations are turned off.</summary>
    private static bool Animates => SystemParameters.ClientAreaAnimation;

    /// <summary>Fans a pile out.</summary>
    /// <param name="surface">What the pile lies on, which is pushed out and dimmed.</param>
    /// <param name="origin">The pile's centre where it lies, in the layer's coordinates.</param>
    /// <param name="centre">The pile as it stands in the middle; it must have a width and a height.</param>
    /// <param name="cards">The cards; each must have a width and a height.</param>
    public void Open(UIElement surface, Point origin, FrameworkElement centre, IReadOnlyList<FrameworkElement> cards)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(centre);
        ArgumentNullException.ThrowIfNull(cards);
        Clear();
        Visibility = Visibility.Visible;
        IsHitTestVisible = true;
        var middle = new Point(ActualWidth / 2, (ActualHeight - FootRoom) / 2);
        Push(surface, origin);
        Dim();
        Shake(origin);
        Centre(centre, origin, middle);
        Deal(cards, middle);
        Focus();
    }

    /// <summary>Folds the pile: the cards go back into it, the backdrop lifts and the surface comes back.</summary>
    /// <param name="closed">What to do once it is folded, or <see langword="null"/>.</param>
    public void Close(Action? closed = null)
    {
        if (!IsOpen)
        {
            closed?.Invoke();
            return;
        }

        IsHitTestVisible = false;
        if (!Animates)
        {
            Finish(closed);
            return;
        }

        var middle = Middle();
        foreach (var (card, slot) in _cards)
        {
            var move = Moves(card);
            move.Translate.BeginAnimation(TranslateTransform.XProperty, To(middle.X - slot.X, FoldTime, Glide));
            move.Translate.BeginAnimation(TranslateTransform.YProperty, To(middle.Y - slot.Y, FoldTime, Glide));
            card.BeginAnimation(OpacityProperty, To(0, FoldTime, null));
        }

        _spokes.BeginAnimation(OpacityProperty, To(0, FoldTime, null));
        _centre!.BeginAnimation(OpacityProperty, To(0, FoldTime, null));
        if (_pushed is { } pushed && pushed.Surface.RenderTransform is ScaleTransform push)
        {
            push.BeginAnimation(ScaleTransform.ScaleXProperty, To(1, FoldTime, Glide));
            push.BeginAnimation(ScaleTransform.ScaleYProperty, To(1, FoldTime, Glide));
        }

        var lift = To(0, FoldTime, null);
        lift.Completed += (_, _) => Finish(closed);
        _backdrop.BeginAnimation(OpacityProperty, lift);
    }

    /// <summary>Gives the place a card's centre stands at in the ring.</summary>
    /// <param name="card">The card.</param>
    /// <returns>The place, or the layer's middle for a card it does not hold.</returns>
    public Point SlotOf(FrameworkElement card) =>
        _cards.Find(each => ReferenceEquals(each.Card, card)) is { Card: not null } found ? found.Slot : Middle();

    /// <summary>Moves a dragged card so that its centre stands at a point, lifted above the others.</summary>
    /// <param name="card">The card.</param>
    /// <param name="centre">Where its centre goes, in the layer's coordinates.</param>
    public void Move(FrameworkElement card, Point centre)
    {
        ArgumentNullException.ThrowIfNull(card);
        SetLeft(card, centre.X - (card.Width / 2));
        SetTop(card, centre.Y - (card.Height / 2));
        SetZIndex(card, 2);
    }

    /// <summary>Lets a dragged card glide back to its place in the ring.</summary>
    /// <param name="card">The card.</param>
    public void Return(FrameworkElement card)
    {
        ArgumentNullException.ThrowIfNull(card);
        var slot = SlotOf(card);
        var from = new Point(GetLeft(card) + (card.Width / 2), GetTop(card) + (card.Height / 2));
        Place(card, slot);
        SetZIndex(card, 1);
        if (!Animates)
        {
            return;
        }

        var move = Moves(card);
        move.Translate.BeginAnimation(TranslateTransform.XProperty, FromTo(from.X - slot.X, 0, FoldTime, Glide));
        move.Translate.BeginAnimation(TranslateTransform.YProperty, FromTo(from.Y - slot.Y, 0, FoldTime, Glide));
    }

    /// <summary>Pushes the surface out from the pile, a little, as the shock passes.</summary>
    /// <param name="surface">The surface.</param>
    /// <param name="origin">The pile's centre, in the layer's coordinates.</param>
    private void Push(UIElement surface, Point origin)
    {
        var at = TranslatePoint(origin, surface);
        var push = new ScaleTransform(1, 1, at.X, at.Y);
        _pushed = (surface, surface.RenderTransform);
        surface.RenderTransform = push;
        if (!Animates)
        {
            push.ScaleX = PushScale;
            push.ScaleY = PushScale;
            return;
        }

        push.BeginAnimation(ScaleTransform.ScaleXProperty, To(PushScale, GlideTime, Shock));
        push.BeginAnimation(ScaleTransform.ScaleYProperty, To(PushScale, GlideTime, Shock));
    }

    /// <summary>Lays the backdrop over the surface and dims it.</summary>
    private void Dim()
    {
        _backdrop.Fill = BackdropBrush;
        FitBackdrop();
        Children.Add(_backdrop);
        if (Animates)
        {
            _backdrop.BeginAnimation(OpacityProperty, FromTo(0, 1, DimTime, null));
        }
    }

    /// <summary>Sends two rings out from the pile, the circular shock; none when nothing moves.</summary>
    /// <param name="origin">The pile's centre.</param>
    private void Shake(Point origin)
    {
        if (!Animates)
        {
            return;
        }

        for (var index = 0; index < FanShock.RingCount; index++)
        {
            var delay = FanShock.Delay(index);
            var circle = new EllipseGeometry(origin, FanShock.StartRadius, FanShock.StartRadius);
            var ring = new Path
            {
                Data = circle, Stroke = LineBrush, StrokeThickness = LineWidth, Opacity = 0, IsHitTestVisible = false,
            };
            Children.Add(ring);
            circle.BeginAnimation(EllipseGeometry.RadiusXProperty, Grow(delay));
            circle.BeginAnimation(EllipseGeometry.RadiusYProperty, Grow(delay));
            var fade = FromTo(FanShock.RingOpacity, 0, ShockTime, null);
            fade.BeginTime = delay;
            fade.Completed += (_, _) => Children.Remove(ring);
            ring.BeginAnimation(OpacityProperty, fade);
        }
    }

    /// <summary>Puts the pile in the middle, gliding there from where it lay.</summary>
    /// <param name="centre">The pile.</param>
    /// <param name="origin">Where it lay.</param>
    /// <param name="middle">The middle.</param>
    private void Centre(FrameworkElement centre, Point origin, Point middle)
    {
        _centre = centre;
        Place(centre, middle);
        SetZIndex(centre, 1);
        Children.Add(centre);
        if (!Animates)
        {
            return;
        }

        var glide = new TranslateTransform();
        centre.RenderTransform = glide;
        glide.BeginAnimation(TranslateTransform.XProperty, FromTo(origin.X - middle.X, 0, GlideTime, Glide));
        glide.BeginAnimation(TranslateTransform.YProperty, FromTo(origin.Y - middle.Y, 0, GlideTime, Glide));
    }

    /// <summary>
    /// Deals the cards out on the graph's rings around the middle (<see cref="PileFanLayout.Ring"/>, sketch EX2b), drawn
    /// in a little when the layer is too small for them, one after the other, with spokes to them.
    /// </summary>
    /// <param name="cards">The cards.</param>
    /// <param name="middle">The middle.</param>
    private void Deal(IReadOnlyList<FrameworkElement> cards, Point middle)
    {
        var slots = PileFanLayout.FitRing(
            PileFanLayout.Ring(cards.Count), ActualWidth, ActualHeight - FootRoom);
        var spokes = new GeometryGroup();
        _spokes.Stroke = LineBrush;
        _spokes.Data = spokes;
        Children.Add(_spokes);
        for (var index = 0; index < cards.Count; index++)
        {
            var slot = new Point(middle.X + slots[index].X, middle.Y + slots[index].Y);
            spokes.Children.Add(new LineGeometry(middle, slot));
            _cards.Add((cards[index], slot));
            Place(cards[index], slot);
            SetZIndex(cards[index], 1);
            Children.Add(cards[index]);
            SpringOut(cards[index], slot, middle, CardsStart + (CardStagger * index));
        }

        if (Animates)
        {
            var show = FromTo(0, 1, CardTime, null);
            show.BeginTime = CardsStart;
            _spokes.BeginAnimation(OpacityProperty, show);
        }
    }

    /// <summary>Lets a card spring out of the pile to its place.</summary>
    /// <param name="card">The card.</param>
    /// <param name="slot">Its place.</param>
    /// <param name="middle">The pile's centre, which it leaves from.</param>
    /// <param name="begin">When it leaves.</param>
    private static void SpringOut(FrameworkElement card, Point slot, Point middle, TimeSpan begin)
    {
        var move = Moves(card);
        if (!Animates)
        {
            return;
        }

        move.Translate.BeginAnimation(
            TranslateTransform.XProperty, Delayed(FromTo(middle.X - slot.X, 0, CardTime, Spring), begin));
        move.Translate.BeginAnimation(
            TranslateTransform.YProperty, Delayed(FromTo(middle.Y - slot.Y, 0, CardTime, Spring), begin));
        move.Scale.BeginAnimation(
            ScaleTransform.ScaleXProperty, Delayed(FromTo(CardStartScale, 1, CardTime, Spring), begin));
        move.Scale.BeginAnimation(
            ScaleTransform.ScaleYProperty, Delayed(FromTo(CardStartScale, 1, CardTime, Spring), begin));
        card.BeginAnimation(OpacityProperty, Delayed(FromTo(0, 1, DimTime, null), begin));
    }

    /// <summary>Gives a card's transforms, made the first time: a scale about its centre and a move.</summary>
    /// <param name="card">The card.</param>
    /// <returns>The transforms.</returns>
    private static (ScaleTransform Scale, TranslateTransform Translate) Moves(FrameworkElement card)
    {
        if (card.RenderTransform is TransformGroup { Children: [ScaleTransform scale, TranslateTransform translate] })
        {
            return (scale, translate);
        }

        var made = (
            Scale: new ScaleTransform(1, 1, card.Width / 2, card.Height / 2), Translate: new TranslateTransform());
        card.RenderTransform = new TransformGroup { Children = { made.Scale, made.Translate } };
        return made;
    }

    /// <summary>Takes everything away at once, gives the surface its transform back and hides the layer.</summary>
    /// <param name="closed">What to do once it is folded, or <see langword="null"/>.</param>
    private void Finish(Action? closed)
    {
        Clear();
        Visibility = Visibility.Collapsed;
        closed?.Invoke();
    }

    /// <summary>Empties the layer and gives the surface its transform back.</summary>
    private void Clear()
    {
        foreach (UIElement child in Children)
        {
            child.BeginAnimation(OpacityProperty, null);
        }

        Children.Clear();
        _cards.Clear();
        _centre = null;
        _backdropPressed = false;
        if (_pushed is { } pushed)
        {
            pushed.Surface.RenderTransform = pushed.Before;
            _pushed = null;
        }
    }

    /// <summary>Puts an element so that its centre stands at a point.</summary>
    /// <param name="element">The element; it has a width and a height.</param>
    /// <param name="centre">The point.</param>
    private static void Place(FrameworkElement element, Point centre)
    {
        SetLeft(element, centre.X - (element.Width / 2));
        SetTop(element, centre.Y - (element.Height / 2));
    }

    /// <summary>Makes the backdrop cover the whole layer.</summary>
    private void FitBackdrop()
    {
        _backdrop.Width = ActualWidth;
        _backdrop.Height = ActualHeight;
    }

    /// <summary>Gives the middle the pile stands in.</summary>
    /// <returns>The middle.</returns>
    private Point Middle() => _centre is null
        ? new Point(ActualWidth / 2, ActualHeight / 2)
        : new Point(GetLeft(_centre) + (_centre.Width / 2), GetTop(_centre) + (_centre.Height / 2));

    /// <summary>Makes a shock ring's growth.</summary>
    /// <param name="delay">When it starts.</param>
    /// <returns>The animation.</returns>
    private static DoubleAnimation Grow(TimeSpan delay) =>
        Delayed(FromTo(FanShock.StartRadius, FanShock.EndRadius, ShockTime, Shock), delay);

    /// <summary>Makes an animation to a value from where it stands.</summary>
    /// <param name="to">The value.</param>
    /// <param name="time">How long.</param>
    /// <param name="ease">The ease, or <see langword="null"/> for none.</param>
    /// <returns>The animation.</returns>
    private static DoubleAnimation To(double to, Duration time, IEasingFunction? ease) =>
        new(to, time) { EasingFunction = ease };

    /// <summary>Makes an animation between two values.</summary>
    /// <param name="from">The first value.</param>
    /// <param name="to">The last value.</param>
    /// <param name="time">How long.</param>
    /// <param name="ease">The ease, or <see langword="null"/> for none.</param>
    /// <returns>The animation.</returns>
    private static DoubleAnimation FromTo(double from, double to, Duration time, IEasingFunction? ease) =>
        new(from, to, time) { EasingFunction = ease };

    /// <summary>Starts an animation later; until then it holds its first value.</summary>
    /// <param name="animation">The animation.</param>
    /// <param name="begin">When it starts.</param>
    /// <returns>The animation.</returns>
    private static DoubleAnimation Delayed(DoubleAnimation animation, TimeSpan begin)
    {
        animation.BeginTime = begin;
        return animation;
    }
}
