using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using static Chainsaw.App.Controls.Freezing;

namespace Chainsaw.App.Controls;

/// <summary>
/// The strip as a lens (sketch TL2a): the selection view's window magnified on a raised ground with soft edges, its days
/// and hours, the held-back transactions as short grey strokes and the traced arrival as a dashed accent line; a chip
/// over it steps the window. The strip ends dotted at the newest block, a small cube with its height.
/// </summary>
public sealed partial class TimeStrip
{
    /// <summary>The lens's ground's height, centred on the line.</summary>
    private const double LensHeight = 64;

    /// <summary>How far the lens's ground reaches past its window on either side.</summary>
    private const double LensReach = 8;

    /// <summary>The lens's corner radius.</summary>
    private const double LensRadius = 12;

    /// <summary>How far above the line the chip's middle stands.</summary>
    private const double ChipRise = 44;

    /// <summary>The chip's height.</summary>
    private const double ChipHeight = 22;

    /// <summary>The newest block's disc.</summary>
    private const double CubeDisc = 20;

    /// <summary>Where the chip stood when it was last drawn, so that a click on it steps the window.</summary>
    private Rect? _chip;

    /// <summary>Gives the lens's window and the newest block's time on the axis, for the layout.</summary>
    /// <param name="model">The time line.</param>
    /// <returns>The window, or <see langword="null"/>, and the block's time, or <see langword="null"/>.</returns>
    private static (StripWindow? Window, double? Newest) LensOf(GraphTimelineViewModel model) =>
        (model.LensWindow, model.NewestBlock is { } tip ? model.Axis.Hours(tip.Time) : null);

    /// <summary>Gives how far up the strip takes the pointer: above the chip when there is a lens.</summary>
    private double GroundTop => _model?.HasLens == true ? LineY - ChipRise - (ChipHeight / 2) : LineY - HitReach;

    /// <summary>Draws the lens's ground under the line, with soft edges on its sides.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="layout">The layout.</param>
    private void DrawLensGround(DrawingContext dc, TimeStripLayout layout)
    {
        if (layout.Window is not { } window)
        {
            return;
        }

        var left = layout.X(window.From) - LensReach;
        var right = layout.X(window.To) + LensReach;
        var ground = new Rect(left, LineY - (LensHeight / 2), right - left, LensHeight);
        dc.DrawRoundedRectangle(GraphPalette.Find(this, "Brush.Surface"), null, ground, LensRadius, LensRadius);
    }

    /// <summary>
    /// Draws what stands inside the lens: the days' marks and names, the hours, the held-back transactions as short grey
    /// strokes and the traced arrival as a dashed accent line.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="layout">The layout.</param>
    private void DrawLensMarks(DrawingContext dc, TimeStripLayout layout)
    {
        if (layout.Window is not { } window)
        {
            return;
        }

        var model = _model!;
        var dim = GraphPalette.Find(this, "Brush.TextDim");
        foreach (var mark in layout.LensMarks())
        {
            var reach = mark.IsDay ? 9 : 3;
            var ink = GraphPalette.Find(this, mark.IsDay ? "Brush.TextDim" : "Brush.BorderHover");
            dc.DrawLine(new Pen(ink, 1), new Point(mark.X, LineY - reach), new Point(mark.X, LineY + reach));
            if (mark.Name is { } name)
            {
                var text = Text(name, mark.IsDay ? 11 : 10, mark.IsDay ? GraphPalette.Find(this, "Brush.TextMuted") : dim,
                    FontWeights.Normal);
                dc.DrawText(text, new Point(mark.X - (text.Width / 2), LineY + DayTop));
            }
        }

        var heldBack = new Pen(GraphPalette.Find(this, "Brush.Strip.HeldBack"), 1.5)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        foreach (var hours in model.LensHeldBack.Select(model.Axis.Hours)
                     .Where(hours => hours >= window.From && hours <= window.To))
        {
            var x = layout.X(hours);
            dc.DrawLine(heldBack, new Point(x, LineY - 5), new Point(x, LineY + 5));
        }

        if (model.LensArrival is { } arrival)
        {
            var x = layout.X(model.Axis.Hours(arrival));
            var line = new Pen(GraphPalette.Find(this, "Brush.Accent"), 1.5) { DashStyle = ArrivalDash };
            dc.DrawLine(line, new Point(x, LineY - 22), new Point(x, LineY + 12));
        }
    }

    /// <summary>The arrival's dashes, 3 px apart.</summary>
    private static readonly DashStyle ArrivalDash = Frozen(new DashStyle([2, 2], 0));

    /// <summary>Draws the chip over the lens that names its window, and remembers where it stands.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="layout">The layout.</param>
    private void DrawLensChip(DrawingContext dc, TimeStripLayout layout)
    {
        _chip = null;
        if (layout.Window is not { } window)
        {
            return;
        }

        var text = Text(_model!.LensText, 11.5, GraphPalette.Find(this, "Brush.Text"), FontWeights.Normal);
        var width = text.Width + 9 + 18 + 9;
        var middle = (layout.X(window.From) + layout.X(window.To)) / 2;
        var left = Math.Clamp(middle - (width / 2), 0, Math.Max(0, ActualWidth - width));
        var chip = new Rect(left, LineY - ChipRise - (ChipHeight / 2), width, ChipHeight);
        dc.DrawRoundedRectangle(
            GraphPalette.Find(this, "Brush.SurfaceRaised"), new Pen(GraphPalette.Find(this, "Brush.BorderStrong"), 1),
            chip, ChipHeight / 2, ChipHeight / 2);
        DrawIcon(dc, "Icon.Lens", GraphPalette.Find(this, "Brush.TextMuted"), new Point(chip.X + 9, chip.Y + 5), 12);
        dc.DrawText(text, new Point(chip.X + 27, chip.Y + ((ChipHeight - text.Height) / 2)));
        _chip = chip;
    }

    /// <summary>Draws the newest block where the strip ends: a cube on a disc, and its height under it.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="layout">The layout.</param>
    private void DrawNewestBlock(DrawingContext dc, TimeStripLayout layout)
    {
        if (_model!.NewestBlock is not { } tip || layout.NewestBlock is null)
        {
            return;
        }

        var centre = new Point(layout.EndX, LineY);
        dc.DrawEllipse(GraphPalette.Find(this, "Brush.Background"),
            new Pen(GraphPalette.Find(this, "Brush.BorderHover"), 1.5), centre, CubeDisc / 2, CubeDisc / 2);
        DrawIcon(dc, "Icon.Block", GraphPalette.Find(this, "Brush.TextMuted"), new Point(centre.X - 5.5, centre.Y - 5.5),
            11);
        var height = Text(BlockMarks.Height(tip.Height), 10.5, GraphPalette.Find(this, "Brush.TextDim"),
            FontWeights.Normal);
        dc.DrawText(height, new Point(centre.X - (height.Width / 2), LineY + (CubeDisc / 2) + 2));
    }

    /// <summary>Says whether a point is on the lens's chip.</summary>
    /// <param name="point">The point.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private bool OnChip(Point point) => _chip is { } chip && chip.Contains(point);

    /// <summary>Says whether a place on the strip is on the newest block's cube.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="x">The place.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private bool OnNewestBlock(TimeStripLayout layout, double x) =>
        layout.NewestBlock is not null && Math.Abs(x - layout.EndX) <= CubeDisc / 2;

    /// <summary>Gives what the card over the newest block says.</summary>
    /// <returns>The title and the line under it.</returns>
    private (string Title, string Line) NewestBlockCard()
    {
        var tip = _model!.NewestBlock!;
        var title = $"Senaste block {BlockMarks.Height(tip.Height)} · {_model.Axis.TimeName(tip.Time)}";
        return (title, "Prickat före: inget mer är hämtat i grafen.");
    }

    /// <summary>Draws an icon of the 24 grid at a size, with the line 1.5 px.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="key">The icon's resource key.</param>
    /// <param name="ink">Its colour.</param>
    /// <param name="corner">Its top-left corner.</param>
    /// <param name="size">Its size.</param>
    private void DrawIcon(DrawingContext dc, string key, Brush ink, Point corner, double size)
    {
        if (TryFindResource(key) is not Geometry glyph)
        {
            return;
        }

        var scale = size / GridIcon.GridSize;
        var pen = new Pen(ink, GridIcon.StrokeFor(size))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, corner.X, corner.Y));
        dc.DrawGeometry(null, pen, glyph);
        dc.Pop();
    }
}
