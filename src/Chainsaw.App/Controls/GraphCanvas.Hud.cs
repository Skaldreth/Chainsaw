using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The performance HUD (Debug only, Ctrl+Shift+P on the graph): frames per second, the time between frames, the time
/// each part of a frame took on the UI thread, how much the frames drew (whole, partly or only moved) and how many
/// nodes and lines, over the last second (<see cref="FrameStats"/>), so that the graph's speed is measured on Windows
/// rather than guessed.
/// </summary>
/// <remarks>
/// The HUD never holds the frames: it is drawn after the frames the surface does anyway, a few times a second, and
/// once more when the frames rest. In a Release build the measuring calls are compiled away
/// (<see cref="ConditionalAttribute"/>) and the key does nothing. The render thread's own time is not seen from here;
/// WPF's own tools (WPF Performance Suite, PresentMon) show it.
/// </remarks>
public sealed partial class GraphCanvas
{
    /// <summary>The least time between two drawings of the HUD.</summary>
    private static readonly TimeSpan HudFrame = TimeSpan.FromMilliseconds(250);

    /// <summary>The HUD's names of the parts of a frame, in Swedish as the rest of the surface.</summary>
    private static readonly Dictionary<FrameStage, string> StageNames = new()
    {
        [FrameStage.Total] = "UI per bild",
        [FrameStage.Hover] = "  Hovring",
        [FrameStage.Motions] = "  Rörelser",
        [FrameStage.Lines] = "  Linjer",
        [FrameStage.Nodes] = "  Noder",
        [FrameStage.OverNodes] = "  Över noderna",
        [FrameStage.Overlay] = "  Ovanpå",
        [FrameStage.Comments] = "  Lappar",
    };

    /// <summary>The HUD's layer, over everything else on the surface.</summary>
    private readonly LayerVisual _hudVisual = new();

    /// <summary>When each part of the frame being measured began, by <see cref="FrameStage"/>.</summary>
    private readonly long[] _hudStarts = new long[Enum.GetValues<FrameStage>().Length];

    // A Release build never shows the HUD, so the field is never set there.
#pragma warning disable CS0649
    /// <summary>The frames measured while the HUD is shown, or <see langword="null"/> while it is not.</summary>
    private FrameStats? _hudStats;
#pragma warning restore CS0649

    /// <summary>The screen's time the HUD was last drawn at.</summary>
    private TimeSpan _hudDrawnAt;

    /// <summary>Shows or hides the HUD with Ctrl+Shift+P; does nothing in a Release build.</summary>
    /// <param name="e">The key.</param>
    /// <returns><see langword="true"/> when the key was the HUD's.</returns>
    private bool HudKey(KeyEventArgs e)
    {
#if DEBUG
        if (e.Key != Key.P || Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift))
        {
            return false;
        }

        _hudStats = _hudStats is null ? new FrameStats() : null;
        DrawHud();
        e.Handled = true;
        return true;
#else
        _ = e;
        return false;
#endif
    }

    /// <summary>Begins measuring a frame of the screen.</summary>
    /// <param name="renderingTime">The screen's time of the frame.</param>
    [Conditional("DEBUG")]
    private void HudFrameBegins(TimeSpan renderingTime) => _hudStats?.Begin(renderingTime);

    /// <summary>
    /// Ends the frame loop's part of a frame: draws the HUD when it is due, or when the frames rest, since nothing
    /// will draw it until they run again.
    /// </summary>
    /// <param name="renderingTime">The screen's time of the frame.</param>
    [Conditional("DEBUG")]
    private void HudFrameEnds(TimeSpan renderingTime)
    {
        if (_hudStats is not null && (!_framesHooked || renderingTime - _hudDrawnAt >= HudFrame))
        {
            _hudDrawnAt = renderingTime;
            DrawHud();
        }
    }

    /// <summary>Notes when a part of the frame begins.</summary>
    /// <param name="stage">The part.</param>
    [Conditional("DEBUG")]
    private void HudStart(FrameStage stage)
    {
        if (_hudStats is not null)
        {
            _hudStarts[(int)stage] = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>Adds the time since a part of the frame began to the frame.</summary>
    /// <param name="stage">The part.</param>
    [Conditional("DEBUG")]
    private void HudStop(FrameStage stage) =>
        _hudStats?.Add(stage, Stopwatch.GetElapsedTime(_hudStarts[(int)stage]).TotalMilliseconds);

    /// <summary>Notes how much the frame drew.</summary>
    /// <param name="work">What it did.</param>
    [Conditional("DEBUG")]
    private void HudMark(FrameWork work) => _hudStats?.Mark(work);

    /// <summary>Notes nodes and lines the frame drew.</summary>
    /// <param name="nodes">The nodes drawn.</param>
    /// <param name="edges">The lines drawn.</param>
    [Conditional("DEBUG")]
    private void HudDrew(int nodes, int edges) => _hudStats?.Drew(nodes, edges);

    /// <summary>
    /// Draws the HUD after a whole drawing of the surface that came after the frames rested, so that the HUD shows the
    /// frame that drew it.
    /// </summary>
    [Conditional("DEBUG")]
    private void HudAfterRender()
    {
        if (_hudStats is not null && !_framesHooked)
        {
            DrawHud();
        }
    }

    /// <summary>Draws the HUD in the top left corner of the surface, or empties its layer when it is hidden.</summary>
    private void DrawHud()
    {
        using var dc = _hudVisual.RenderOpen();
        if (_hudStats is not { } stats)
        {
            return;
        }

        var text = new FormattedText(
            HudText(stats.Summary()),
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Cascadia Mono"),
            11,
            Ink("Brush.Text"),
            PixelsPerDip);
        const double Pad = 10;
        var box = new Rect(12, 12, text.Width + (2 * Pad), text.Height + (2 * Pad));
        dc.DrawRoundedRectangle(Ink("Brush.Surface"), PenOf(Ink("Brush.Border"), 1), box, 6, 6);
        dc.DrawText(text, new Point(box.X + Pad, box.Y + Pad));
    }

    /// <summary>Writes the HUD's lines.</summary>
    /// <param name="summary">The frames over the last second.</param>
    /// <returns>The text.</returns>
    private string HudText(FrameSummary summary)
    {
        var sv = CultureInfo.GetCultureInfo("sv-SE");
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Grafens fart · Ctrl+Shift+P{(_framesHooked ? string.Empty : " · vilar")}");
        text.AppendLine(string.Format(
            sv,
            "{0,5:0} bilder/s   {1:0.0} ms mellan (längst {2:0.0})",
            summary.FramesPerSecond,
            summary.Interval,
            summary.LongestInterval));
        text.AppendLine(string.Format(sv, "{0,-16}{1,8}{2,8}", string.Empty, "snitt", "längst"));
        foreach (var stage in summary.Stages)
        {
            text.AppendLine(string.Format(
                sv,
                "{0,-16}{1,8:0.00}{2,8:0.00}",
                StageNames[stage.Stage],
                stage.Average,
                stage.Longest));
        }

        text.AppendLine(string.Format(
            sv,
            "Hela {0} · Delvis {1} · Flyttad {2} · Inget {3}",
            summary.Work[FrameWork.Full],
            summary.Work[FrameWork.Partial],
            summary.Work[FrameWork.View],
            summary.Work[FrameWork.None]));
        text.AppendLine(string.Format(
            sv,
            "Mest på en bild: {0} noder, {1} linjer",
            summary.Nodes,
            summary.Edges));
        text.Append(string.Format(
            sv,
            "I scenen: {0} noder, {1} linjer · {2} nodvisuals",
            _model?.Scene?.Nodes.Count ?? 0,
            _model?.Curves.Count ?? 0,
            _nodeVisuals.Count));
        return text.ToString();
    }
}
