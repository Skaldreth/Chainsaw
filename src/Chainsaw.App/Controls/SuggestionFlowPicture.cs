using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>
/// The small picture of how the money flows in a suggestion (sketch KF2b): what came in as a bar on the left, a band to
/// each candidate on the right whose width is its score, the rank in a ring and the amount. The chosen candidate is in
/// accent, the rest faint. Only drawing; the numbers come from <see cref="SuggestionFlow"/>.
/// </summary>
public sealed class SuggestionFlowPicture : FrameworkElement
{
    /// <summary>The flow to draw.</summary>
    public static readonly DependencyProperty FlowProperty = DependencyProperty.Register(
        nameof(Flow), typeof(SuggestionFlow), typeof(SuggestionFlowPicture),
        new FrameworkPropertyMetadata(SuggestionFlow.None, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>The picture's height in the sketch.</summary>
    private const double PictureHeight = 132;

    /// <summary>Where the bar of what came in stands from the left.</summary>
    private const double InLeft = 18;

    /// <summary>The bar's width.</summary>
    private const double InWidth = 10;

    /// <summary>The bar's top.</summary>
    private const double InTop = 30;

    /// <summary>The bar's height.</summary>
    private const double InHeight = 84;

    /// <summary>Where the bands end, at the candidates' small bars.</summary>
    private const double BandEnd = 240;

    /// <summary>Where the rank's ring stands.</summary>
    private const double RankX = 266;

    /// <summary>The rank ring's radius.</summary>
    private const double RankRadius = 8;

    /// <summary>Where the amount starts.</summary>
    private const double AmountX = 282;

    /// <summary>The middle of the candidates' column.</summary>
    private const double Middle = 68;

    /// <summary>The most room between two candidates.</summary>
    private const double WidestStep = 48;

    /// <summary>The room the candidates spread over.</summary>
    private const double Spread = 72;

    /// <summary>Where the fee is written, above the bands' middle.</summary>
    private const double FeeX = 118;

    /// <summary>Where the bands bend: the first control point's left.</summary>
    private const double BendFrom = 140;

    /// <summary>Where the bands bend: the second control point's left.</summary>
    private const double BendTo = 150;

    /// <summary>How strong the chosen band is.</summary>
    private const double ChosenOpacity = 0.75;

    /// <summary>How strong another band is.</summary>
    private const double OtherOpacity = 0.45;

    /// <summary>Creates the picture.</summary>
    public SuggestionFlowPicture() => Ink = new ChartDrawing(this);

    /// <summary>Gets or sets the flow to draw.</summary>
    public SuggestionFlow Flow
    {
        get => (SuggestionFlow)GetValue(FlowProperty);
        set => SetValue(FlowProperty, value);
    }

    /// <summary>Gets the text in the page's font, as the other drawn charts write it.</summary>
    private ChartDrawing Ink { get; }

    /// <summary>Takes the panel's width and the sketch's height.</summary>
    /// <param name="availableSize">The room.</param>
    /// <returns>The size.</returns>
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width, PictureHeight);

    /// <summary>Draws the picture.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        var flow = Flow ?? SuggestionFlow.None;
        var dc = drawingContext;
        dc.DrawRoundedRectangle(
            GraphPalette.Find(this, "Brush.Background"), null, new Rect(0, 0, ActualWidth, PictureHeight), 10, 10);
        if (!flow.HasBands)
        {
            return;
        }

        var accent = GraphPalette.Find(this, "Brush.Accent");
        var faint = GraphPalette.Find(this, "Brush.TextFaint");
        var muted = GraphPalette.Find(this, "Brush.TextMuted");
        Write(dc, flow.InText, 11, muted, new Point(InLeft, 8));
        Write(dc, flow.FeeText, 11, GraphPalette.Find(this, "Brush.TextDim"), new Point(FeeX, 8));
        dc.DrawRoundedRectangle(accent, null, new Rect(InLeft, InTop, InWidth, InHeight), 3, 3);
        var count = flow.Bands.Count;
        var step = count > 1 ? Math.Min(WidestStep, Spread / (count - 1)) : 0;
        var first = Middle - (step * (count - 1) / 2);
        for (var index = 0; index < count; index++)
        {
            var band = flow.Bands[index];
            var y = first + (step * index);
            var from = InTop + (InHeight * (index + 0.5) / count);
            DrawBand(dc, band, from, y, band.IsChosen ? accent : faint);
            DrawRank(dc, band, y, accent, faint, muted);
        }
    }

    /// <summary>Draws a band from the bar of what came in to a candidate, and the candidate's small bar.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="band">The band.</param>
    /// <param name="from">Where it leaves the bar.</param>
    /// <param name="y">Where the candidate stands.</param>
    /// <param name="ink">Its ink.</param>
    private static void DrawBand(DrawingContext dc, FlowBand band, double from, double y, Brush ink)
    {
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(InLeft + InWidth, from), false, false);
            context.BezierTo(new Point(BendFrom, from), new Point(BendTo, y), new Point(BandEnd, y), true, true);
        }

        geometry.Freeze();
        dc.PushOpacity(band.IsChosen ? ChosenOpacity : OtherOpacity);
        dc.DrawGeometry(null, new Pen(ink, band.Width), geometry);
        dc.Pop();
        dc.DrawRoundedRectangle(ink, null, new Rect(BandEnd, y - 11, 8, 22), 2.5, 2.5);
    }

    /// <summary>Draws a candidate's rank in its ring and its amount.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="band">The band.</param>
    /// <param name="y">Where the candidate stands.</param>
    /// <param name="accent">The accent.</param>
    /// <param name="faint">The faint ink.</param>
    /// <param name="muted">The muted text.</param>
    private void DrawRank(DrawingContext dc, FlowBand band, double y, Brush accent, Brush faint, Brush muted)
    {
        var center = new Point(RankX, y);
        dc.DrawEllipse(
            band.IsChosen ? accent : null,
            new Pen(band.IsChosen ? accent : faint, 1.4),
            center,
            RankRadius,
            RankRadius);
        var text = band.IsChosen ? GraphPalette.Find(this, "Brush.OnAccent") : muted;
        var rank = Ink.Text(band.Rank.ToString(CultureInfo.InvariantCulture), 10, text, FontWeights.SemiBold);
        dc.DrawText(rank, new Point(RankX - (rank.Width / 2), y - (rank.Height / 2)));
        var amount = Ink.Text(band.AmountText, 11.5, band.IsChosen ? GraphPalette.Find(this, "Brush.Text") : muted);
        dc.DrawText(amount, new Point(AmountX, y - (amount.Height / 2)));
    }

    /// <summary>Writes a line of text from its top left corner; nothing when it is empty.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="text">The text.</param>
    /// <param name="size">The size.</param>
    /// <param name="ink">The ink.</param>
    /// <param name="at">The top left corner.</param>
    private void Write(DrawingContext dc, string text, double size, Brush ink, Point at)
    {
        if (text.Length > 0)
        {
            dc.DrawText(Ink.Text(text, size, ink), at);
        }
    }
}
