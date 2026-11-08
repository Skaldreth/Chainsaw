using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>
/// The strip along the bottom of the graph (sketch TL1a): a thin line that fades in and out at its ends, whole up to
/// the last transaction and dotted after, where nothing more is fetched; one stroke per transaction and the days under
/// it. Hovering shows a card with what moved and a faint front in the graph; a click chooses the time, which gets a dot
/// with a sonar in the strip while the graph shows what came later faint.
/// </summary>
/// <remarks>
/// Only the strip itself takes the pointer: the room above it, where the card opens, is left to the graph. The sonar
/// stands still when Windows has animations turned off.
/// </remarks>
public sealed partial class TimeStrip : FrameworkElement
{
    /// <summary>How far above the bottom the line runs.</summary>
    private const double LineFromBottom = 26;

    /// <summary>How far past its ends the line runs while it fades (sketch TL1a: 30 px).</summary>
    private const double FadeRoom = 30;

    /// <summary>How much of the strip, at each end, the line fades over.</summary>
    private const double FadeShare = 0.06;

    /// <summary>How far above and below the line the strip takes the pointer.</summary>
    private const double HitReach = 14;

    /// <summary>How close to a stroke the pointer must be to hover or choose it, in pixels.</summary>
    private const double TickTolerance = 5;

    /// <summary>A stroke's width.</summary>
    private const double TickWidth = 2;

    /// <summary>A stroke's height.</summary>
    private const double TickHeight = 12;

    /// <summary>The height of the stroke under the pointer.</summary>
    private const double HoveredTickHeight = 18;

    /// <summary>The least distance between two days' names.</summary>
    private const double LeastDayGap = 64;

    /// <summary>How far under the line the days' names stand.</summary>
    private const double DayTop = 8;

    /// <summary>The chosen time's dot (sketch TL1a: 11 px with a 2 px ring in the background's colour).</summary>
    private const double DotRadius = 5.5;

    /// <summary>The sonar's ring at its start and its end.</summary>
    private const double SonarFrom = 5.5, SonarTo = 14;

    /// <summary>The sonar's ring when it stands still.</summary>
    private const double SonarStill = 10;

    /// <summary>How long one wave of the sonar takes.</summary>
    private static readonly TimeSpan SonarPeriod = TimeSpan.FromSeconds(2.8);

    /// <summary>The share of a wave the ring grows in; the rest is quiet.</summary>
    private const double SonarGrowth = 0.7;

    /// <summary>How often the sonar is drawn while it moves.</summary>
    private static readonly TimeSpan SonarFrame = TimeSpan.FromMilliseconds(33);

    /// <summary>How far above the line the card's bottom stands.</summary>
    private const double CardGap = 16;

    /// <summary>The space inside the card.</summary>
    private const double CardPadding = 12;

    /// <summary>The card's corner radius.</summary>
    private const double CardRadius = 12;

    /// <summary>The dots of the strip after the last transaction, in multiples of its width.</summary>
    private static readonly DashStyle StripDots = Frozen(new DashStyle([0.01 / 1.5, 4 / 1.5], 0));

    /// <summary>The time line, once attached.</summary>
    private GraphTimelineViewModel? _model;

    /// <summary>The stroke under the pointer, or <see langword="null"/>.</summary>
    private int? _hoveredTick;

    /// <summary>The time under the pointer, in hours, or <see langword="null"/> when the pointer is not on the strip.</summary>
    private double? _hoveredHours;

    /// <summary>Draws the sonar while it moves.</summary>
    private DispatcherTimer? _sonar;

    /// <summary>When the sonar's waves began.</summary>
    private DateTime _sonarStart;

    /// <summary>Creates the strip.</summary>
    public TimeStrip()
    {
        Cursor = Cursors.Hand;
        IsVisibleChanged += (_, _) => KeepSonar();
    }

    /// <summary>Attaches the time line the strip shows.</summary>
    /// <param name="model">The time line.</param>
    public void Attach(GraphTimelineViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        model.Changed += (_, _) =>
        {
            KeepSonar();
            InvalidateVisual();
        };
        InvalidateVisual();
    }

    /// <summary>Gets the strip's layout at its current size, or <see langword="null"/> when there is nothing to show.</summary>
    private TimeStripLayout? Layout => _model is { IsShown: true } model && ActualWidth > 2 * FadeRoom
        ? new TimeStripLayout(
            model.Axis, [.. model.Times.Ticks.Select(tick => tick.Time)], FadeRoom, ActualWidth - FadeRoom,
            LensOf(model).Window, LensOf(model).Newest)
        : null;

    /// <summary>Gets where the line runs, from the top.</summary>
    private double LineY => ActualHeight - LineFromBottom;

    /// <summary>Draws the strip, its strokes, the days, the chosen time's dot and the card for the hovered stroke.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        if (Layout is not { } layout)
        {
            return;
        }

        // A transparent ground under the strip only, so that the strip takes the pointer and the room above does not.
        drawingContext.DrawRectangle(
            Brushes.Transparent, null, new Rect(0, GroundTop, ActualWidth, ActualHeight - GroundTop));
        DrawLensGround(drawingContext, layout);
        DrawLine(drawingContext, layout);
        DrawLensMarks(drawingContext, layout);
        DrawTicks(drawingContext, layout);
        DrawDays(drawingContext, layout);
        DrawNewestBlock(drawingContext, layout);
        DrawChosen(drawingContext, layout);
        DrawLensChip(drawingContext, layout);
        DrawCard(drawingContext, layout);
    }

    /// <summary>Follows the pointer: the stroke under it, or the time under it, and the faint front in the graph.</summary>
    /// <param name="e">The move.</param>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (_model is null || Layout is not { } layout)
        {
            return;
        }

        var x = e.GetPosition(this).X;
        _hoveredTick = layout.NearestTick(x, TickTolerance);
        _hoveredHours = layout.HoursAt(x);
        _model.Hover(HoveredTime(layout));
        InvalidateVisual();
    }

    /// <summary>The pointer left the strip: nothing is hovered, and the front leaves the graph.</summary>
    /// <param name="e">The arguments.</param>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hoveredTick = null;
        _hoveredHours = null;
        _model?.Hover(null);
        InvalidateVisual();
    }

    /// <summary>Chooses the time under the pointer: the stroke's own time when one is close.</summary>
    /// <param name="e">The press.</param>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        if (_model is null || Layout is not { } layout)
        {
            return;
        }

        if (OnChip(e.GetPosition(this)))
        {
            _model.StepLens();
            e.Handled = true;
            return;
        }

        var x = e.GetPosition(this).X;
        _hoveredTick = layout.NearestTick(x, TickTolerance);
        _hoveredHours = layout.HoursAt(x);
        if (HoveredTime(layout) is { } time)
        {
            _model.Choose(time);
        }

        e.Handled = true;
    }

    /// <summary>Gives the time the pointer points at: the hovered stroke's, or the strip's time under it.</summary>
    /// <param name="layout">The layout.</param>
    /// <returns>The time, or <see langword="null"/>.</returns>
    private DateTimeOffset? HoveredTime(TimeStripLayout layout)
    {
        if (_hoveredTick is { } tick)
        {
            return _model!.Times.Ticks[tick].Time;
        }

        return _hoveredHours is { } hours ? layout.Axis.Time(hours) : null;
    }

    /// <summary>Draws the line: whole up to the last transaction, dotted after it, fading at both ends.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="layout">The layout.</param>
    private void DrawLine(DrawingContext dc, TimeStripLayout layout)
    {
        var y = LineY;
        var whole = new Pen(Faded("Brush.BorderStrong"), 1);
        whole.Freeze();
        dc.DrawLine(whole, new Point(0, y), new Point(layout.LastTickX, y));
        var dotted = new Pen(Faded("Brush.BorderHover"), 1.5)
        {
            DashStyle = StripDots,
            DashCap = PenLineCap.Round,
        };
        dotted.Freeze();
        var end = layout.NewestBlock is null ? ActualWidth : layout.EndX - 12;
        dc.DrawLine(dotted, new Point(layout.LastTickX + 6, y), new Point(end, y));
    }

    /// <summary>
    /// Draws one stroke per transaction, at most one per pixel: grey up to the chosen time, faint after it, and light
    /// and taller under the pointer.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="layout">The layout.</param>
    private void DrawTicks(DrawingContext dc, TimeStripLayout layout)
    {
        var ticks = _model!.Times.Ticks;
        var chosen = _model.Chosen;
        var past = GraphPalette.Find(this, "Brush.TextDim");
        var later = GraphPalette.Find(this, "Brush.BorderStrong");
        foreach (var (x, index) in layout.TickColumns())
        {
            var ink = chosen is { } at && ticks[index].Time > at ? later : past;
            DrawTick(dc, x, TickHeight, ink);
        }

        if (_hoveredTick is { } hovered)
        {
            DrawTick(dc, layout.X(layout.Axis.Hours(ticks[hovered].Time)), HoveredTickHeight,
                GraphPalette.Find(this, "Brush.Text"));
        }
    }

    /// <summary>Draws a stroke, centred on the line.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="x">Its place.</param>
    /// <param name="height">Its height.</param>
    /// <param name="ink">Its colour.</param>
    private void DrawTick(DrawingContext dc, double x, double height, Brush ink) =>
        dc.DrawRoundedRectangle(ink, null, new Rect(x - (TickWidth / 2), LineY - (height / 2), TickWidth, height), 1, 1);

    /// <summary>Writes the days under the strip; the days after the last transaction are fainter.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="layout">The layout.</param>
    private void DrawDays(DrawingContext dc, TimeStripLayout layout)
    {
        var dim = GraphPalette.Find(this, "Brush.TextDim");
        var faint = GraphPalette.Find(this, "Brush.TextFaint");
        foreach (var day in layout.Days(LeastDayGap))
        {
            var text = Text(day.Name, 11, day.IsAfterLast ? faint : dim, FontWeights.Normal);
            var left = Math.Clamp(day.X - (text.Width / 2), 0, Math.Max(0, ActualWidth - text.Width));
            dc.DrawText(text, new Point(left, LineY + DayTop));
        }
    }

    /// <summary>
    /// Draws the chosen time's dot on the strip with its sonar, a ring that grows and fades; a still ring when Windows
    /// has animations turned off.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="layout">The layout.</param>
    private void DrawChosen(DrawingContext dc, TimeStripLayout layout)
    {
        if (_model!.Chosen is not { } chosen)
        {
            return;
        }

        var centre = new Point(layout.X(layout.Axis.Hours(chosen)), LineY);
        var light = GraphPalette.Find(this, "Brush.Text");
        var (radius, opacity) = SonarRing();
        dc.PushOpacity(opacity);
        dc.DrawEllipse(null, new Pen(light, 1.5), centre, radius, radius);
        dc.Pop();
        dc.DrawEllipse(light, new Pen(GraphPalette.Find(this, "Brush.Background"), 2), centre, DotRadius, DotRadius);
    }

    /// <summary>Gives the sonar's ring now: growing and fading through a wave, or standing still.</summary>
    /// <returns>The ring's radius and opacity.</returns>
    private (double Radius, double Opacity) SonarRing()
    {
        if (!AnimationsOn)
        {
            return (SonarStill, 0.3);
        }

        var phase = (DateTime.UtcNow - _sonarStart).TotalMilliseconds % SonarPeriod.TotalMilliseconds
            / SonarPeriod.TotalMilliseconds;
        var grown = Math.Min(1, phase / SonarGrowth);
        var eased = 1 - Math.Pow(1 - grown, 3);
        return (SonarFrom + ((SonarTo - SonarFrom) * eased), 0.6 * (1 - grown));
    }

    /// <summary>
    /// Draws the card over the hovered stroke: the time, and what moved (sketch TL1a); over the strip elsewhere, only the
    /// time under the pointer.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="layout">The layout.</param>
    private void DrawCard(DrawingContext dc, TimeStripLayout layout)
    {
        if (_hoveredHours is null || HoveredTime(layout) is not { } time)
        {
            return;
        }

        var onBlock = _hoveredTick is null && OnNewestBlock(layout, layout.X(_hoveredHours.Value));
        var (titleText, lineText) = onBlock ? NewestBlockCard() : (layout.Axis.TimeName(time), TickText());
        var title = Text(titleText, 13, GraphPalette.Find(this, "Brush.Text"), FontWeights.SemiBold);
        var line = lineText is null
            ? null
            : Text(lineText, 12, GraphPalette.Find(this, "Brush.TextMuted"), FontWeights.Normal);
        var width = Math.Max(title.Width, line?.Width ?? 0) + (2 * CardPadding);
        var height = title.Height + (line is null ? 0 : line.Height + 3) + 20;
        var x = Math.Clamp(layout.X(layout.Axis.Hours(time)) - (width / 2), 0, Math.Max(0, ActualWidth - width));
        var card = new Rect(x, LineY - CardGap - height, width, height);
        var shadow = card;
        shadow.Offset(0, 6);
        dc.PushOpacity(0.35);
        dc.DrawRoundedRectangle(Brushes.Black, null, shadow, CardRadius, CardRadius);
        dc.Pop();
        dc.DrawRoundedRectangle(
            GraphPalette.Find(this, "Brush.SurfaceRaised"),
            new Pen(GraphPalette.Find(this, "Brush.BorderStrong"), 1),
            card,
            CardRadius,
            CardRadius);
        dc.DrawText(title, new Point(card.X + CardPadding, card.Y + 10));
        if (line is not null)
        {
            dc.DrawText(line, new Point(card.X + CardPadding, card.Y + 10 + title.Height + 3));
        }
    }

    /// <summary>Gives what moved in the hovered stroke, or <see langword="null"/> when no stroke is hovered.</summary>
    /// <returns>The text.</returns>
    private string? TickText() => _hoveredTick is { } tick ? _model!.Times.Ticks[tick].Text : null;

    /// <summary>Gives a brush of the theme that fades in at the strip's left end and out at its right.</summary>
    /// <param name="key">The brush's resource key.</param>
    /// <returns>The brush.</returns>
    private Brush Faded(string key)
    {
        var ink = GraphPalette.Find(this, key) is SolidColorBrush solid ? solid.Color : Colors.Gray;
        var clear = Color.FromArgb(0, ink.R, ink.G, ink.B);
        var brush = new LinearGradientBrush
        {
            MappingMode = BrushMappingMode.Absolute,
            StartPoint = new Point(0, 0),
            EndPoint = new Point(Math.Max(1, ActualWidth), 0),
            GradientStops =
            [
                new GradientStop(clear, 0),
                new GradientStop(ink, FadeShare),
                new GradientStop(ink, 1 - FadeShare),
                new GradientStop(clear, 1),
            ],
        };
        brush.Freeze();
        return brush;
    }

    /// <summary>Keeps the sonar moving while a time is chosen, the strip is shown and Windows animates.</summary>
    private void KeepSonar()
    {
        if (_model?.IsChosen == true && IsVisible && AnimationsOn)
        {
            if (_sonar is null)
            {
                _sonarStart = DateTime.UtcNow;
                _sonar = new DispatcherTimer(SonarFrame, DispatcherPriority.Render, OnSonarFrame, Dispatcher);
            }

            return;
        }

        _sonar?.Stop();
        _sonar = null;
    }

    /// <summary>Draws the sonar's next frame, unless the window is minimised.</summary>
    /// <param name="sender">The timer.</param>
    /// <param name="e">The arguments.</param>
    private void OnSonarFrame(object? sender, EventArgs e)
    {
        if (!GraphCanvas.IsMinimised(this))
        {
            InvalidateVisual();
        }
    }

    /// <summary>Gets whether Windows animates; when it does not, the sonar stands still.</summary>
    private static bool AnimationsOn => SystemParameters.ClientAreaAnimation;

    /// <summary>Writes a text in the interface's font.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">Its size.</param>
    /// <param name="ink">Its colour.</param>
    /// <param name="weight">Its weight.</param>
    /// <returns>The text, ready to draw.</returns>
    private FormattedText Text(string text, double size, Brush ink, FontWeight weight)
    {
        var family = TryFindResource("Font.Ui") as FontFamily ?? new FontFamily("Segoe UI");
        return new FormattedText(
            text,
            CultureInfo.GetCultureInfo("sv-SE"),
            FlowDirection.LeftToRight,
            new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal),
            size,
            ink,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
    }

    /// <summary>Freezes a dash style, so that it can be shared.</summary>
    /// <param name="style">The style.</param>
    /// <returns>The frozen style.</returns>
    private static DashStyle Frozen(DashStyle style)
    {
        style.Freeze();
        return style;
    }
}
