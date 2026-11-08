using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The scene and what stands over it, as two layers of their own (principle 6): the scene (the hop columns, the time
/// bands, the lines, their amounts and the nodes) is drawn on the surface's units into a layer that keeps its drawing
/// while only its transform follows a pan, a glide or a zoom (<see cref="SceneWindow"/>); what stands on the screen
/// over it (the days' names, the box, the block mark's box and the selection view) is drawn again in every frame,
/// which is cheap. A pan then sends the render thread a new transform, not the whole graph again.
/// </summary>
/// <remarks>
/// The scene has its own layer rather than being drawn in <see cref="OnRender"/>: WPF lets go of everything the last
/// <see cref="OnRender"/> drew before it takes the new drawing, so a drawing kept from one frame to the next would
/// still be sent again in full. The layers lie under the comments' notes, in the order they were drawn before.
/// </remarks>
public sealed partial class GraphCanvas
{
    /// <summary>Decides when the scene's drawing can be moved instead of made again.</summary>
    private readonly SceneWindow _sceneWindow = new();

    /// <summary>The layer that holds the scene's drawing, on the surface's units.</summary>
    private readonly LayerVisual _sceneVisual = new();

    /// <summary>The layer over the scene, drawn on screen units in every frame.</summary>
    private readonly LayerVisual _overlayVisual = new();

    /// <summary>Moves the scene's layer onto the screen: changed in place, so that a pan sends only it.</summary>
    private readonly MatrixTransform _sceneTransform = new();

    /// <summary>The part of the surface on screen when the scene was last drawn, without the margin.</summary>
    private GraphRect _screenNow;

    /// <summary>
    /// Whether something changed what the scene shows since it was last drawn, so that it is drawn again without the
    /// margin: a hover, a drag or a choice draws no more than is seen, and the margin is taken by the next pan.
    /// </summary>
    private bool _sceneChanged = true;

    /// <summary>Whether the scene's layer holds a drawing, so that it is emptied only once.</summary>
    private bool _sceneMade;

    /// <summary>How many layers stand under the notes: the scene's and the one over it.</summary>
    private const int SceneLayers = 2;

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

    /// <summary>Puts the scene's layer and the one over it on the surface, under the notes.</summary>
    private void HostScene()
    {
        _sceneVisual.Transform = _sceneTransform;
        AddVisualChild(_sceneVisual);
        AddVisualChild(_overlayVisual);
    }

    /// <summary>
    /// Follows a pan, a glide or a zoom under way: when the scene's drawing still covers the screen, the layer is
    /// moved and what stands over it drawn again at once, without asking WPF for a new layout; otherwise the surface
    /// is drawn again as usual.
    /// </summary>
    private void FollowView()
    {
        if (_model?.Scene is not { } scene || GraphHidden || !_sceneMade || !SceneIsStill
            || !_sceneWindow.Covers(_viewport.Visible(ActualWidth, ActualHeight), _viewport.Zoom, IsZooming))
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

    /// <summary>Empties the scene's layer, when there is no graph or the selection view covers it.</summary>
    private void ForgetScene()
    {
        _sceneWindow.Forget();
        if (_sceneMade)
        {
            _sceneVisual.RenderOpen().Close();
            _sceneMade = false;
        }
    }

    /// <summary>
    /// Draws the scene into its layer when its drawing is stale or does not cover the screen, and puts the layer
    /// where the viewport has it.
    /// </summary>
    /// <param name="scene">The scene.</param>
    private void ShowScene(GraphScene scene)
    {
        var visible = _viewport.Visible(ActualWidth, ActualHeight);
        if (!SceneIsStill || !_sceneWindow.Covers(visible, _viewport.Zoom, IsZooming))
        {
            var margin = _sceneChanged || SceneMovesByItself ? 0 : SceneWindow.Margin;
            var region = _sceneWindow.Take(visible, _viewport.Zoom, margin);
            _screenNow = visible;
            _sceneChanged = false;
            using (var dc = _sceneVisual.RenderOpen())
            {
                DrawScene(dc, scene, region);
            }

            KeepMarkCentres();
            _sceneMade = true;

            // What the scene drew that moves by itself keeps its frames running; drawing never starts them.
            KeepRings(_ringsOnScreen && _blast == BlastPhase.None);
            KeepTurning(_cardsMove);
        }

        var matrix = new Matrix(_viewport.Zoom, 0, 0, _viewport.Zoom, _viewport.OffsetX, _viewport.OffsetY);
        if (_sceneTransform.Matrix != matrix)
        {
            _sceneTransform.Matrix = matrix;
        }
    }

    /// <summary>
    /// Draws the scene on the surface's units, over a part of it: the hop columns, the time bands, the areas, the
    /// lines with their pills and amounts, the watches, the nodes and their cards, the candidates, the fan and the
    /// found node's ring.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="scene">The scene.</param>
    /// <param name="visible">The part of the surface to draw: the screen and the margin around it.</param>
    private void DrawScene(DrawingContext dc, GraphScene scene, GraphRect visible)
    {
        var model = _model!;
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
        var (nearNodes, nearEdges) = model.Near;
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
            dc.PushOpacity(LaterAmountOpacity(edge.Key));
            DrawAmount(dc, edge, model.Curves[edge.Key], nearEdges, taken);
            dc.Pop();
        }

        _ringsOnScreen = false;
        DrawWatchMarks(dc, visible);
        dc.Pop();
        var nodes = _blast == BlastPhase.None
            ? GraphHitTest.VisibleNodes(scene, model.Places, visible)
            : scene.Nodes.Where(node => model.Places.ContainsKey(node.Key));
        foreach (var node in nodes)
        {
            DrawFlying(dc, node, model.Places[node.Key], nearNodes);
        }

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
