using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>
/// The pile (sketch EX2b): drawn as a deck of cards, and fanned out as tiles in a ring around it over the dimmed graph,
/// with the same circular shock and centring as the start page's piles (<see cref="FanShock"/>, sketch GR8a): two rings
/// leave the pile while the surface glides it to the middle, and it glides back when the pile folds, unless the user
/// moved the surface meanwhile. A click chooses a tile, Shift-click more, and a tile dragged out onto the dimmed graph
/// is picked out where it is dropped, with the other chosen under it. A click on the pile or the dimmed graph, or Esc,
/// folds it again.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>How far the pointer must move, in screen units, before a press on a tile becomes a drag.</summary>
    private const double CardDragDistance = 6;

    /// <summary>How much the graph dims behind a fanned pile (sketch EX2b: to 30 %).</summary>
    private const double FanDim = 0.7;

    /// <summary>A tile's corner radius.</summary>
    private const double TileRadius = 9;

    /// <summary>The frame round a tile's icon.</summary>
    private const double TileIconBox = 22;

    /// <summary>The icon in a tile's frame.</summary>
    private const double TileIconSize = 12;

    /// <summary>How far each card behind the deck's front lies (sketch EX2b: 5 and 10).</summary>
    private const double DeckStep = 5;

    /// <summary>How far the dragged tile is turned, in degrees (sketch EX2b: −2°).</summary>
    private const double DragTilt = -2;

    /// <summary>The red dot of a negative label on a tile.</summary>
    private const double NegativeDot = 8;

    /// <summary>The tile pressed in the fan, or <see langword="null"/>.</summary>
    private string? _cardPressed;

    /// <summary>Where on screen the tile was pressed.</summary>
    private GraphPoint _cardPressAt;

    /// <summary>
    /// Where on the surface a dragged tile is now, or <see langword="null"/> while it is not dragged.
    /// </summary>
    private GraphPoint? _cardDraggedTo;

    /// <summary>Whether the press under way is on the dimmed graph or the pile.</summary>
    private bool _fanBackgroundPressed;

    /// <summary>How open the pile is, from 0 (folded) to 1 (fanned out).</summary>
    private double _fanOpen;

    /// <summary>The fan drawn, kept while it folds after the model let it go.</summary>
    private PileFanViewModel? _fanShown;

    /// <summary>When the pile began to open or fold, and from how open.</summary>
    private (DateTime Start, double From, double To)? _fanMove;

    /// <summary>When the shock's rings left the pile, or <see langword="null"/> while none move.</summary>
    private DateTime? _fanShock;

    /// <summary>The surface's glide that centres the pile, or brings it home: when it began and which way.</summary>
    private (DateTime Start, bool Home)? _fanGlide;

    /// <summary>The viewport before the pile was centred, to go back to when it folds; null when there is none.</summary>
    private Viewport? _fanHome;

    /// <summary>The viewport the centring last set, so that a pan or a zoom by the user stops it.</summary>
    private Viewport? _fanSet;

    /// <summary>The fanned pile's centre on the surface, which the centring brings to the middle.</summary>
    private GraphPoint _fanPile;

    /// <summary>Takes a press while a pile is fanned out: on a tile, on the pile, or on the dimmed graph.</summary>
    /// <param name="at">The press on screen.</param>
    /// <param name="surface">The press on the surface.</param>
    /// <returns><see langword="true"/> when the fan took it.</returns>
    private bool PressFan(GraphPoint at, GraphPoint surface)
    {
        if (_model?.Fan is not { } fan)
        {
            return false;
        }

        _cardPressed = fan.CardAt(surface);
        _cardPressAt = at;
        _cardDraggedTo = null;
        _fanBackgroundPressed = _cardPressed is null;
        CaptureMouse();
        return true;
    }

    /// <summary>Moves a pressed tile, once the pointer has gone far enough to make it a drag.</summary>
    /// <param name="at">The pointer on screen.</param>
    /// <param name="surface">The pointer on the surface.</param>
    /// <returns><see langword="true"/> when the fan took the move.</returns>
    private bool MoveFan(GraphPoint at, GraphPoint surface)
    {
        if (_model?.Fan is not { } fan)
        {
            return false;
        }

        if (_cardPressed is null)
        {
            // While the pile is fanned out, the graph under it is dimmed and takes no hovering.
            Cursor = fan.CardAt(surface) is null && !OnPile(fan, surface) ? null : Cursors.Hand;
            return true;
        }

        if (_cardDraggedTo is null && at.DistanceTo(_cardPressAt) < CardDragDistance)
        {
            return true;
        }

        _cardDraggedTo = surface;
        Cursor = Cursors.SizeAll;
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Lets go in the fan: a dragged tile is picked out where it lands, unless it lands back on the pile; a click on a
    /// tile chooses it (Shift adds); a click on the pile or the dimmed graph folds it.
    /// </summary>
    /// <param name="surface">Where it was let go, on the surface.</param>
    /// <returns><see langword="true"/> when the fan took it.</returns>
    private bool ReleaseFan(GraphPoint surface)
    {
        if (_model?.Fan is not { } fan || (_cardPressed is null && !_fanBackgroundPressed))
        {
            return false;
        }

        var card = _cardPressed;
        var dragged = _cardDraggedTo is not null;
        var background = _fanBackgroundPressed;
        ForgetFanPress();
        ReleaseMouseCapture();
        Cursor = null;
        if (card is not null && dragged && !OnPile(fan, surface))
        {
            var grip = new GraphPoint(
                surface.X - (GraphSizes.NodeWidth / 2), surface.Y - (GraphSizes.NodeHeight / 2));
            _ = _model.PickFromPileAsync(fan.Taking(card), grip);
        }
        else if (card is not null && !dragged)
        {
            fan.Click(card, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        }
        else if (background)
        {
            fan.Close();
        }

        InvalidateVisual();
        return true;
    }

    /// <summary>Forgets a press in the fan, when the capture is lost before it is let go.</summary>
    private void ForgetFanPress()
    {
        _cardPressed = null;
        _cardDraggedTo = null;
        _fanBackgroundPressed = false;
    }

    /// <summary>The fan's keys and the selection view's: Esc folds or closes, Enter picks out, L lassoes.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the fan or the view took it.</returns>
    private bool FanKey(Key key)
    {
        if (HeldBackKey(key))
        {
            return true;
        }

        if (_model?.Fan is not { } fan)
        {
            return false;
        }

        switch (key)
        {
            case Key.Escape:
                fan.Close();
                return true;
            case Key.Enter:
                fan.PickSelected();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Says whether a point lies on the fanned pile's own deck.</summary>
    /// <param name="fan">The fan.</param>
    /// <param name="surface">The point.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private bool OnPile(PileFanViewModel fan, GraphPoint surface)
    {
        if (!_model!.Places.TryGetValue(fan.Pile.Key, out var place))
        {
            return false;
        }

        var frame = GraphSizes.NodeFrame(place);
        var deck = new GraphRect(frame.X, frame.Y, frame.Width + (2 * DeckStep), frame.Height + (2 * DeckStep));
        return deck.Contains(surface);
    }

    /// <summary>
    /// Follows the model's fan: opening starts the ring's motion from the pile, folding plays it back with the last
    /// fan drawn. Without animations it opens and folds at once.
    /// </summary>
    private void FollowFanMotion()
    {
        var fan = _model?.Fan;
        var target = fan is null ? 0.0 : 1.0;
        if (fan is not null)
        {
            _fanShown = fan;
        }

        if (_fanMove is { } moving ? moving.To == target : _fanOpen == target)
        {
            return;
        }

        if (fan is null)
        {
            BeginFanHome();
        }
        else
        {
            BeginFanCentring(fan);
        }

        if (!AnimationsOn)
        {
            _fanOpen = target;
            _fanMove = null;
            InvalidateVisual();
            return;
        }

        _fanMove = (DateTime.UtcNow, _fanOpen, target);
        KeepMoving();
    }

    /// <summary>Moves the pile's opening, its shock and the surface's glide one frame on.</summary>
    /// <returns><see langword="true"/> while something still moves.</returns>
    private bool StepFan()
    {
        var gliding = StepFanGlide();
        if (_fanShock is { } shock && DateTime.UtcNow - shock >= FanShock.Length)
        {
            _fanShock = null;
        }

        var shaking = _fanShock is not null;
        if (_fanMove is not { } move)
        {
            return gliding || shaking;
        }

        var t = HeldBackMotion.Unit((DateTime.UtcNow - move.Start) / HeldBackMotion.FanTime);
        _fanOpen = move.From + ((move.To - move.From) * HeldBackMotion.EaseInOut(t));
        if (t < 1)
        {
            return true;
        }

        _fanMove = null;
        if (move.To == 0)
        {
            _fanShown = null;
        }

        return gliding || shaking;
    }

    /// <summary>
    /// Starts the shock from a pile that fans out and the surface's glide that brings it to the middle; without
    /// animations the pile stands in the middle at once and no rings go.
    /// </summary>
    /// <param name="fan">The fan.</param>
    private void BeginFanCentring(PileFanViewModel fan)
    {
        StopGlide();
        StopZoom();
        _fanPile = fan.Centre;
        _fanHome = _viewport;
        if (!AnimationsOn)
        {
            SetFanViewport(FanShock.Centring(_fanHome, _fanPile, ScreenMiddle, 1), stored: true);
            return;
        }

        _fanShock = DateTime.UtcNow;
        _fanGlide = (DateTime.UtcNow, false);
        _fanSet = _viewport;
    }

    /// <summary>
    /// Brings the surface back to where it was before the pile was centred, as the pile folds; not when the user
    /// panned or zoomed meanwhile, since the surface is then where the user wants it.
    /// </summary>
    private void BeginFanHome()
    {
        _fanShock = null;
        if (_fanHome is null || _viewport != _fanSet)
        {
            ForgetFanCentring();
            return;
        }

        if (!AnimationsOn)
        {
            SetFanViewport(_fanHome, stored: true);
            ForgetFanCentring();
            return;
        }

        // A pile folded before it reached the middle goes home from where it is: the ease is symmetric, so starting the
        // way home as far along as the way out has left to go keeps the surface still.
        var now = DateTime.UtcNow;
        var reached = _fanGlide is { Home: false } outward
            ? HeldBackMotion.Unit((now - outward.Start) / FanShock.GlideTime)
            : 1;
        _fanGlide = (now - (HeldBackMotion.FanTime * (1 - reached)), true);
    }

    /// <summary>
    /// Moves the surface's glide one frame on: towards the middle as the pile opens, home as it folds. A pan or a zoom
    /// by the user stops it where it is.
    /// </summary>
    /// <returns><see langword="true"/> while it still moves.</returns>
    private bool StepFanGlide()
    {
        if (_fanGlide is not { } glide || _fanHome is not { } home)
        {
            return false;
        }

        if (_viewport != _fanSet)
        {
            _fanGlide = null;
            return false;
        }

        var length = glide.Home ? HeldBackMotion.FanTime : FanShock.GlideTime;
        var share = HeldBackMotion.Unit((DateTime.UtcNow - glide.Start) / length);
        var done = share >= 1;
        SetFanViewport(FanShock.Centring(home, _fanPile, ScreenMiddle, glide.Home ? 1 - share : share), done);
        if (!done)
        {
            return true;
        }

        _fanGlide = null;
        if (glide.Home)
        {
            ForgetFanCentring();
        }

        return false;
    }

    /// <summary>Puts the surface where the centring has it, and stores the place once it has landed.</summary>
    /// <param name="viewport">The viewport.</param>
    /// <param name="stored">Whether it has landed, so that the case stores it.</param>
    private void SetFanViewport(Viewport viewport, bool stored)
    {
        _viewport = viewport;
        _fanSet = viewport;
        if (stored)
        {
            _model?.ViewportChanged(_viewport);
        }

        InvalidateVisual();
    }

    /// <summary>Forgets where the surface was before the pile was centred.</summary>
    private void ForgetFanCentring()
    {
        _fanGlide = null;
        _fanHome = null;
        _fanSet = null;
    }

    /// <summary>Gets the middle of the surface on screen, where a fanned pile is centred.</summary>
    private GraphPoint ScreenMiddle => new(ActualWidth / 2, ActualHeight / 2);

    /// <summary>
    /// Draws the shock's two rings from the fanned pile (<see cref="FanShock"/>), in screen pixels whatever the zoom,
    /// over the dimmed graph.
    /// </summary>
    /// <param name="dc">Where to draw, in the surface's units.</param>
    private void DrawFanShock(DrawingContext dc)
    {
        if (_fanShock is not { } start)
        {
            return;
        }

        var elapsed = DateTime.UtcNow - start;
        var zoom = _viewport.Zoom;
        var pen = PenOf(GraphPalette.Find(this, "Brush.BorderHover"), 1.5 / zoom);
        var centre = new Point(_fanPile.X, _fanPile.Y);
        for (var index = 0; index < FanShock.RingCount; index++)
        {
            if (FanShock.Ring(index, elapsed) is not { } ring)
            {
                continue;
            }

            dc.PushOpacity(ring.Opacity);
            dc.DrawEllipse(null, pen, centre, ring.Radius / zoom, ring.Radius / zoom);
            dc.Pop();
        }
    }

    /// <summary>
    /// Draws the fan over the graph: the dimmed ground, the pile again on top, and its tiles flying out to their ring,
    /// with the tiles being dragged at the pointer.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="visible">The visible part of the surface.</param>
    private void DrawFan(DrawingContext dc, GraphRect visible)
    {
        if (_fanOpen <= 0 || _fanShown is not { } fan || _model?.Scene?.Node(fan.Pile.Key) is not { } pile
            || !_model.Places.TryGetValue(pile.Key, out var place))
        {
            return;
        }

        dc.PushOpacity(FanDim * _fanOpen);
        dc.DrawRectangle(
            GraphPalette.Find(this, "Brush.Background"),
            null,
            new Rect(visible.X, visible.Y, visible.Width, visible.Height));
        dc.Pop();
        DrawFanShock(dc);
        DrawPile(dc, pile, place, open: true);
        var lifted = _cardDraggedTo is not null && _cardPressed is { } pressed ? fan.Taking(pressed) : [];
        for (var index = 0; index < fan.Shown.Count; index++)
        {
            var card = fan.Shown[index];
            if (lifted.Contains(card.Key))
            {
                continue;
            }

            var (travel, opacity, scale) = HeldBackMotion.Tile(index, fan.Shown.Count, _fanOpen);
            var centre = HeldBackMotion.Lerp(fan.Centre, fan.Places[card.Key], travel);
            if (!PileFanLayout.TileFrame(centre).Intersects(visible))
            {
                continue;
            }

            dc.PushOpacity(opacity);
            dc.PushTransform(new ScaleTransform(scale, scale, centre.X, centre.Y));
            DrawTile(dc, card, centre, fan.Selected.Contains(card.Key));
            dc.Pop();
            dc.Pop();
        }

        if (_cardDraggedTo is { } to)
        {
            DrawDragged(dc, fan, lifted, to);
        }
    }

    /// <summary>
    /// Draws the tiles being dragged at the pointer, turned a little, the first on top; the frame is lighter where they
    /// can be dropped.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="fan">The fan.</param>
    /// <param name="lifted">The tiles dragged.</param>
    /// <param name="to">The pointer on the surface.</param>
    private void DrawDragged(DrawingContext dc, PileFanViewModel fan, IReadOnlyList<string> lifted, GraphPoint to)
    {
        dc.PushTransform(new RotateTransform(DragTilt, to.X, to.Y));
        for (var index = lifted.Count - 1; index >= 0; index--)
        {
            var card = fan.Pile.Cards.First(one => one.Key == lifted[index]);
            DrawTile(dc, card, new GraphPoint(to.X, to.Y + (index * DeckStep)), !OnPile(fan, to));
        }

        dc.Pop();
    }

    /// <summary>
    /// Draws a tile (sketch EX2b): the sort's icon in its frame, the name, and what moved between it and the pile's
    /// actor; a red dot for a negative label; the accent frame when chosen.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="card">The card.</param>
    /// <param name="centre">The tile's centre.</param>
    /// <param name="chosen">Whether it is chosen.</param>
    private void DrawTile(DrawingContext dc, PileCard card, GraphPoint centre, bool chosen)
    {
        var frame = PileFanLayout.TileFrame(centre);
        var rect = new Rect(frame.X, frame.Y, frame.Width, frame.Height);
        var edge = chosen
            ? PenOf(GraphPalette.Find(this, "Brush.Accent"), 1.5)
            : PenOf(GraphPalette.Find(this, "Brush.BorderStrong"), 1);
        dc.DrawRoundedRectangle(GraphPalette.Find(this, "Brush.SurfaceRaised"), edge, rect, TileRadius, TileRadius);
        var look = card.Node.Look;
        var iconCorner = new Point(rect.X + 7, rect.Y + ((rect.Height - TileIconBox) / 2));
        DrawSmallIcon(dc, look, GraphPalette.Brush(this, look.Ink), iconCorner, TileIconBox, TileIconSize);
        var left = iconCorner.X + TileIconBox + 8;
        var room = rect.Right - 9 - left;
        var title = card.Node.IsNamed ? card.Node.Name : card.Node.Subject;
        var name = CachedFitted(title, 12, GraphPalette.Find(this, "Brush.Text"), FontWeights.SemiBold, room);
        var line = CachedText(card.Line, 10.5, GraphPalette.Find(this, "Brush.TextDim"), FontWeights.Normal);
        var top = rect.Y + ((rect.Height - name.Height - line.Height + 2) / 2);
        dc.DrawText(name, new Point(left, top));
        dc.DrawText(line, new Point(left, top + name.Height - 2));
        if (HeldBackChoice.GroupOf(look.Sort) == CounterpartyGroup.Risk)
        {
            dc.DrawEllipse(
                GraphPalette.Find(this, "Brush.Error"),
                PenOf(GraphPalette.Find(this, "Brush.Background"), 1.5),
                new Point(rect.Right, rect.Y),
                NegativeDot / 2,
                NegativeDot / 2);
        }
    }

    /// <summary>
    /// Draws a pile as a deck of cards (sketch EX2b): two cards behind the front, the stack's icon, the count of
    /// counterparties and what moved with how many transactions; lighter while it is fanned out.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The pile's node.</param>
    /// <param name="place">Its top-left corner.</param>
    /// <param name="open">Whether it is fanned out.</param>
    private void DrawPile(DrawingContext dc, NodeView node, GraphPoint place, bool open)
    {
        var front = new Rect(place.X, place.Y, GraphSizes.NodeWidth, GraphSizes.NodeHeight);
        for (var step = 2; step >= 1; step--)
        {
            var behind = front;
            behind.Offset(step * DeckStep, step * DeckStep);
            dc.DrawRoundedRectangle(
                GraphPalette.Find(this, "Brush.Background"),
                PenOf(GraphPalette.Find(this, step == 2 ? "Brush.Border" : "Brush.BorderStrong"), 1),
                behind,
                NodeRadius,
                NodeRadius);
        }

        var chosen = _model!.SelectedNode == node.Key;
        var frameKey = open || _model.HoveredNode == node.Key ? "Brush.BorderHover" : "Brush.BorderStrong";
        dc.DrawRoundedRectangle(
            GraphPalette.Find(this, open ? "Brush.SurfaceSelected" : "Brush.Surface"),
            PenOf(GraphPalette.Find(this, chosen ? "Brush.Accent" : frameKey), chosen ? 1.5 : 1),
            front,
            NodeRadius,
            NodeRadius);
        var muted = GraphPalette.Find(this, "Brush.TextMuted");
        var iconCorner = new Point(front.X + NodePadding, front.Y + ((front.Height - IconBox) / 2));
        DrawSmallIcon(dc, node.Look, muted, iconCorner, IconBox, IconSize);
        var left = iconCorner.X + IconBox + NodeGap;
        var room = front.Right - NodePadding - left;
        var name = CachedFitted(node.Name, 12.5, GraphPalette.Find(this, "Brush.Text"), FontWeights.SemiBold, room);
        var sub = CachedFitted(node.Subject, 11, GraphPalette.Find(this, "Brush.TextDim"), FontWeights.Normal, room);
        var top = front.Y + ((front.Height - name.Height - sub.Height + 2) / 2);
        dc.DrawText(name, new Point(left, top));
        dc.DrawText(sub, new Point(left, top + name.Height - 2));
    }

    /// <summary>
    /// Draws a clue (sketch EX1): a dashed pill with a plus and the count, and under it, small, what it says about the
    /// time; centred at the top of its node's place.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The clue's node.</param>
    /// <param name="place">Its top-left corner.</param>
    private void DrawClue(DrawingContext dc, NodeView node, GraphPoint place)
    {
        const double PillHeight = 30;
        const double Plus = 12;
        const double Pad = 13;
        var hovered = _model!.HoveredNode == node.Key;
        var words = node.Name.TrimStart('+').Trim();
        var count = CachedText(words, 12.5, GraphPalette.Find(this, "Brush.Text"), FontWeights.SemiBold);
        var width = Pad + Plus + 7 + count.Width + Pad;
        var middle = place.X + (GraphSizes.NodeWidth / 2);
        var pill = new Rect(middle - (width / 2), place.Y, width, PillHeight);
        var pen = PenOf(GraphPalette.Find(this, hovered ? "Brush.Text" : "Brush.TextDim"), 1.5, FrameDash);
        dc.DrawRoundedRectangle(
            GraphPalette.Find(this, hovered ? "Brush.SurfaceRaised" : "Brush.Surface"),
            pen,
            pill,
            PillHeight / 2,
            PillHeight / 2);
        var plusCorner = new Point(pill.X + Pad, pill.Y + ((PillHeight - Plus) / 2));
        DrawGlyph(dc, "Icon.Plus", GraphPalette.Find(this, "Brush.TextMuted"), plusCorner, Plus);
        dc.DrawText(count, new Point(plusCorner.X + Plus + 7, pill.Y + ((PillHeight - count.Height) / 2)));
        if (node.Subject.Length > 0)
        {
            var line = CachedText(node.Subject, 11, GraphPalette.Find(this, "Brush.TextFaint"), FontWeights.Normal);
            dc.DrawText(line, new Point(middle - (line.Width / 2), pill.Bottom + 4));
        }
    }

    /// <summary>Draws a sort's icon in its round or square frame at a size.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="look">The sort's look.</param>
    /// <param name="ink">The colour.</param>
    /// <param name="corner">The frame's top-left corner.</param>
    /// <param name="box">The frame's size.</param>
    /// <param name="size">The icon's size.</param>
    private void DrawSmallIcon(DrawingContext dc, SortLook look, Brush ink, Point corner, double box, double size)
    {
        var radius = look.Shape == NodeShape.Round ? box / 2 : 6;
        var pen = PenOf(ink, 1.5, look.IsDashed ? FrameDash : null);
        dc.DrawRoundedRectangle(null, pen, new Rect(corner, new Size(box, box)), radius, radius);
        var offset = (box - size) / 2;
        DrawGlyph(dc, look.Icon, ink, new Point(corner.X + offset, corner.Y + offset), size);
    }
}
