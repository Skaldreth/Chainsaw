using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The scene and what stands over it, as layers of their own (principle 6). The scene is drawn on the surface's units
/// in four layers, in the order it was always drawn: the lines' layer (the hop columns, the time bands, the chains'
/// headings, the areas, the notes' threads, the lines with their pills, amounts and watches), the nodes' layer, the
/// loose layer (the nodes being dragged) and the layer over the nodes (the cards, the trash's warning, the candidates,
/// the areas' chips, the start guide, the fan and the found node's ring). Their drawing is kept while only their
/// transform follows a pan or a glide (<see cref="SceneWindow"/>); what stands on the screen over them (the days'
/// names, the box, the block mark's box and the selection view) is drawn again in every frame.
/// </summary>
/// <remarks>
/// <para>
/// The layers are their own visuals rather than drawn in <see cref="OnRender"/>: WPF lets go of everything the last
/// <see cref="OnRender"/> drew before it takes the new drawing, so a drawing kept from one frame to the next would
/// still be sent again in full. They lie under the comments' notes.
/// </para>
/// <para>
/// While nodes are dragged (<see cref="ViewModels.GraphViewModel.Moved"/>), the nodes that stand still keep their drawing and
/// only the lines' layer, the loose layer and the layer over the nodes are drawn again, which draws exactly what a
/// whole drawing would: every layer keeps its place in the order, and when a dragged node overlaps a node drawn after
/// it, the whole scene is drawn instead so that the still node stays on top.
/// </para>
/// </remarks>
public sealed partial class GraphCanvas
{
    /// <summary>How many layers stand under the notes: the scene's four and the one over them.</summary>
    private const int SceneLayers = 5;

    /// <summary>Decides when the scene's drawing can be moved instead of made again.</summary>
    private readonly SceneWindow _sceneWindow = new();

    /// <summary>The lines' layer: the hop columns, the time bands, the areas, the lines, their amounts and watches.</summary>
    private readonly LayerVisual _linesLayer = new();

    /// <summary>The nodes' layer: the nodes that stand still.</summary>
    private readonly LayerVisual _nodesLayer = new();

    /// <summary>The loose layer: the nodes being dragged, over the others.</summary>
    private readonly LayerVisual _looseLayer = new();

    /// <summary>The layer over the nodes: the cards, the candidates, the chips, the guide, the fan and the found ring.</summary>
    private readonly LayerVisual _overNodesLayer = new();

    /// <summary>The layer over the scene, drawn on screen units in every frame.</summary>
    private readonly LayerVisual _overlayVisual = new();

    /// <summary>Moves the scene's layers onto the screen: changed in place, so that a pan sends only it.</summary>
    private readonly MatrixTransform _sceneTransform = new();

    /// <summary>The nodes being dragged since the scene was last drawn whole, drawn in the loose layer.</summary>
    private readonly HashSet<string> _loose = new(StringComparer.Ordinal);

    /// <summary>The part of the surface on screen when the scene was last drawn, without the margin.</summary>
    private GraphRect _screenNow;

    /// <summary>
    /// Whether something changed what the scene shows since it was last drawn, so that it is drawn again without the
    /// margin: a hover, a drag or a choice draws no more than is seen, and the margin is taken by the next pan.
    /// </summary>
    private bool _sceneChanged = true;

    /// <summary>Whether the scene's layers hold a drawing, so that they are emptied only once.</summary>
    private bool _sceneMade;

    /// <summary>Whether the loose nodes changed since the nodes' layer was drawn, so that it leaves them out anew.</summary>
    private bool _looseGrew;

    /// <summary>
    /// The nodes whose hourglass breathes on screen, gathered since the scene was last drawn whole; while nothing else
    /// moves they are loose, so that a breath draws only them.
    /// </summary>
    private readonly HashSet<string> _breathing = new(StringComparer.Ordinal);

    /// <summary>
    /// Says whether nothing on the surface moves by itself in a way the scene's drawing cannot follow: no blast of
    /// the selection view and no fanned pile, which draw toward the middle of the screen.
    /// </summary>
    private bool SceneIsStill => _blast == BlastPhase.None && _fanOpen <= 0 && _fanShock is null;

    /// <summary>
    /// Says whether the scene is drawn again in every frame anyway (rings, a card that turns, an hourglass, a blast or
    /// a fan), when the margin around the screen would only cost each frame more.
    /// </summary>
    private bool SceneMovesByItself =>
        !SceneIsStill || _frames.IsRunning(GraphMotion.Rings) || _frames.IsRunning(GraphMotion.Cards);

    /// <summary>Gets the scene's layers, in the order they are drawn.</summary>
    private IEnumerable<LayerVisual> SceneLayersInOrder => [_linesLayer, _nodesLayer, _looseLayer, _overNodesLayer];

    /// <summary>Puts the scene's layers and the one over them on the surface, under the notes.</summary>
    private void HostScene()
    {
        foreach (var layer in SceneLayersInOrder)
        {
            layer.Transform = _sceneTransform;
            AddVisualChild(layer);
        }

        AddVisualChild(_overlayVisual);
    }

    /// <summary>Gives a layer under the notes by its place in the order.</summary>
    /// <param name="index">Its place, from 0.</param>
    /// <returns>The layer.</returns>
    private Visual SceneLayerAt(int index) => index switch
    {
        0 => _linesLayer,
        1 => _nodesLayer,
        2 => _looseLayer,
        3 => _overNodesLayer,
        _ => _overlayVisual,
    };

    /// <summary>
    /// Follows a pan, a glide, a zoom or a drag: when the scene's drawing still covers the screen at this zoom, the
    /// layers are moved and what stands over them is drawn again at once, without asking WPF for a new layout;
    /// otherwise the surface is drawn again as usual.
    /// </summary>
    private void FollowView()
    {
        if (!CanKeepScene(out var scene))
        {
            InvalidateVisual();
            return;
        }

        Drawn?.Invoke(this, EventArgs.Empty);
        PlaceComments();
        ShowScene(scene);
        using var over = _overlayVisual.RenderOpen();
        DrawOverScene(over);
    }

    /// <summary>
    /// Says whether the scene's drawing can be kept: there is one, nothing moves by itself, and it covers the screen at
    /// the zoom it was made at. A zoom under way draws the scene again in every frame, so that the lines and the icons
    /// are exactly as wide as they should be at each step.
    /// </summary>
    /// <param name="scene">The scene, when it can.</param>
    /// <returns><see langword="true"/> when it can.</returns>
    private bool CanKeepScene(out GraphScene scene)
    {
        scene = _model?.Scene!;
        return scene is not null && !GraphHidden && _sceneMade && SceneIsStill
            && _sceneWindow.Covers(_viewport.Visible(ActualWidth, ActualHeight), _viewport.Zoom);
    }

    /// <summary>Empties the scene's layers, when there is no graph or the selection view covers it.</summary>
    private void ForgetScene()
    {
        _sceneWindow.Forget();
        _loose.Clear();
        if (_sceneMade)
        {
            foreach (var layer in SceneLayersInOrder)
            {
                layer.RenderOpen().Close();
            }

            _sceneMade = false;
        }
    }

    /// <summary>
    /// Draws the whole scene into its layers when its drawing is stale or does not cover the screen, and puts the
    /// layers where the viewport has them.
    /// </summary>
    /// <param name="scene">The scene.</param>
    private void ShowScene(GraphScene scene)
    {
        var visible = _viewport.Visible(ActualWidth, ActualHeight);
        if (!SceneIsStill || !_sceneWindow.Covers(visible, _viewport.Zoom))
        {
            var margin = _sceneChanged || SceneMovesByItself ? 0 : SceneWindow.Margin;
            var region = _sceneWindow.Take(visible, _viewport.Zoom, margin);
            _screenNow = visible;
            _sceneChanged = false;
            _loose.Clear();
            _looseGrew = false;
            _breathing.Clear();
            var near = _model!.Near;
            DrawLayer(_linesLayer, dc => DrawLines(dc, scene, region, near.Edges));
            DrawLayer(_nodesLayer, dc => DrawNodes(dc, scene, region, near.Nodes, still: true));
            DrawLayer(_looseLayer, null);
            DrawLayer(_overNodesLayer, dc => DrawOverNodes(dc, region, near.Nodes));
            _sceneMade = true;
            KeepMarkCentres();
            KeepSceneMotions();
        }

        var matrix = new Matrix(_viewport.Zoom, 0, 0, _viewport.Zoom, _viewport.OffsetX, _viewport.OffsetY);
        if (_sceneTransform.Matrix != matrix)
        {
            _sceneTransform.Matrix = matrix;
        }
    }

    /// <summary>
    /// Takes nodes that a drag moved as loose, and asks for a frame that draws them; anything a drag cannot follow
    /// draws the whole scene instead.
    /// </summary>
    /// <param name="moved">What the drag moved.</param>
    private void Loosen(GraphMoved moved)
    {
        if (!_sceneMade || !SceneIsStill)
        {
            Redraw();
            return;
        }

        foreach (var key in moved.Nodes)
        {
            _looseGrew |= _loose.Add(key);
        }

        Request(GraphChange.Drag);
    }

    /// <summary>
    /// Draws again only the layers that changed (a drag, a step of the rings, a breath, a shimmer), and the nodes'
    /// layer only when it must leave out a node that was not loose before; the nodes that stand still keep their
    /// drawing.
    /// </summary>
    /// <param name="layers">The layers that changed: <see cref="GraphChange.Lines"/>, <see cref="GraphChange.Loose"/>
    /// and <see cref="GraphChange.OverNodes"/>.</param>
    /// <returns>
    /// <see langword="true"/> when the frame was drawn so; <see langword="false"/> when the whole scene must be
    /// drawn instead (it does not cover the screen, or a loose node overlaps a still node drawn after it).
    /// </returns>
    private bool DrawAroundStillNodes(GraphChange layers)
    {
        if (!CanKeepScene(out var scene) || _sceneWindow.Region is not { } region
            || (_loose.Count > 0 && LooseUnderStill(scene)))
        {
            return false;
        }

        // What is near what is chosen is worked out once for the frame; a drag or a breath never changes it.
        var near = _model!.Near;
        if (_looseGrew)
        {
            DrawLayer(_nodesLayer, dc => DrawNodes(dc, scene, region, near.Nodes, still: true));
            _looseGrew = false;
            layers |= GraphChange.Loose;
        }

        if (layers.HasFlag(GraphChange.Lines))
        {
            DrawLayer(_linesLayer, dc => DrawLines(dc, scene, region, near.Edges));
            KeepMarkCentres();
        }

        if (layers.HasFlag(GraphChange.Loose))
        {
            DrawLayer(_looseLayer, dc => DrawNodes(dc, scene, region, near.Nodes, still: false));
        }

        if (layers.HasFlag(GraphChange.OverNodes))
        {
            DrawLayer(_overNodesLayer, dc => DrawOverNodes(dc, region, near.Nodes));
        }

        KeepSceneMotions();
        return true;
    }

    /// <summary>
    /// Moves the cards and the hourglasses one step: a card that turns changes the nodes too, so it draws the whole
    /// scene; a card's shimmer draws only what stands over the nodes, and a breath only the breathing nodes, made
    /// loose for it.
    /// </summary>
    /// <param name="seconds">The time since the last step; the motions follow the clock.</param>
    /// <returns>What changed.</returns>
    private GraphChange CardsStep(double seconds)
    {
        if (_turns.Count > 0)
        {
            return GraphChange.Scene;
        }

        var change = _cardsMove ? GraphChange.OverNodes : GraphChange.None;
        if (_breathing.Count > 0)
        {
            foreach (var key in _breathing)
            {
                _looseGrew |= _loose.Add(key);
            }

            change |= GraphChange.Loose;
        }

        return change;
    }

    /// <summary>
    /// Says whether a loose node overlaps a still node that comes after it in the drawing order, which the loose layer
    /// would cover though the still node lies on top of it in the whole drawing.
    /// </summary>
    /// <param name="scene">The scene.</param>
    /// <returns><see langword="true"/> when one does.</returns>
    private bool LooseUnderStill(GraphScene scene)
    {
        var places = _model!.Places;
        var looseFrames = new List<GraphRect>();
        foreach (var node in scene.Nodes)
        {
            if (!places.TryGetValue(node.Key, out var place))
            {
                continue;
            }

            var frame = scene.FrameOf(node.Key, place).Inflate(GraphSizes.MarkReach);
            if (_loose.Contains(node.Key))
            {
                looseFrames.Add(frame);
            }
            else if (looseFrames.Exists(loose => loose.Intersects(frame)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Draws a layer anew, or empties it.</summary>
    /// <param name="layer">The layer.</param>
    /// <param name="draw">What to draw, or <see langword="null"/> to empty it.</param>
    private static void DrawLayer(LayerVisual layer, Action<DrawingContext>? draw)
    {
        using var dc = layer.RenderOpen();
        draw?.Invoke(dc);
    }

    /// <summary>
    /// Keeps the frames of what the scene drew that moves by itself running: the rings, the cards and the
    /// hourglasses; drawing never starts the frames itself.
    /// </summary>
    private void KeepSceneMotions()
    {
        KeepRings(_ringsOnScreen && _blast == BlastPhase.None);
        KeepTurning(_cardsMove || _breathing.Count > 0);
    }

    /// <summary>
    /// Draws the lines' layer: the hop columns, the time bands, the chains' headings, the areas, the notes' threads,
    /// the watches' glows, the trash's stubs, the lines, their pills and amounts, and the watches' marks and lines.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="visible">The part of the surface to draw: the screen and the margin around it.</param>
    /// <param name="nearEdges">The lines near what is chosen.</param>
    private void DrawLines(DrawingContext dc, GraphScene scene, GraphRect visible, IReadOnlySet<string> nearEdges)
    {
        var model = _model!;
        _ringsOnScreen = false;
        _nextMarkCentres.Clear();
        dc.PushTransform(FanPush());
        dc.PushOpacity(HeldBackMotion.LineOpacity(_blast, BlastK));
        if (model.IsHopColumns)
        {
            DrawLanes(dc, scene, visible);
        }

        DrawTimeBands(dc, visible);
        DrawChainHeadings(dc, scene, visible);
        DrawAreas(dc, visible);
        DrawCommentThreads(dc, visible);
        var edges = GraphHitTest.VisibleEdges(scene, model.Curves, visible).ToList();
        DrawWatchGlows(dc, visible);
        DrawTrashStubs(dc, visible);
        foreach (var edge in edges)
        {
            DrawEdge(dc, edge, model.Curves[edge.Key], nearEdges);
        }

        var taken = new RoomGrid();
        foreach (var edge in edges)
        {
            DrawSwap(dc, edge, model.Curves[edge.Key], nearEdges, taken);
        }

        model.BeginAmounts();
        foreach (var edge in AmountOrder(edges, nearEdges))
        {
            var faded = PushFade(dc, LaterAmountOpacity(edge.Key));
            DrawAmount(dc, edge, model.Curves[edge.Key], nearEdges, taken);
            PopFade(dc, faded);
        }

        DrawWatchMarks(dc, visible);
        dc.Pop();
        dc.Pop();
    }

    /// <summary>Draws the nodes that stand still, or the loose ones, in the drawing order.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="visible">The part of the surface to draw: the screen and the margin around it.</param>
    /// <param name="nearNodes">The nodes near what is chosen.</param>
    /// <param name="still">Whether to draw the nodes that stand still rather than the loose ones.</param>
    private void DrawNodes(
        DrawingContext dc, GraphScene scene, GraphRect visible, IReadOnlySet<string> nearNodes, bool still)
    {
        var model = _model!;
        var nodes = _blast == BlastPhase.None
            ? GraphHitTest.VisibleNodes(scene, model.Places, visible)
            : scene.Nodes.Where(node => model.Places.ContainsKey(node.Key));
        dc.PushTransform(FanPush());
        foreach (var node in nodes.Where(node => _loose.Contains(node.Key) != still))
        {
            DrawFlying(dc, node, model.Places[node.Key], nearNodes);
        }

        dc.Pop();
    }

    /// <summary>
    /// Draws the layer over the nodes: the cards, the trash's warning, the candidates, the areas' chips, the start
    /// guide, the fan and the found node's ring.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="visible">The part of the surface to draw: the screen and the margin around it.</param>
    /// <param name="nearNodes">The nodes near what is chosen.</param>
    private void DrawOverNodes(DrawingContext dc, GraphRect visible, IReadOnlySet<string> nearNodes)
    {
        dc.PushTransform(FanPush());
        DrawCards(dc, nearNodes);
        DrawTrashPending(dc);
        dc.PushOpacity(HeldBackMotion.LineOpacity(_blast, BlastK));
        DrawCandidates(dc, visible);
        DrawAreaChips(dc, visible);
        DrawGuide(dc);
        dc.Pop();
        dc.Pop();
        DrawFan(dc, visible);
        DrawFound(dc);
    }

    /// <summary>
    /// Says whether a part of the surface is on screen, not only in the margin, so that something that moves by
    /// itself keeps the frames running only while it is seen.
    /// </summary>
    /// <param name="area">The part of the surface.</param>
    /// <returns><see langword="true"/> when it is on screen.</returns>
    private bool OnScreenNow(GraphRect area) => area.Intersects(_screenNow);

    /// <summary>Notes that moving rings were drawn over a part of the surface, if that part is on screen.</summary>
    /// <param name="area">Where the rings reach.</param>
    private void RingsAt(GraphRect area) => _ringsOnScreen |= OnScreenNow(area);

    /// <summary>Draws what stands on the screen over the scene, in screen units.</summary>
    /// <param name="dc">Where to draw.</param>
    private void DrawOverScene(DrawingContext dc)
    {
        DrawTimeNames(dc);
        DrawBox(dc);
        DrawBlockMarkBox(dc);
        DrawHeldBack(dc);
    }
}

/// <summary>
/// A layer of the graph's surface that the pointer passes through: the surface finds what is under the pointer from
/// the view model, so WPF need not test every line and frame of the scene on each move of the mouse (principle 6).
/// </summary>
internal sealed class LayerVisual : DrawingVisual
{
    /// <summary>Takes no point, so that the surface's own ground under the layer takes it.</summary>
    /// <param name="hitTestParameters">The point.</param>
    /// <returns>Nothing.</returns>
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;

    /// <summary>Takes no geometry, for the same reason.</summary>
    /// <param name="hitTestParameters">The geometry.</param>
    /// <returns>Nothing.</returns>
    protected override GeometryHitTestResult? HitTestCore(GeometryHitTestParameters hitTestParameters) => null;
}
