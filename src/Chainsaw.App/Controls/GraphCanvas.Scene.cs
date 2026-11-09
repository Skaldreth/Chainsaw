using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The scene and what stands over it, as layers of their own, as a game engine keeps its world (principle 6). The
/// scene is drawn on the surface's units in three layers, in the order it was always drawn: the lines' layer (the hop
/// columns, the time bands, the chains' headings, the areas, the notes' threads, the lines with their pills, amounts
/// and watches), the nodes' layer, where each node is a visual of its own in the drawing order, and the layer over the
/// nodes (the cards, the trash's warning, the candidates, the areas' chips, the start guide, the fan and the found
/// node's ring). Their drawing is kept while only their transform follows a pan or a glide (<see cref="SceneWindow"/>);
/// what stands on the screen over them (the days' names, the box, the block mark's box and the selection view) is
/// drawn again in every frame.
/// </summary>
/// <remarks>
/// <para>
/// The layers are their own visuals rather than drawn in <see cref="OnRender"/>: WPF lets go of everything the last
/// <see cref="OnRender"/> drew before it takes the new drawing, so a drawing kept from one frame to the next would
/// still be sent again in full. They lie under the comments' notes.
/// </para>
/// <para>
/// A change names the layers it touches (<see cref="GraphChange"/>), and a change of a few nodes names them: a drag
/// (<see cref="ViewModels.GraphViewModel.Moved"/>), a hover or a breathing hourglass draws again only those nodes'
/// visuals, which keep their place in the order, and every frame shows exactly what a whole drawing would.
/// </para>
/// </remarks>
public sealed partial class GraphCanvas
{
    /// <summary>How many layers stand under the notes: the scene's three and the one over them.</summary>
    private const int SceneLayers = 4;

    /// <summary>Decides when the scene's drawing can be moved instead of made again.</summary>
    private readonly SceneWindow _sceneWindow = new();

    /// <summary>The lines' layer: the hop columns, the time bands, the areas, the lines, their amounts and watches.</summary>
    private readonly LayerVisual _linesLayer = new();

    /// <summary>The nodes' layer: a visual for each node on the part drawn, in the drawing order.</summary>
    private readonly NodesVisual _nodesLayer = new();

    /// <summary>The layer over the nodes: the cards, the candidates, the chips, the guide, the fan and the found ring.</summary>
    private readonly LayerVisual _overNodesLayer = new();

    /// <summary>The layer over the scene, drawn on screen units in every frame.</summary>
    private readonly LayerVisual _overlayVisual = new();

    /// <summary>Moves the scene's layers onto the screen: changed in place, so that a pan sends only it.</summary>
    private readonly MatrixTransform _sceneTransform = new();

    /// <summary>The nodes asked to be drawn again alone since the last frame (dragged, hovered, breathing).</summary>
    private readonly HashSet<string> _touchedNodes = new(StringComparer.Ordinal);

    /// <summary>
    /// The nodes whose hourglass breathes on screen, gathered since the scene was last drawn whole, so that a breath
    /// draws only them.
    /// </summary>
    private readonly HashSet<string> _breathing = new(StringComparer.Ordinal);

    /// <summary>Each drawn node's visual, by the node's key.</summary>
    private Dictionary<string, LayerVisual> _nodeVisuals = new(StringComparer.Ordinal);

    /// <summary>The part of the surface on screen when the scene was last drawn, without the margin.</summary>
    private GraphRect _screenNow;

    /// <summary>
    /// Whether something changed what the scene shows since it was last drawn, so that it is drawn again without the
    /// margin: a hover, a drag or a choice draws no more than is seen, and the margin is taken by the next pan.
    /// </summary>
    private bool _sceneChanged = true;

    /// <summary>Whether the scene's layers hold a drawing, so that they are emptied only once.</summary>
    private bool _sceneMade;

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

    /// <summary>Puts the scene's layers and the one over them on the surface, under the notes.</summary>
    private void HostScene()
    {
        foreach (var layer in new ContainerVisual[] { _linesLayer, _nodesLayer, _overNodesLayer })
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
        2 => _overNodesLayer,
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
        _touchedNodes.Clear();
        if (_sceneMade)
        {
            DrawLayer(_linesLayer, null);
            _nodesLayer.Children.Clear();
            _nodeVisuals = new Dictionary<string, LayerVisual>(StringComparer.Ordinal);
            DrawLayer(_overNodesLayer, null);
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
            // While a zoom is under way every frame draws the scene anyway, so no more than the screen is drawn.
            var margin = _sceneChanged || IsZooming || SceneMovesByItself ? 0 : SceneWindow.Margin;
            var region = _sceneWindow.Take(visible, _viewport.Zoom, margin);
            _screenNow = visible;
            _sceneChanged = false;
            _touchedNodes.Clear();
            _breathing.Clear();
            SettleTurns();
            var near = _model!.Near;
            DrawLayer(_linesLayer, dc => DrawLines(dc, scene, region, near.Edges));
            DrawNodes(scene, region, near.Nodes);
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
    /// Asks for the given nodes to be drawn again alone in the next frame, with the layers named; anything that cannot
    /// be followed so draws the whole scene instead.
    /// </summary>
    /// <param name="nodes">The nodes.</param>
    /// <param name="layers">The other layers that changed with them.</param>
    private void Touch(IEnumerable<string> nodes, GraphChange layers)
    {
        if (!_sceneMade || !SceneIsStill)
        {
            Redraw();
            return;
        }

        _touchedNodes.UnionWith(nodes);
        Request(layers | GraphChange.Nodes);
    }

    /// <summary>
    /// Draws again only the layers that changed (a drag, a hover, a step of the rings, a breath, a shimmer), and of the
    /// nodes only those touched; every other node keeps its drawing and its place in the order.
    /// </summary>
    /// <param name="layers">
    /// The layers that changed: <see cref="GraphChange.Lines"/>, <see cref="GraphChange.Nodes"/> and
    /// <see cref="GraphChange.OverNodes"/>.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the frame was drawn so; <see langword="false"/> when the whole scene must be
    /// drawn instead (it does not cover the screen, or a touched node has no visual yet, as one dragged in from the
    /// edge).
    /// </returns>
    private bool DrawAroundStillNodes(GraphChange layers)
    {
        if (!CanKeepScene(out var scene) || _sceneWindow.Region is not { } region
            || (layers.HasFlag(GraphChange.Nodes) && !TouchedAreDrawn(scene, region))
            || (layers.HasFlag(GraphChange.OverNodes) && TurnEnded))
        {
            return false;
        }

        // What is near what is chosen is worked out once for the frame; a drag, a hover or a breath never changes it.
        var near = _model!.Near;
        if (layers.HasFlag(GraphChange.Lines))
        {
            DrawLayer(_linesLayer, dc => DrawLines(dc, scene, region, near.Edges));
            KeepMarkCentres();
        }

        if (layers.HasFlag(GraphChange.Nodes))
        {
            foreach (var key in _touchedNodes)
            {
                if (_nodeVisuals.TryGetValue(key, out var visual) && scene.Node(key) is { } node)
                {
                    DrawLayer(visual, dc => DrawOneNode(dc, node, near.Nodes));
                }
            }

            _touchedNodes.Clear();
        }

        if (layers.HasFlag(GraphChange.OverNodes))
        {
            DrawLayer(_overNodesLayer, dc => DrawOverNodes(dc, region, near.Nodes));
        }

        KeepSceneMotions();
        return true;
    }

    /// <summary>
    /// Says whether every touched node that reaches into the part drawn has a visual, so that drawing them alone shows
    /// all of them; a node that came into the part (dragged in from the edge) needs its place in the order made.
    /// </summary>
    /// <param name="scene">The scene.</param>
    /// <param name="region">The part of the surface drawn.</param>
    /// <returns><see langword="true"/> when they all have one.</returns>
    private bool TouchedAreDrawn(GraphScene scene, GraphRect region)
    {
        var places = _model!.Places;
        foreach (var key in _touchedNodes)
        {
            if (!_nodeVisuals.ContainsKey(key) && places.TryGetValue(key, out var place)
                && scene.FrameOf(key, place).Inflate(GraphSizes.MarkReach).Intersects(region))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Moves the cards and the hourglasses one step: a card that turns changes the nodes too, so it draws the whole
    /// scene; a card's shimmer draws only what stands over the nodes, and a breath only the breathing nodes.
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
            _touchedNodes.UnionWith(_breathing);
            change |= GraphChange.Nodes;
        }

        return change;
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
        KeepRings((_ringsOnScreen || _ringsOverNodes) && _blast == BlastPhase.None);
        // A turn keeps the frames running until a whole drawing has settled it, even one that ended while drawing.
        KeepTurning(_cardsMove || _turns.Count > 0 || _breathing.Count > 0);
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
        var edges = model.Index?.VisibleEdges(visible) ?? [];
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

    /// <summary>
    /// Draws every node on the part of the surface into a visual of its own, in the drawing order, so that a node that
    /// changes alone (hovered, dragged, breathing) is drawn again alone.
    /// </summary>
    /// <param name="scene">The scene.</param>
    /// <param name="visible">The part of the surface to draw: the screen and the margin around it.</param>
    /// <param name="nearNodes">The nodes near what is chosen.</param>
    private void DrawNodes(GraphScene scene, GraphRect visible, IReadOnlySet<string> nearNodes)
    {
        var model = _model!;
        var nodes = _blast == BlastPhase.None
            ? model.Index?.VisibleNodes(visible) ?? []
            : scene.Nodes.Where(node => model.Places.ContainsKey(node.Key));
        var kept = new Dictionary<string, LayerVisual>(StringComparer.Ordinal);
        var order = new List<LayerVisual>();
        foreach (var node in nodes)
        {
            var visual = _nodeVisuals.GetValueOrDefault(node.Key) ?? new LayerVisual();
            DrawLayer(visual, dc => DrawOneNode(dc, node, nearNodes));
            order.Add(visual);
            kept[node.Key] = visual;
        }

        // The layer's children are put in again only when the nodes drawn or their order changed.
        var children = _nodesLayer.Children;
        if (children.Count != order.Count || order.Where((visual, at) => !ReferenceEquals(children[at], visual)).Any())
        {
            children.Clear();
            order.ForEach(visual => children.Add(visual));
        }

        _nodeVisuals = kept;
    }

    /// <summary>Draws a node, as the whole drawing places it, into the visual it was given.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The node.</param>
    /// <param name="nearNodes">The nodes near what is chosen.</param>
    private void DrawOneNode(DrawingContext dc, NodeView node, IReadOnlySet<string> nearNodes)
    {
        // A node that lost its place is drawn nowhere, as the whole drawing would leave it out.
        if (_model!.Places.TryGetValue(node.Key, out var place))
        {
            dc.PushTransform(FanPush());
            DrawFlying(dc, node, place, nearNodes);
            dc.Pop();
        }
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
        _drawingOverNodes = true;
        _ringsOverNodes = false;
        try
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
        finally
        {
            _drawingOverNodes = false;
        }
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
    private void RingsAt(GraphRect area)
    {
        // Each layer keeps its own note, since either may be drawn again without the other.
        if (_drawingOverNodes)
        {
            _ringsOverNodes |= OnScreenNow(area);
        }
        else
        {
            _ringsOnScreen |= OnScreenNow(area);
        }
    }

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

/// <summary>
/// The nodes' layer: a visual of its own for each node, in the drawing order, that the pointer passes through like
/// the other layers (<see cref="LayerVisual"/>).
/// </summary>
internal sealed class NodesVisual : ContainerVisual
{
    /// <summary>Takes no point; the surface finds what is under the pointer from the view model.</summary>
    /// <param name="hitTestParameters">The point.</param>
    /// <returns>Nothing.</returns>
    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters) => null;

    /// <summary>Takes no geometry, for the same reason.</summary>
    /// <param name="hitTestParameters">The geometry.</param>
    /// <returns>Nothing.</returns>
    protected override GeometryHitTestResult? HitTestCore(GeometryHitTestParameters hitTestParameters) => null;
}
