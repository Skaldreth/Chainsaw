using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>
/// The graph's surface (sketch GR4): draws the nodes, lines, amounts and hop columns of a <see cref="GraphViewModel"/>
/// and passes on what the pointer and the keys do. It draws only what is on screen (principle 6) and holds only the
/// viewport; what is chosen, moved and shown is the view model's.
/// </summary>
/// <remarks>
/// The pointer: a click chooses a node or a line (or nothing), a node is dragged to move it, the empty surface is
/// dragged to pan, and the wheel zooms around the pointer. The keys: Esc chooses nothing, E expands, F fits the graph
/// to the screen, + and − zoom.
/// </remarks>
public sealed partial class GraphCanvas : FrameworkElement
{
    /// <summary>A node's corner radius.</summary>
    private const double NodeRadius = 10;

    /// <summary>The square that holds a node's icon.</summary>
    private const double IconBox = 24;

    /// <summary>The icon's size inside its square.</summary>
    private const double IconSize = 14;

    /// <summary>The space inside a node's frame, left and right.</summary>
    private const double NodePadding = 9;

    /// <summary>The space between a node's icon, name and count.</summary>
    private const double NodeGap = 8;

    /// <summary>A mark's disc.</summary>
    private const double MarkSize = 18;

    /// <summary>How far a mark's disc reaches past the node's corner.</summary>
    private const double MarkOverhang = 7;

    /// <summary>A mark's icon on its disc.</summary>
    private const double MarkIconSize = 10;

    /// <summary>How far a cluster's second frame lies behind the first (sketch GR3a: several addresses).</summary>
    private const double StackOffset = 3;

    /// <summary>How faint a node is that is not expanded (sketch GR3a, <em>Ej expanderad</em>).</summary>
    private const double NotExpandedOpacity = 0.55;

    /// <summary>How faint a node is that is not near what is chosen.</summary>
    private const double FadedNodeOpacity = 0.3;

    /// <summary>How faint a line is that is not near what is chosen.</summary>
    private const double FadedLineOpacity = 0.1;

    /// <summary>How faint a line is that carries none of the traced money.</summary>
    private const double UntracedLineOpacity = 0.7;

    /// <summary>The short dashes of an uncertain line, in multiples of its thickness (sketch GR3a: 2 5).</summary>
    private static readonly DashStyle UncertainDash = Frozen(new DashStyle([1.6, 4], 0));

    /// <summary>The long dashes of a line to another chain (sketch GR3a: 7 5).</summary>
    private static readonly DashStyle OtherChainDash = Frozen(new DashStyle([5, 3.5], 0));

    /// <summary>The dashes of a frame that is uncertain or ends the trail.</summary>
    private static readonly DashStyle FrameDash = Frozen(new DashStyle([3, 2], 0));

    /// <summary>The view model, once attached.</summary>
    private GraphViewModel? _model;

    /// <summary>What part of the surface is on screen.</summary>
    private Viewport _viewport = Viewport.Initial;

    /// <summary>Whether the graph should be fitted to the screen as soon as the surface has a size.</summary>
    private bool _fitPending;

    /// <summary>The press, drag and click under way.</summary>
    private readonly GraphGesture _gesture = new();

    /// <summary>The hovered node's details, opened and closed by the canvas as the pointer moves between nodes.</summary>
    private readonly ToolTip _hoverTip = new() { Placement = PlacementMode.Mouse };

    /// <summary>Creates the surface, which takes the keyboard when clicked.</summary>
    public GraphCanvas()
    {
        Focusable = true;
        _hoverTip.PlacementTarget = this;
        IsVisibleChanged += (_, _) =>
        {
            CloseHoverTip();
            if (!IsVisible)
            {
                KeepRings(false);
            }
            else
            {
                // Drawn again when shown, so that rings that moved before move on.
                InvalidateVisual();
            }
        };
        FocusVisualStyle = null;
        ClipToBounds = true;
        SizeChanged += (_, _) => FitIfPending();
    }

    /// <summary>Attaches the view model whose graph the surface draws.</summary>
    /// <param name="model">The view model.</param>
    public void Attach(GraphViewModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        _model = model;
        model.Redraw += (_, _) => InvalidateVisual();
        model.ShowNodesWanted += (_, nodes) => FitTo(nodes);
        model.FitWanted += (_, _) =>
        {
            _fitPending = true;
            FitIfPending();
        };
        InvalidateVisual();
    }

    /// <summary>Fits the whole graph to the screen, centred, never larger than its natural size.</summary>
    public void Fit()
    {
        if (_model is not { Places.Count: > 0 } model)
        {
            _viewport = Viewport.Initial;
            InvalidateVisual();
            return;
        }

        var frames = model.Places.Values.Select(GraphSizes.NodeFrame).ToList();
        var bounds = frames.Skip(1).Aggregate(frames[0], (all, frame) => all.Union(frame));
        _viewport = Viewport.Fit(bounds, ActualWidth, ActualHeight);
        InvalidateVisual();
    }

    /// <summary>Zooms around the middle of the screen.</summary>
    /// <param name="factor">How much; above 1 zooms in.</param>
    public void ZoomBy(double factor)
    {
        _viewport = _viewport.ZoomAt(new GraphPoint(ActualWidth / 2, ActualHeight / 2), factor);
        _model?.ViewportChanged(_viewport);
        InvalidateVisual();
    }

    /// <summary>
    /// Raised each time the surface is drawn, after a pan, a zoom, a drag or a new graph, so that what stands over a
    /// node (its actions and forms, sketches IN2b and IN1b) follows it.
    /// </summary>
    public event EventHandler? Drawn;

    /// <summary>Gives where a node's frame stands on the screen, in this control's coordinates.</summary>
    /// <param name="nodeKey">The node.</param>
    /// <returns>The frame, or <see langword="null"/> when the node is not in the graph.</returns>
    public Rect? NodeOnScreen(string? nodeKey)
    {
        if (nodeKey is null || _model is null || !_model.Places.TryGetValue(nodeKey, out var place))
        {
            return null;
        }

        var frame = GraphSizes.NodeFrame(place);
        var corner = _viewport.ToScreen(new GraphPoint(frame.X, frame.Y));
        return new Rect(corner.X, corner.Y, frame.Width * _viewport.Zoom, frame.Height * _viewport.Zoom);
    }

    /// <summary>Draws what is on screen: the hop columns, the lines, their amounts and the nodes.</summary>
    /// <param name="drawingContext">Where to draw.</param>
    protected override void OnRender(DrawingContext drawingContext)
    {
        ArgumentNullException.ThrowIfNull(drawingContext);
        Drawn?.Invoke(this, EventArgs.Empty);

        // A transparent ground, so that the empty surface takes the pointer too.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_model?.Scene is not { } scene)
        {
            KeepRings(false);
            return;
        }

        var visible = _viewport.Visible(ActualWidth, ActualHeight);
        drawingContext.PushTransform(new MatrixTransform(
            _viewport.Zoom, 0, 0, _viewport.Zoom, _viewport.OffsetX, _viewport.OffsetY));
        if (_model.IsHopColumns)
        {
            DrawLanes(drawingContext, scene, visible);
        }

        DrawAreas(drawingContext, visible);
        var (nearNodes, nearEdges) = _model.Near;
        var edges = GraphHitTest.VisibleEdges(scene, _model.Curves, visible).ToList();
        DrawWatchGlows(drawingContext, visible);
        foreach (var edge in edges)
        {
            DrawEdge(drawingContext, edge, _model.Curves[edge.Key], nearEdges);
        }

        foreach (var edge in edges)
        {
            DrawAmount(drawingContext, edge, _model.Curves[edge.Key], nearEdges);
        }

        _ringsOnScreen = false;
        DrawWatchMarks(drawingContext, visible);
        foreach (var node in GraphHitTest.VisibleNodes(scene, _model.Places, visible))
        {
            DrawNode(drawingContext, node, _model.Places[node.Key], nearNodes);
        }

        DrawCandidates(drawingContext, visible);
        DrawAreaChips(drawingContext, visible);
        drawingContext.Pop();
        KeepRings(_ringsOnScreen);
    }

    /// <summary>Presses on the surface: takes hold of a node, or of the surface to pan it.</summary>
    /// <param name="e">The press.</param>
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonDown(e);
        CloseHoverTip();
        Focus();
        _model?.CloseMenu();
        var at = ToGraph(e.GetPosition(this));
        var surface = _viewport.ToSurface(at);
        if (_model is not null && PressArea(surface))
        {
            e.Handled = true;
            return;
        }

        var node = _model?.HitTest(surface, Tolerance).Node;
        var place = node is not null && _model!.Places.TryGetValue(node, out var found) ? found : default;
        _gesture.Press(at, node, place, new GraphPoint(surface.X - place.X, surface.Y - place.Y));
        CaptureMouse();
        e.Handled = true;
    }

    /// <summary>Moves the pointer: drags the node or pans the surface, or says what is under the pointer.</summary>
    /// <param name="e">The move.</param>
    protected override void OnMouseMove(MouseEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseMove(e);
        if (_model is null)
        {
            return;
        }

        var at = ToGraph(e.GetPosition(this));
        var surface = _viewport.ToSurface(at);
        if (!_gesture.IsPressed)
        {
            ShowHover(surface);
            return;
        }

        var (dx, dy) = _gesture.Move(at);
        if (_gesture.IsDragging && _gesture.Node is { } node)
        {
            var wanted = new GraphPoint(surface.X - _gesture.Grip.X, surface.Y - _gesture.Grip.Y);
            _model.MoveNode(node, _gesture.NodePlace, wanted);
        }
        else if (dx != 0 || dy != 0)
        {
            _viewport = _viewport.Pan(dx, dy);
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Lets go: a drag of a node is stored, and a click chooses what is under the pointer. The press is ended before
    /// the capture is released, since releasing it raises <see cref="OnLostMouseCapture"/> at once.
    /// </summary>
    /// <param name="e">The release.</param>
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseLeftButtonUp(e);
        var (end, node) = _gesture.End();
        ReleaseMouseCapture();
        Finish(end, node, ToGraph(e.GetPosition(this)));
        e.Handled = true;
    }

    /// <summary>Ends a press when the capture is lost, for example to another window; a dragged node is stored.</summary>
    /// <param name="e">The arguments.</param>
    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        var (end, node) = _gesture.End();
        if (end == GestureEnd.DropNode)
        {
            Finish(end, node, default);
        }
    }

    /// <summary>The pointer left the surface: nothing is under it any more.</summary>
    /// <param name="e">The arguments.</param>
    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _model?.HoverAt(null, Tolerance);
        CloseHoverTip();
    }

    /// <summary>Zooms around the pointer.</summary>
    /// <param name="e">The wheel.</param>
    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseWheel(e);
        var at = e.GetPosition(this);
        var factor = e.Delta > 0 ? Viewport.WheelStep : 1 / Viewport.WheelStep;
        _viewport = _viewport.ZoomAt(new GraphPoint(at.X, at.Y), factor);
        _model?.ViewportChanged(_viewport);
        InvalidateVisual();
        e.Handled = true;
    }

    /// <summary>
    /// The graph's keys: Esc (closes an open form, then chooses nothing), E, F, + and −, and the inspector's forms L,
    /// U and K (sketch IN1a).
    /// </summary>
    /// <param name="e">The key.</param>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        var plus = e.Key is Key.OemPlus or Key.Add;
        var allowed = plus ? ModifierKeys.Shift : ModifierKeys.None;
        if (_model is not null && e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _model.CopyAddress();
            e.Handled = true;
            return;
        }

        if (_model is null || (Keyboard.Modifiers & ~allowed) != ModifierKeys.None)
        {
            return;
        }

        if (WatchKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (CheckKey(e.Key))
        {
            e.Handled = true;
            return;
        }

        if (LeftForText(e.Key))
        {
            return;
        }

        e.Handled = true;
        switch (e.Key)
        {
            case Key.Escape when _model.Forms.IsOpen:
                _model.Forms.Cancel();
                break;
            case Key.Escape when _model.History.IsOpen:
                _model.History.Cancel();
                break;
            case Key.Escape:
                _model.Choose(null, null);
                break;
            case Key.L:
                _model.Forms.Open(InspectorForm.Label);
                break;
            case Key.U:
                _model.Forms.Open(InspectorForm.Split);
                break;
            case Key.K:
                _model.Forms.Open(InspectorForm.CrossChain);
                break;
            case Key.E:
                _model.ExpandCommand.Execute(null);
                break;
            case Key.F:
                Fit();
                _model.ViewportChanged(_viewport);
                break;
            case Key.OemPlus or Key.Add:
                ZoomBy(Viewport.WheelStep);
                break;
            case Key.OemMinus or Key.Subtract:
                ZoomBy(1 / Viewport.WheelStep);
                break;
            default:
                e.Handled = false;
                break;
        }
    }

    /// <summary>
    /// The watches' keys, before the graph's own: Esc closes the node's menu, then the watch form; B opens
    /// <em>Bevaka …</em> for the chosen node (sketch BV2a).
    /// </summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when it was the watches' key.</returns>
    private bool WatchKey(Key key)
    {
        var model = _model!;
        switch (key)
        {
            case Key.Escape when model.IsMenuOpen:
                model.CloseMenu();
                return true;
            case Key.Escape when model.IsWatchFormOpen:
                model.WatchForm!.Cancel();
                return true;
            case Key.B:
                model.CloseMenu();
                model.OpenWatch();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Does what a press came to: stores a dragged node, or chooses what a click is on.</summary>
    /// <param name="end">What the press came to.</param>
    /// <param name="node">The node it held.</param>
    /// <param name="at">Where on screen it was let go.</param>
    private void Finish(GestureEnd end, string? node, GraphPoint at)
    {
        if (_model is null)
        {
            return;
        }

        // A pan is stored when the press ends, not on every move.
        _model.ViewportChanged(_viewport);
        if (end == GestureEnd.DropNode && node is not null)
        {
            _ = _model.DropNodeAsync(node);
        }
        else if (end == GestureEnd.Click && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            // Ctrl+click picks a counterparty for an open watch form (sketch BV2a), or else chooses actors for an own
            // area (sketch KF1b), one at a time.
            var hit = _model.HitTest(_viewport.ToSurface(at), Tolerance).Node;
            if (_model.PickForWatch(hit))
            {
                return;
            }

            if (hit is { } picked)
            {
                _model.Checks.TogglePick(picked);
            }
        }
        else if (end == GestureEnd.Click)
        {
            _model.Checks.ChooseArea(null);
            _model.ChooseAt(_viewport.ToSurface(at), Tolerance);
        }
    }

    /// <summary>Opens the node's menu on a right click (sketch BV2a), or closes it over empty surface.</summary>
    /// <param name="e">The release.</param>
    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnMouseRightButtonUp(e);
        CloseHoverTip();
        Focus();
        var at = e.GetPosition(this);
        MenuAt = at;
        _model?.OpenMenu(_model.HitTest(_viewport.ToSurface(ToGraph(at)), Tolerance).Node);
        e.Handled = true;
    }

    /// <summary>Gets where on the surface the node's menu was opened, so that the view puts it there.</summary>
    public Point MenuAt { get; private set; }

    /// <summary>Lights what is under the pointer, shows its details as a tooltip and the hand over it.</summary>
    /// <param name="surface">The pointer on the surface.</param>
    private void ShowHover(GraphPoint surface)
    {
        var model = _model!;
        model.HoverAt(surface, Tolerance);
        var hit = model.HitTest(surface, Tolerance);
        Cursor = hit.Node is not null || hit.Edge is not null ? Cursors.Hand : null;
        var text = model.Scene?.Node(hit.Node)?.ToolTip;
        if (text == _hoverTip.Content as string)
        {
            return;
        }

        // WPF's tooltip service only asks when the element under the pointer changes, so the canvas drives its own.
        _hoverTip.IsOpen = false;
        _hoverTip.Content = text;
        _hoverTip.IsOpen = text is not null;
    }

    /// <summary>Closes the hovered node's tooltip, when a press starts or the canvas is hidden or left.</summary>
    private void CloseHoverTip()
    {
        _hoverTip.IsOpen = false;
        _hoverTip.Content = null;
    }

    /// <summary>Turns WPF's point into the graph's.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The same point.</returns>
    private static GraphPoint ToGraph(Point point) => new(point.X, point.Y);

    /// <summary>Freezes a dash style, so that it can be shared.</summary>
    /// <param name="style">The style.</param>
    /// <returns>The frozen style.</returns>
    private static DashStyle Frozen(DashStyle style)
    {
        style.Freeze();
        return style;
    }

    /// <summary>Gets how close to a line the pointer must be, in the surface's units at the current zoom.</summary>
    private double Tolerance => GraphHitTest.LineTolerance / _viewport.Zoom;

    /// <summary>
    /// Fits the graph if a fit waits and the surface has a size; the first time after a case opens, the part on screen
    /// the case stored is shown instead, so that the graph opens as it was left.
    /// </summary>
    private void FitIfPending()
    {
        if (_fitPending && ActualWidth > 0 && ActualHeight > 0)
        {
            _fitPending = false;
            if (_model?.TakeStoredViewport() is { } stored)
            {
                _viewport = stored;
                InvalidateVisual();
                return;
            }

            Fit();
        }
    }

    /// <summary>Draws the hop columns as faint bands with their names at the top.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="visible">The visible part of the surface.</param>
    private void DrawLanes(DrawingContext dc, GraphScene scene, GraphRect visible)
    {
        var lane = GraphPalette.Find(this, "Brush.Graph.Lane");
        var text = GraphPalette.Find(this, "Brush.Graph.LaneText");
        foreach (var column in scene.Lanes.Where(l => l.X <= visible.Right && l.X + GraphSizes.NodeWidth >= visible.X))
        {
            var top = Math.Min(0, visible.Y);
            dc.DrawRoundedRectangle(
                lane, null, new Rect(column.X, top, GraphSizes.NodeWidth, visible.Bottom - top), NodeRadius, NodeRadius);
            var name = Text(column.Name, 11, text, FontWeights.Normal);
            dc.DrawText(name, new Point(column.X + ((GraphSizes.NodeWidth - name.Width) / 2), 8));
        }
    }

    /// <summary>Draws a line: its colour, thickness and dashes, faint when it is not near what is chosen.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="edge">The line.</param>
    /// <param name="curve">Its curve.</param>
    /// <param name="near">The lines near what is chosen.</param>
    private void DrawEdge(DrawingContext dc, EdgeView edge, EdgeCurve curve, IReadOnlySet<string> near)
    {
        var pen = new Pen(GraphPalette.Brush(this, edge.Ink), edge.Width)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            DashCap = PenLineCap.Round,
            DashStyle = edge.Dash switch
            {
                EdgeDash.Uncertain => UncertainDash,
                EdgeDash.OtherChain => OtherChainDash,
                _ => DashStyles.Solid,
            },
        };
        var opacity = LineOpacity(edge, near);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(ToPoint(curve.Start), false, false);
            context.BezierTo(ToPoint(curve.Control1), ToPoint(curve.Control2), ToPoint(curve.End), true, true);
        }

        geometry.Freeze();
        dc.PushOpacity(opacity);
        dc.DrawGeometry(null, pen, geometry);
        dc.Pop();
    }

    /// <summary>Gives how faint a line is: faint away from what is chosen, a little faint without traced money.</summary>
    /// <param name="edge">The line.</param>
    /// <param name="near">The lines near what is chosen.</param>
    /// <returns>The opacity.</returns>
    private double LineOpacity(EdgeView edge, IReadOnlySet<string> near)
    {
        if (_model!.IsFocused && !near.Contains(edge.Key))
        {
            return FadedLineOpacity;
        }

        return edge.IsTraced || edge.Dash == EdgeDash.OtherChain ? 1 : UntracedLineOpacity;
    }

    /// <summary>Gives how faint a node is: faint away from what is chosen, or when it is not expanded.</summary>
    /// <param name="node">The node.</param>
    /// <param name="near">The nodes near what is chosen.</param>
    /// <returns>The opacity.</returns>
    private double NodeOpacity(NodeView node, IReadOnlySet<string> near)
    {
        if (_model!.IsFocused && !near.Contains(node.Key))
        {
            return FadedNodeOpacity;
        }

        return node.IsDim ? NotExpandedOpacity : 1;
    }

    /// <summary>Writes a line's amount at its middle, when the gallring and the focus say so.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="edge">The line.</param>
    /// <param name="curve">Its curve.</param>
    /// <param name="near">The lines near what is chosen.</param>
    private void DrawAmount(DrawingContext dc, EdgeView edge, EdgeCurve curve, IReadOnlySet<string> near)
    {
        var model = _model!;
        if (!model.Labels.Shows(edge.IsTraced, model.HoveredEdge == edge.Key, model.IsFocused, near.Contains(edge.Key)))
        {
            // The swap is not an amount, so the gallring never hides it.
            if (edge.Swap is { } alone)
            {
                DrawSwap(dc, alone, new Point(curve.Middle.X, curve.Middle.Y - 7));
            }

            return;
        }

        var ink = edge.Ink == GraphInk.Untraced
            ? GraphPalette.Find(this, "Brush.TextDim")
            : GraphPalette.Brush(this, edge.Ink);
        var text = Text(edge.Label, 11, ink, FontWeights.Normal);
        var glyph = edge.Glyph.Length > 0 ? Text(edge.Glyph, 11, ink, FontWeights.SemiBold) : null;
        var glyphWidth = glyph is null ? 0 : glyph.Width + 3;
        var value = edge.Value is { } worth
            ? Text(worth.Text, 9.5, GraphPalette.Find(this, "Brush.TextDim"), FontWeights.Normal)
            : null;
        var width = Math.Max(text.Width + glyphWidth, value?.Width ?? 0);
        var height = text.Height + (value?.Height ?? 0);
        var middle = curve.Middle;
        var box = new Rect(middle.X - (width / 2) - 5, middle.Y - (text.Height / 2) - 1, width + 10, height + 2);
        dc.DrawRoundedRectangle(GraphPalette.Find(this, "Brush.Background"), null, box, 4, 4);
        var left = middle.X - ((text.Width + glyphWidth) / 2);
        if (glyph is not null)
        {
            dc.DrawText(glyph, new Point(left, box.Y + 1));
        }

        dc.DrawText(text, new Point(left + glyphWidth, box.Y + 1));
        if (value is not null)
        {
            dc.DrawText(value, new Point(middle.X - (value.Width / 2), box.Y + 1 + text.Height));
        }

        if (edge.Swap is { } swap)
        {
            DrawSwap(dc, swap, new Point(middle.X, box.Bottom + 4));
        }
    }

    /// <summary>
    /// Draws a swap's pill under a line's amount (sketch TK1a): <c>₮ → Ξ</c> in the other chain's purple on a dark
    /// pill, since the money changes token but not owner.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="swap">The pill's text.</param>
    /// <param name="top">The middle of the pill's top edge.</param>
    private void DrawSwap(DrawingContext dc, string swap, Point top)
    {
        var text = Text(swap, 10, GraphPalette.Find(this, "Brush.Kind.OtherChain"), FontWeights.SemiBold);
        var pill = new Rect(top.X - (text.Width / 2) - 7, top.Y, text.Width + 14, text.Height + 2);
        dc.DrawRoundedRectangle(
            GraphPalette.Find(this, "Brush.Graph.SwapPill"),
            new Pen(GraphPalette.Find(this, "Brush.Graph.ChainFrame"), 1),
            pill,
            pill.Height / 2,
            pill.Height / 2);
        dc.DrawText(text, new Point(pill.X + 7, pill.Y + 1));
    }

    /// <summary>Draws a node: its frame, icon, name, count, marks and the other chain's name.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The node.</param>
    /// <param name="place">Its top-left corner.</param>
    /// <param name="near">The nodes near what is chosen.</param>
    private void DrawNode(DrawingContext dc, NodeView node, GraphPoint place, IReadOnlySet<string> near)
    {
        var model = _model!;
        var chosen = model.SelectedNode == node.Key;
        var opacity = NodeOpacity(node, near);
        var frame = new Rect(place.X, place.Y, GraphSizes.NodeWidth, GraphSizes.NodeHeight);
        var ink = GraphPalette.Brush(this, node.Look.Ink);
        dc.PushOpacity(opacity);
        if (node.Look.Sort == NodeSort.Cluster || node.Node.ClusterSize > 1)
        {
            var behind = frame;
            behind.Offset(StackOffset, StackOffset);
            dc.DrawRoundedRectangle(
                GraphPalette.Find(this, "Brush.Background"),
                new Pen(GraphPalette.Find(this, "Brush.SurfaceSelected"), 1),
                behind,
                NodeRadius,
                NodeRadius);
        }

        dc.DrawRoundedRectangle(
            GraphPalette.Find(this, chosen ? "Brush.SurfaceRaised" : "Brush.Surface"),
            FramePen(node, chosen || model.Checks.Picked.Contains(node.Key)),
            frame,
            NodeRadius,
            NodeRadius);
        DrawIcon(dc, node.Look, ink, new Point(place.X + NodePadding, place.Y + ((GraphSizes.NodeHeight - IconBox) / 2)));

        var count = Text(node.Count, 11, GraphPalette.Find(this, "Brush.TextDim"), FontWeights.Normal);
        var nameLeft = place.X + NodePadding + IconBox + NodeGap;
        var countRoom = node.Count.Length > 0 ? count.Width + NodeGap : 0;
        var nameRight = place.X + GraphSizes.NodeWidth - NodePadding - countRoom;
        DrawTitle(dc, node, place, nameLeft, Math.Max(1, nameRight - nameLeft));
        if (node.Count.Length > 0)
        {
            dc.DrawText(count, new Point(nameRight + NodeGap, place.Y + ((GraphSizes.NodeHeight - count.Height) / 2)));
        }

        DrawMarks(dc, node, place);
        DrawWatchMark(dc, node, place);
        if (node.IsStartPoint)
        {
            DrawStartFlag(dc, place);
        }

        if (node.ChainName is { } chain)
        {
            DrawChain(dc, chain, place);
        }

        dc.Pop();
    }

    /// <summary>
    /// Writes a node's title (CLAUDE.md, <em>Nodernas namn</em>; sketch GR5): a name with the address under it in dimmed
    /// grey, or only the address as a quieter title; the address is shortened only when it does not fit.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The node.</param>
    /// <param name="place">Its top-left corner.</param>
    /// <param name="left">Where the text starts.</param>
    /// <param name="room">How wide the text may be.</param>
    private void DrawTitle(DrawingContext dc, NodeView node, GraphPoint place, double left, double room)
    {
        if (!node.IsNamed)
        {
            var title = Fitted(node.Subject, 12.5, GraphPalette.Find(this, "Brush.TextMuted"), FontWeights.Medium, room);
            dc.DrawText(title, new Point(left, place.Y + ((GraphSizes.NodeHeight - title.Height) / 2)));
            return;
        }

        var name = Text(node.Name, 12.5, GraphPalette.Find(this, "Brush.Text"), FontWeights.SemiBold);
        name.MaxTextWidth = room;
        name.MaxLineCount = 1;
        name.Trimming = TextTrimming.CharacterEllipsis;
        var subject = Fitted(node.Subject, 11, GraphPalette.Find(this, "Brush.TextDim"), FontWeights.Normal, room);
        var top = place.Y + ((GraphSizes.NodeHeight - name.Height - subject.Height + 2) / 2);
        dc.DrawText(name, new Point(left, top));
        dc.DrawText(subject, new Point(left, top + name.Height - 2));
    }

    /// <summary>Makes an address's text, shortened in the middle only as much as the room asks.</summary>
    /// <param name="subject">The address, txid or id in full.</param>
    /// <param name="size">The size.</param>
    /// <param name="ink">The colour.</param>
    /// <param name="weight">The weight.</param>
    /// <param name="room">How wide it may be.</param>
    /// <returns>The formatted text.</returns>
    private FormattedText Fitted(string subject, double size, Brush ink, FontWeight weight, double room)
    {
        var text = Text(subject, size, ink, weight);
        if (text.Width <= room || subject.Length == 0)
        {
            return text;
        }

        var characters = (int)Math.Floor(room / (text.Width / subject.Length));
        do
        {
            text = Text(SubjectText.Fit(subject, characters), size, ink, weight);
            characters--;
        }
        while (text.Width > room && characters >= SubjectText.Shortest);

        return text;
    }

    /// <summary>
    /// Draws the start point's flag in the node's top-left corner (sketch GR5, variant A): a light disc of the same
    /// form as the marks in the other corner, so that the two never collide.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="place">The node's top-left corner.</param>
    private void DrawStartFlag(DrawingContext dc, GraphPoint place)
    {
        var (disc, icon) = GraphPalette.Mark(this, NodeBadge.StartPoint);
        var centre = new Point(place.X - MarkOverhang + (MarkSize / 2), place.Y - MarkOverhang + (MarkSize / 2));
        dc.DrawEllipse(disc, new Pen(GraphPalette.Find(this, "Brush.Background"), 2), centre, MarkSize / 2, MarkSize / 2);
        var corner = new Point(centre.X - (MarkIconSize / 2), centre.Y - (MarkIconSize / 2));
        DrawGlyph(dc, VisualLanguage.Of(NodeBadge.StartPoint).Icon, icon, corner, MarkIconSize);
    }

    /// <summary>Gives a node's frame: the accent when chosen, lighter under the pointer, dashed when uncertain.</summary>
    /// <param name="node">The node.</param>
    /// <param name="chosen">Whether it is chosen.</param>
    /// <returns>The pen.</returns>
    private Pen FramePen(NodeView node, bool chosen)
    {
        if (chosen)
        {
            return new Pen(GraphPalette.Find(this, "Brush.Accent"), 1.5);
        }

        if (node.Look.IsDashed)
        {
            return new Pen(GraphPalette.Find(this, "Brush.Graph.DashedFrame"), 1) { DashStyle = FrameDash };
        }

        var hovered = _model!.HoveredNode == node.Key;
        return new Pen(GraphPalette.Find(this, hovered ? "Brush.BorderHover" : "Brush.Border"), 1);
    }

    /// <summary>Draws a sort's icon in its round or square frame (sketch GR3a).</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="look">The sort's look.</param>
    /// <param name="ink">The sort's colour.</param>
    /// <param name="corner">The frame's top-left corner.</param>
    private void DrawIcon(DrawingContext dc, SortLook look, Brush ink, Point corner)
    {
        var radius = look.Shape == NodeShape.Round ? IconBox / 2 : 7;
        var framePen = new Pen(ink, 1.5) { DashStyle = look.IsDashed ? FrameDash : DashStyles.Solid };
        dc.DrawRoundedRectangle(null, framePen, new Rect(corner, new Size(IconBox, IconBox)), radius, radius);
        var offset = (IconBox - IconSize) / 2;
        DrawGlyph(dc, look.Icon, ink, new Point(corner.X + offset, corner.Y + offset), IconSize);
    }

    /// <summary>
    /// Draws an icon of the 24 grid at a size, with the line kept 1.5 px on screen at every zoom, as
    /// <see cref="GridIcon"/> does.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="key">The icon's resource key.</param>
    /// <param name="ink">Its colour.</param>
    /// <param name="corner">Its top-left corner.</param>
    /// <param name="size">Its size on the surface.</param>
    private void DrawGlyph(DrawingContext dc, string key, Brush ink, Point corner, double size)
    {
        if (TryFindResource(key) is not Geometry glyph)
        {
            return;
        }

        var scale = size / GridIcon.GridSize;
        var pen = new Pen(ink, GridIcon.StrokeFor(size * _viewport.Zoom))
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        dc.PushTransform(new MatrixTransform(scale, 0, 0, scale, corner.X, corner.Y));
        dc.DrawGeometry(null, pen, glyph);
        dc.Pop();
    }

    /// <summary>Draws a node's marks as discs in its top-right corner, the first outermost.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The node.</param>
    /// <param name="place">Its top-left corner.</param>
    private void DrawMarks(DrawingContext dc, NodeView node, GraphPoint place)
    {
        var ring = new Pen(GraphPalette.Find(this, "Brush.Background"), 2);
        for (var index = 0; index < node.Badges.Count; index++)
        {
            var badge = node.Badges[index];
            var (disc, icon) = GraphPalette.Mark(this, badge);
            var centre = new Point(
                place.X + GraphSizes.NodeWidth + MarkOverhang - (MarkSize / 2) - (index * (MarkSize + 2)),
                place.Y - MarkOverhang + (MarkSize / 2));
            dc.DrawEllipse(disc, ring, centre, MarkSize / 2, MarkSize / 2);
            var corner = new Point(centre.X - (MarkIconSize / 2), centre.Y - (MarkIconSize / 2));
            DrawGlyph(dc, VisualLanguage.Of(badge).Icon, icon, corner, MarkIconSize);
        }
    }

    /// <summary>Writes the other chain's short name under a node's corner (sketch GR3c: TRON).</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="chain">The chain's short name.</param>
    /// <param name="place">The node's top-left corner.</param>
    private void DrawChain(DrawingContext dc, string chain, GraphPoint place)
    {
        var text = Text(chain, 10, GraphPalette.Find(this, "Brush.Kind.OtherChain"), FontWeights.SemiBold);
        var box = new Rect(
            place.X + GraphSizes.NodeWidth + 10 - text.Width - 10,
            place.Y + GraphSizes.NodeHeight + 9 - text.Height - 2,
            text.Width + 10,
            text.Height + 2);
        dc.DrawRoundedRectangle(
            GraphPalette.Find(this, "Brush.Background"), new Pen(GraphPalette.Find(this, "Brush.Graph.ChainFrame"), 1),
            box, 6, 6);
        dc.DrawText(text, new Point(box.X + 5, box.Y + 1));
    }

    /// <summary>Makes a text in the UI font.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The size.</param>
    /// <param name="ink">The colour.</param>
    /// <param name="weight">The weight.</param>
    /// <returns>The formatted text.</returns>
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

    /// <summary>Turns a point of the surface into WPF's point.</summary>
    /// <param name="point">The point.</param>
    /// <returns>The same point.</returns>
    private static Point ToPoint(GraphPoint point) => new(point.X, point.Y);
}
