using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using static Chainsaw.App.Controls.Freezing;

namespace Chainsaw.App.Controls;

/// <summary>
/// The surface's headings for the chains' parts (sketch TK1a), the guide on from a lone start point (sketch GR6c), and
/// the abilities given a visible way: a double click on a node expands it, and a right click on the empty surface
/// opens its menu.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>The guide nodes' corner radius, as in sketch GR6c.</summary>
    private const double GuideRadius = 10;

    /// <summary>The square that holds a guide node's icon.</summary>
    private const double GuideIconBox = 26;

    /// <summary>The guide node's icon inside its square.</summary>
    private const double GuideIconSize = 15;

    /// <summary>How faint the later guide node <em>Hitta uttaget</em> is, as in sketch GR6c.</summary>
    private const double FaintGuideOpacity = 0.55;

    /// <summary>How far the sonar's rings reach past <em>Expandera</em>'s frame at their widest.</summary>
    private const double SonarReach = 14;

    /// <summary>How wide the line under <em>Expandera</em> may be before it wraps.</summary>
    private const double GuideTextWidth = 260;

    /// <summary>The dots of the guide's lines (sketch GR6c: <c>1.5 6</c>).</summary>
    private static readonly DashStyle StartGuideDots = Frozen(new DashStyle([1, 4], 0));

    /// <summary>The dashes of a guide node's frame.</summary>
    private static readonly DashStyle StartGuideDash = Frozen(new DashStyle([4, 3], 0));

    /// <summary>The chains' parts as last worked out, and the places they were worked out from.</summary>
    private (IReadOnlyDictionary<string, GraphPoint>? Places, IReadOnlyList<ChainSection> Parts) _sections = (null, []);

    /// <summary>
    /// Follows what the menu and the guide need from the view model: the part on screen moved from the menu, and the
    /// guide drawn again when the exit or an expansion changes what it says.
    /// </summary>
    /// <param name="model">The view model.</param>
    private void AttachReach(GraphViewModel model)
    {
        model.ViewMoveWanted += (_, move) => MoveView(move);
        model.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(GraphViewModel.ShowsOffline) or nameof(GraphViewModel.IsExpanding)
                or nameof(GraphViewModel.HasCase))
            {
                InvalidateVisual();
            }
        };
    }

    /// <summary>Moves the part on screen as the menu on the empty surface asked.</summary>
    /// <param name="move">The move.</param>
    private void MoveView(ViewMove move)
    {
        switch (move)
        {
            case ViewMove.Fit:
                Fit();
                _model?.ViewportChanged(_viewport);
                break;
            case ViewMove.ZoomIn:
                ZoomBy(Viewport.WheelStep);
                break;
            default:
                ZoomBy(1 / Viewport.WheelStep);
                break;
        }
    }

    /// <summary>
    /// A double click on a node expands it at once (TODO, <em>Förmågor utan synlig väg</em>), as E does; the first
    /// click has already chosen it.
    /// </summary>
    /// <param name="e">The press.</param>
    /// <param name="surface">Where it is, on the surface.</param>
    /// <returns><see langword="true"/> when a node took it.</returns>
    private bool PressToExpand(MouseButtonEventArgs e, GraphPoint surface)
    {
        if (_model is null || e.ClickCount != 2 || _blast != BlastPhase.None || _model.Fan is not null
            || (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Shift)) != 0)
        {
            return false;
        }

        return _model.ExpandAt(_model.HitTest(surface, Tolerance).Node);
    }

    /// <summary>A click on the guide's <em>Expandera</em> expands the start point (sketch GR6c).</summary>
    /// <param name="surface">Where the press is, on the surface.</param>
    /// <returns><see langword="true"/> when the guide took it.</returns>
    private bool PressGuide(GraphPoint surface)
    {
        if (_model?.StartPointGuide is not { } guide || !guide.Expand.Contains(surface))
        {
            return false;
        }

        _model.ExpandAt(guide.StartKey);
        return true;
    }

    /// <summary>Gives the guide node under the pointer's explanation, or <see langword="null"/> over neither.</summary>
    /// <param name="surface">The pointer on the surface.</param>
    /// <returns>The explanation.</returns>
    private string? GuideTipAt(GraphPoint surface)
    {
        if (_model?.StartPointGuide is not { } guide)
        {
            return null;
        }

        if (guide.Expand.Contains(surface))
        {
            return StartGuide.ExpandTip;
        }

        return guide.Exit.Contains(surface) ? StartGuide.ExitTip : null;
    }

    /// <summary>
    /// Opens the empty surface's menu where a right click beside the nodes was: a comment there, <em>Passa in</em>
    /// and the zoom.
    /// </summary>
    /// <param name="at">The click on screen.</param>
    private void OpenSurfaceMenu(Point at) => _model?.OpenSurfaceMenu(_viewport.ToSurface(ToGraph(at)));

    /// <summary>
    /// Writes each chain's name above its part of the surface, in quiet capitals, when the graph holds actors on more
    /// than one chain (sketch TK1a); worked out again only when the places change.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="visible">The visible part of the surface.</param>
    private void DrawChainHeadings(DrawingContext dc, GraphScene scene, GraphRect visible)
    {
        var model = _model!;
        if (!ReferenceEquals(_sections.Places, model.Places))
        {
            _sections = (model.Places, ChainSections.Of(scene, model.Places));
        }

        var ink = GraphPalette.Find(this, "Brush.TextFaint");
        var shown = _sections.Parts.Where(part => part.Bounds.Inflate(ChainSections.HeadingLift).Intersects(visible));
        foreach (var part in shown)
        {
            var name = CaseTexts.ChainName(part.Chain).ToUpperInvariant();
            dc.DrawText(CachedText(name, 11, ink, FontWeights.Normal), new Point(part.HeadingAt.X, part.HeadingAt.Y));
        }
    }

    /// <summary>
    /// Draws the guide on from a lone start point (sketch GR6c): a dotted accent line to <em>Expandera</em>, ringed by
    /// the sonar while animations are on, with what it does under it, and a faint dotted line on to the faint
    /// <em>Hitta uttaget</em>.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    private void DrawGuide(DrawingContext dc)
    {
        var model = _model!;
        if (model.StartPointGuide is not { } guide)
        {
            return;
        }

        var accent = GraphPalette.Find(this, "Brush.Accent");
        var faint = GraphPalette.Find(this, "Brush.BorderStrong");
        DrawGuideLine(dc, guide.ToExpand, accent, 0.7);
        DrawGuideLine(dc, guide.ToExit, faint, 0.45);
        DrawSonar(dc, guide.Expand, accent);
        DrawGuideNode(dc, guide.Expand, StartGuide.ExpandTitle, model.StartGuideHint, accent);
        DrawGuideIcon(dc, guide.Expand, "Icon.Plus", GraphPalette.Find(this, "Brush.TextMuted"), round: true);

        var text = Text(model.StartGuideText, 12, GraphPalette.Find(this, "Brush.TextMuted"), FontWeights.Normal);
        text.MaxTextWidth = GuideTextWidth;
        text.TextAlignment = TextAlignment.Center;
        dc.DrawText(text, new Point(guide.Expand.Centre.X - (GuideTextWidth / 2), guide.Expand.Bottom + 10));

        dc.PushOpacity(FaintGuideOpacity);
        DrawGuideNode(dc, guide.Exit, StartGuide.ExitTitle, StartGuide.ExitHint, faint);
        DrawGuideIcon(dc, guide.Exit, "Icon.Kind.Bank", GraphPalette.Find(this, "Brush.Kind.CashOut"), round: false);
        dc.Pop();
    }

    /// <summary>Draws a guide's dotted line.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="curve">The line.</param>
    /// <param name="ink">Its colour.</param>
    /// <param name="opacity">How strong it is.</param>
    private static void DrawGuideLine(DrawingContext dc, EdgeCurve curve, Brush ink, double opacity)
    {
        var pen = new Pen(ink, 1.5)
        {
            DashStyle = StartGuideDots, DashCap = PenLineCap.Round, StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        dc.PushOpacity(opacity);
        dc.DrawGeometry(null, pen, Geometry(curve));
        dc.Pop();
    }

    /// <summary>
    /// Draws the sonar around <em>Expandera</em> (sketch GR6c): two rings in the accent that grow and fade by turns
    /// while animations are on, and stand still, faint, when Windows has them off.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="frame">The guide node's frame.</param>
    /// <param name="accent">The accent.</param>
    private void DrawSonar(DrawingContext dc, GraphRect frame, Brush accent)
    {
        _ringsOnScreen = true;
        var pen = new Pen(accent, 1.5);
        for (var ring = 0; ring < 2; ring++)
        {
            var step = AnimationsOn ? (_ringPhase + (ring * 0.5)) % 1 : 0.5;
            var grow = (step - 0.5) * SonarReach * 2;
            var opacity = AnimationsOn ? 0.55 * (1 - step) : (ring == 0 ? 0.3 : 0);
            var rect = new Rect(
                frame.X - grow, frame.Y - grow, Math.Max(0, frame.Width + (2 * grow)),
                Math.Max(0, frame.Height + (2 * grow)));
            dc.PushOpacity(opacity);
            dc.DrawRoundedRectangle(null, pen, rect, GuideRadius + 8, GuideRadius + 8);
            dc.Pop();
        }
    }

    /// <summary>Draws a guide node: a dashed frame on the panel's ground, its title and its second line.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="frame">Its frame.</param>
    /// <param name="title">Its title.</param>
    /// <param name="hint">Its second line.</param>
    /// <param name="edge">Its frame's colour.</param>
    private void DrawGuideNode(DrawingContext dc, GraphRect frame, string title, string hint, Brush edge)
    {
        var rect = new Rect(frame.X, frame.Y, frame.Width, frame.Height);
        var pen = new Pen(edge, 1.5) { DashStyle = StartGuideDash };
        dc.DrawRoundedRectangle(GraphPalette.Find(this, "Brush.Surface"), pen, rect, GuideRadius, GuideRadius);
        var left = frame.X + 10 + GuideIconBox + 10;
        var name = Text(title, 12.5, GraphPalette.Find(this, "Brush.Text"), FontWeights.SemiBold);
        var second = Text(hint, 11, GraphPalette.Find(this, "Brush.TextDim"), FontWeights.Normal);
        var top = frame.Y + ((frame.Height - name.Height - second.Height - 2) / 2);
        dc.DrawText(name, new Point(left, top));
        dc.DrawText(second, new Point(left, top + name.Height + 2));
    }

    /// <summary>Draws a guide node's icon in its dashed round or square frame.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="frame">The guide node's frame.</param>
    /// <param name="icon">The icon's resource key.</param>
    /// <param name="ink">Its colour.</param>
    /// <param name="round">Whether its frame is round (an actor) or square (a service).</param>
    private void DrawGuideIcon(DrawingContext dc, GraphRect frame, string icon, Brush ink, bool round)
    {
        var corner = new Point(frame.X + 10, frame.Y + ((frame.Height - GuideIconBox) / 2));
        var radius = round ? GuideIconBox / 2 : 7;
        var square = new Rect(corner, new Size(GuideIconBox, GuideIconBox));
        dc.DrawRoundedRectangle(null, new Pen(ink, 1.5) { DashStyle = StartGuideDash }, square, radius, radius);
        var offset = (GuideIconBox - GuideIconSize) / 2;
        DrawGlyph(dc, icon, ink, new Point(corner.X + offset, corner.Y + offset), GuideIconSize);
    }
}
