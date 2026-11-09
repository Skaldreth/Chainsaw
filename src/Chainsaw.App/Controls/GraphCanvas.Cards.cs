using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>
/// The card's back (sketch IN4a): Alt + click turns a node round its upright axis, and its back grows to a row per
/// asset that is left, with the value under it, the rate's line and the block at its foot. Several cards can stand
/// turned at once, over the nodes around them; Alt + click again, or the turn in the corner, turns one back. The turn
/// stands still when Windows has animations turned off. A node whose lookup waits for its source gets an hourglass in
/// its lower corner (sketch GR9a).
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>How long a turn takes (sketch IN4a: 0.6 s).</summary>
    private static readonly TimeSpan TurnTime = TimeSpan.FromMilliseconds(600);

    /// <summary>The back's corner radius.</summary>
    private const double CardRadius = 12;

    /// <summary>The space inside the back, left and right.</summary>
    private const double CardPadding = 12;

    /// <summary>An asset's ring.</summary>
    private const double AssetRing = 26;

    /// <summary>The turn back in the head's corner, as a square that takes the pointer.</summary>
    private const double TurnBackBox = 24;

    /// <summary>The cards turned when the graph was last drawn, so that a new one or one gone starts its turn.</summary>
    private readonly HashSet<string> _shownTurned = new(StringComparer.Ordinal);

    /// <summary>The turns under way, by node: when each began, and whether it turns to the back.</summary>
    private readonly Dictionary<string, (DateTime Start, bool ToBack)> _turns = new(StringComparer.Ordinal);

    /// <summary>Whether a card turned or something on a card was fetched in the scene drawn last.</summary>
    private bool _cardsMove;

    /// <summary>Whether a node's front is being drawn by the card's turn, so that <see cref="DrawNode"/> draws it.</summary>
    private bool _drawingFace;

    /// <summary>The card under the pointer, whose turn back is shown.</summary>
    private string? _hoveredCard;

    /// <summary>Whether the pointer is on the hovered card's turn back.</summary>
    private bool _onTurnBack;

    /// <summary>
    /// Says whether a node is left to the card's pass: it stands turned or is turning, and is drawn over the others.
    /// </summary>
    /// <param name="key">The node.</param>
    /// <returns><see langword="true"/> when <see cref="DrawNode"/> should leave it.</returns>
    private bool LeftToCard(string key) =>
        !_drawingFace && (_model!.IsTurned(key) || _turns.ContainsKey(key));

    /// <summary>
    /// Draws the turned cards and the turns under way over the nodes, in surface units, and keeps the frames running
    /// while something moves.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="near">The nodes near what is chosen.</param>
    private void DrawCards(DrawingContext dc, IReadOnlySet<string> near)
    {
        var model = _model!;
        StartTurns(model);
        var moving = false;
        foreach (var key in _shownTurned.Concat(_turns.Keys).Distinct().ToList())
        {
            if (model.Scene?.Node(key) is not { } node || !model.Places.TryGetValue(key, out var place))
            {
                _turns.Remove(key);
                continue;
            }

            var progress = TurnProgress(key);
            moving |= progress < 1;
            DrawTurn(dc, node, place, near, progress);
        }

        moving |= model.Cards.Values.Any(card => card.State == CardState.Fetching || card.Rate == CardRateKind.Fetching);
        _cardsMove = moving;
    }

    /// <summary>Starts the turn of each card that was turned or turned back since the graph was last drawn.</summary>
    /// <param name="model">The view model.</param>
    private void StartTurns(GraphViewModel model)
    {
        var now = DateTime.UtcNow;
        foreach (var key in model.Cards.Keys.Where(key => !_shownTurned.Contains(key)).ToList())
        {
            _shownTurned.Add(key);
            _turns[key] = (now, true);
        }

        foreach (var key in _shownTurned.Where(key => !model.IsTurned(key)).ToList())
        {
            _shownTurned.Remove(key);
            _turns[key] = (now, false);
        }
    }

    /// <summary>Gives how far a card has come in its turn, eased; 1 when it stands still.</summary>
    /// <param name="key">The node.</param>
    /// <returns>The share, from 0 to 1.</returns>
    private double TurnProgress(string key)
    {
        if (!_turns.TryGetValue(key, out var turn))
        {
            return 1;
        }

        var share = AnimationsOn ? (DateTime.UtcNow - turn.Start).TotalMilliseconds / TurnTime.TotalMilliseconds : 1;
        if (share >= 1)
        {
            _turns.Remove(key);
            return 1;
        }

        return 1 - Math.Pow(1 - share, 3);
    }

    /// <summary>
    /// Draws one card in its turn: the front narrowing to its edge in the first half, the back widening from it in
    /// the second, growing from the node's size to its own.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The node.</param>
    /// <param name="place">Its top-left corner.</param>
    /// <param name="near">The nodes near what is chosen.</param>
    /// <param name="progress">How far the turn has come.</param>
    private void DrawTurn(DrawingContext dc, NodeView node, GraphPoint place, IReadOnlySet<string> near, double progress)
    {
        var model = _model!;
        var toBack = model.IsTurned(node.Key);
        var angle = (toBack ? progress : 1 - progress) * Math.PI;
        var squeeze = Math.Abs(Math.Cos(angle));
        var grown = toBack ? progress : 1 - progress;
        if (angle < Math.PI / 2)
        {
            var centre = new Point(place.X + (node.Width / 2), place.Y + (GraphSizes.NodeHeight / 2));
            dc.PushTransform(new ScaleTransform(squeeze, 1, centre.X, centre.Y));
            _drawingFace = true;
            DrawNode(dc, node, place, near);
            _drawingFace = false;
            dc.Pop();
            return;
        }

        if (model.Cards.GetValueOrDefault(node.Key) is not { } card)
        {
            return;
        }

        var width = node.Width + ((Math.Max(node.Width, HoldingsCard.Width) - node.Width) * grown);
        var height = GraphSizes.NodeHeight + ((card.Height - GraphSizes.NodeHeight) * grown);
        var frame = new Rect(place.X, place.Y, width, height);
        dc.PushTransform(new ScaleTransform(squeeze, 1, frame.X + (frame.Width / 2), frame.Y));
        DrawBack(dc, node, card, frame);
        dc.Pop();
    }

    /// <summary>Draws a card's back: its head, a row per asset (or what stands in their place) and its foot.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The node.</param>
    /// <param name="card">The back.</param>
    /// <param name="frame">Where it stands.</param>
    private void DrawBack(DrawingContext dc, NodeView node, HoldingsCard card, Rect frame)
    {
        var shadow = frame;
        shadow.Offset(0, 8);
        dc.PushOpacity(0.35);
        dc.DrawRoundedRectangle(Brushes.Black, null, shadow, CardRadius, CardRadius);
        dc.Pop();
        var chosen = _model!.SelectedNode == node.Key;
        var edge = PenOf(Ink(chosen ? "Brush.TextMuted" : "Brush.BorderStrong"), 1.5);
        dc.DrawRoundedRectangle(Ink("Brush.Floating"), null, frame, CardRadius, CardRadius);
        dc.PushClip(new RectangleGeometry(frame, CardRadius, CardRadius));
        DrawCardHead(dc, node, frame);
        var top = frame.Y + HoldingsCard.HeadHeight;
        switch (card.State)
        {
            case CardState.Fetching:
                DrawCardNote(dc, frame, top, HoldingsCards.FetchingText, null, shimmer: true);
                break;
            case CardState.Failed:
                DrawCardNote(dc, frame, top, HoldingsCards.FailedTitle, card.Problem, shimmer: false);
                break;
            default:
                DrawCardRows(dc, card, frame, top);
                break;
        }

        DrawCardFoot(dc, card, frame);
        dc.Pop();
        dc.DrawRoundedRectangle(null, edge, frame, CardRadius, CardRadius);
    }

    /// <summary>Draws the back's head: the sort's small shape, the node's name and the turn back.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The node.</param>
    /// <param name="frame">The card.</param>
    private void DrawCardHead(DrawingContext dc, NodeView node, Rect frame)
    {
        var ink = InkOf(node.Look.Ink);
        var middle = frame.Y + (HoldingsCard.HeadHeight / 2);
        var shape = new Rect(frame.X + CardPadding, middle - 4, 8, 8);
        var radius = node.Look.Shape == NodeShape.Round ? 4 : 2;
        var pen = PenOf(ink, 1.5, node.Look.IsDashed ? FrameDash : null);
        dc.DrawRoundedRectangle(null, pen, shape, radius, radius);
        var room = frame.Width - (2 * CardPadding) - 15 - TurnBackBox;
        var name = CachedFitted(node.Name, 12, Ink("Brush.TextMuted"), FontWeights.Normal, room);
        dc.DrawText(name, new Point(shape.Right + 7, middle - (name.Height / 2)));
        // The turn back shows only while the pointer is over the card (sketch IN4a), lit when it is on it.
        if (_hoveredCard != node.Key)
        {
            return;
        }

        var back = TurnBackRect(frame);
        if (_onTurnBack)
        {
            dc.DrawRoundedRectangle(Ink("Brush.SurfaceSelected"), null, back, 6, 6);
        }

        DrawGlyph(dc, "Icon.Flip", Ink("Brush.TextMuted"),
            new Point(back.X + 5.5, back.Y + 5.5), 13);
    }

    /// <summary>Draws the rows of a shown back: one per asset, or the empty address's row.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="card">The back.</param>
    /// <param name="frame">The card.</param>
    /// <param name="top">Where the rows start.</param>
    private void DrawCardRows(DrawingContext dc, HoldingsCard card, Rect frame, double top)
    {
        if (card.IsEmpty)
        {
            var since = card.EmptySince is { } at ? HoldingsCards.SinceText(at) : null;
            DrawEmptyRow(dc, frame, top, since);
            return;
        }

        foreach (var row in card.Rows)
        {
            DrawAssetRow(dc, card, row, frame, top);
            top += HoldingsCard.RowHeight;
        }
    }

    /// <summary>
    /// Draws one asset's row: its ring with its mark, the amount with its last decimals dimmed, the value under it
    /// (dashed under when the rate is the case's), and the snowflake when its issuer can freeze it.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="card">The back.</param>
    /// <param name="row">The row.</param>
    /// <param name="frame">The card.</param>
    /// <param name="top">The row's top.</param>
    private void DrawAssetRow(DrawingContext dc, HoldingsCard card, HoldingsRow row, Rect frame, double top)
    {
        DrawRule(dc, frame, top);
        var middle = top + (HoldingsCard.RowHeight / 2);
        var ink = Ink(row.Ink);
        var ring = new Point(frame.X + CardPadding + (AssetRing / 2), middle);
        dc.DrawEllipse(null, PenOf(ink, 1.5), ring, (AssetRing / 2) - 0.75, (AssetRing / 2) - 0.75);
        var glyph = Text(row.Glyph, 13, ink, FontWeights.Normal);
        dc.DrawText(glyph, new Point(ring.X - (glyph.Width / 2), ring.Y - (glyph.Height / 2)));
        var left = frame.X + CardPadding + AssetRing + 11;
        var head = Text(row.Head, 14.5, Ink("Brush.Text"), FontWeights.SemiBold);
        var tail = Text(row.Tail, 14.5, Ink("Brush.TextDim"), FontWeights.Normal);
        var amountTop = middle - head.Height + 2;
        dc.DrawText(head, new Point(left, amountTop));
        dc.DrawText(tail, new Point(left + head.Width, amountTop));
        DrawRowValue(dc, card, row, new Point(left, middle + 2));
        if (row.Pending is not null)
        {
            // What waits in the mempool (finding M2): a small amber clock before the snowflake's place.
            var clock = new Point(frame.Right - CardPadding - (row.CanBeFrozen ? 34 : 10), middle);
            dc.DrawEllipse(Ink("Brush.SurfaceSelected"), null, clock, 10, 10);
            DrawGlyph(dc, "Icon.Clock", Ink("Brush.Pending"),
                new Point(clock.X - 6, clock.Y - 6), 12);
        }

        if (row.CanBeFrozen)
        {
            var (disc, icon) = (Ink("Brush.Mark.Freeze"), Ink("Brush.Mark.OnFreeze"));
            var centre = new Point(frame.Right - CardPadding - 10, middle);
            dc.DrawEllipse(disc, null, centre, 10, 10);
            DrawGlyph(dc, "Icon.Snow", icon, new Point(centre.X - 5.5, centre.Y - 5.5), 11);
        }
    }

    /// <summary>Draws a row's value: today's in grey, the case's dimmer and dashed under, or a dash without a rate.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="card">The back.</param>
    /// <param name="row">The row.</param>
    /// <param name="at">Where the value starts.</param>
    private void DrawRowValue(DrawingContext dc, HoldingsCard card, HoldingsRow row, Point at)
    {
        if (card.Rate == CardRateKind.Fetching)
        {
            DrawShimmer(dc, new Rect(at.X, at.Y + 4, 64, 8));
            return;
        }

        var fromCase = card.Rate == CardRateKind.FromCase;
        var ink = Ink(fromCase || row.Value is null ? "Brush.TextDim" : "Brush.TextMuted");
        var text = Text(row.Value ?? HoldingsCards.NoValue, 11.5, ink, FontWeights.Normal);
        dc.DrawText(text, at);
        if (fromCase && row.Value is not null)
        {
            var line = PenOf(Ink("Brush.BorderHover"), 1, FrameDash);
            var y = at.Y + text.Height;
            dc.DrawLine(line, new Point(at.X, y), new Point(at.X + text.Width, y));
        }
    }

    /// <summary>Draws the row of an empty address: a dashed ring with a dash, <em>Tom</em> and since when.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="frame">The card.</param>
    /// <param name="top">The row's top.</param>
    /// <param name="since">Since when, or <see langword="null"/>.</param>
    private void DrawEmptyRow(DrawingContext dc, Rect frame, double top, string? since)
    {
        DrawRule(dc, frame, top);
        var middle = top + (HoldingsCard.EmptyHeight / 2);
        var ring = new Point(frame.X + CardPadding + (AssetRing / 2), middle);
        var dashed = PenOf(Ink("Brush.BorderStrong"), 1.5, FrameDash);
        dc.DrawEllipse(null, dashed, ring, (AssetRing / 2) - 0.75, (AssetRing / 2) - 0.75);
        dc.DrawLine(PenOf(Ink("Brush.TextDim"), 1.5), new Point(ring.X - 4.5, ring.Y),
            new Point(ring.X + 4.5, ring.Y));
        DrawTwoLines(dc, new Point(frame.X + CardPadding + AssetRing + 11, middle), HoldingsCards.EmptyText, since);
    }

    /// <summary>Draws the row that stands in for the assets while they are fetched, or when they could not be.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="frame">The card.</param>
    /// <param name="top">The row's top.</param>
    /// <param name="title">What it says.</param>
    /// <param name="detail">Why, under it, or <see langword="null"/>.</param>
    /// <param name="shimmer">Whether a shimmer shows that something comes.</param>
    private void DrawCardNote(DrawingContext dc, Rect frame, double top, string title, string? detail, bool shimmer)
    {
        DrawRule(dc, frame, top);
        var middle = top + (HoldingsCard.EmptyHeight / 2);
        var ring = new Point(frame.X + CardPadding + (AssetRing / 2), middle);
        if (shimmer)
        {
            DrawShimmer(dc, new Rect(ring.X - (AssetRing / 2), ring.Y - (AssetRing / 2), AssetRing, AssetRing));
        }
        else
        {
            DrawGlyph(dc, "Icon.Alert", Ink("Brush.Pending"), new Point(ring.X - 8, ring.Y - 8), 16);
        }

        DrawTwoLines(dc, new Point(frame.X + CardPadding + AssetRing + 11, middle), title, detail, frame.Right - CardPadding);
    }

    /// <summary>Writes a title and a dimmer line under it, centred on a height.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="at">The left edge and the middle.</param>
    /// <param name="title">The title.</param>
    /// <param name="detail">The line under it, or <see langword="null"/>.</param>
    /// <param name="right">How far right the lines may reach.</param>
    private void DrawTwoLines(DrawingContext dc, Point at, string title, string? detail, double right = double.NaN)
    {
        var room = double.IsNaN(right) ? double.PositiveInfinity : right - at.X;
        var first = CachedFitted(title, 13.5, Ink("Brush.TextMuted"), FontWeights.Normal, room);
        var second = detail is null
            ? null
            : CachedFitted(detail, 11.5, Ink("Brush.TextDim"), FontWeights.Normal, room);
        var height = first.Height + (second?.Height ?? 0);
        dc.DrawText(first, new Point(at.X, at.Y - (height / 2)));
        if (second is not null)
        {
            dc.DrawText(second, new Point(at.X, at.Y - (height / 2) + first.Height));
        }
    }

    /// <summary>
    /// Draws the back's foot: where the rate comes from (a green dot for today's, a clock for the case's or none, a
    /// turning arc while it is fetched) and, to the right, the block the balance was read at.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="card">The back.</param>
    /// <param name="frame">The card.</param>
    private void DrawCardFoot(DrawingContext dc, HoldingsCard card, Rect frame)
    {
        var foot = new Rect(frame.X, frame.Bottom - HoldingsCard.FootHeight, frame.Width, HoldingsCard.FootHeight);
        dc.DrawRectangle(Ink("Brush.Surface"), null, foot);
        DrawRule(dc, frame, foot.Y);
        var middle = foot.Y + (foot.Height / 2);
        var dim = Ink("Brush.TextDim");
        var right = foot.Right - CardPadding;
        if (card.Block.Length > 0)
        {
            var height = Text(card.Block, 11, dim, FontWeights.Normal);
            right -= height.Width;
            dc.DrawText(height, new Point(right, middle - (height.Height / 2)));
            right -= 15;
            DrawGlyph(dc, "Icon.Block", dim, new Point(right, middle - 5.5), 11);
        }

        if (!card.ShowsRate || (card.State != CardState.Shown && card.Rate != CardRateKind.Fetching))
        {
            return;
        }

        var left = foot.X + CardPadding;
        DrawRateSign(dc, card.Rate, new Point(left + 6, middle));
        var ink = Ink(card.Rate == CardRateKind.Today ? "Brush.TextDim" : "Brush.TextMuted");
        var text = CachedFitted(card.RateText, 11, ink, FontWeights.Normal, right - left - 26);
        dc.DrawText(text, new Point(left + 16, middle - (text.Height / 2)));
    }

    /// <summary>Draws the sign before the rate's line.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="rate">Where the rate comes from.</param>
    /// <param name="centre">The sign's centre.</param>
    private void DrawRateSign(DrawingContext dc, CardRateKind rate, Point centre)
    {
        switch (rate)
        {
            case CardRateKind.Today:
                dc.DrawEllipse(Ink("Brush.Success"), null, centre, 3, 3);
                break;
            case CardRateKind.Fetching:
                var turned = AnimationsOn ? (DateTime.UtcNow.TimeOfDay.TotalMilliseconds / 900 % 1) * 360 : 0;
                dc.PushTransform(new RotateTransform(turned, centre.X, centre.Y));
                DrawGlyph(dc, "Icon.Refresh", Ink("Brush.TextMuted"),
                    new Point(centre.X - 6, centre.Y - 6), 12);
                dc.Pop();
                break;
            default:
                DrawGlyph(dc, "Icon.Clock", Ink("Brush.Pending"),
                    new Point(centre.X - 6, centre.Y - 6), 12);
                break;
        }
    }

    /// <summary>Draws the thin rule over a row.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="frame">The card.</param>
    /// <param name="y">The row's top.</param>
    private void DrawRule(DrawingContext dc, Rect frame, double y) =>
        dc.DrawLine(PenOf(Ink("Brush.Card.Rule"), 1), new Point(frame.X, y),
            new Point(frame.Right, y));

    /// <summary>Draws a bar that breathes while something is fetched; still when Windows does not animate.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="bar">The bar.</param>
    private void DrawShimmer(DrawingContext dc, Rect bar)
    {
        var phase = AnimationsOn ? DateTime.UtcNow.TimeOfDay.TotalMilliseconds / 1200 % 1 : 0.5;
        dc.PushOpacity(0.35 + (0.45 * Math.Sin(phase * Math.PI)));
        var radius = Math.Min(bar.Height, bar.Width) / 2;
        dc.DrawRoundedRectangle(Ink("Brush.BorderStrong"), null, bar, radius, radius);
        dc.Pop();
    }

    /// <summary>
    /// Draws the hourglass in a node's lower right corner while its lookup waits for its source (sketch GR9a), breathing
    /// with the card's frames.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="node">The node.</param>
    /// <param name="place">Its top-left corner.</param>
    private void DrawWaitGlass(DrawingContext dc, NodeView node, GraphPoint place)
    {
        if (!_model!.IsWaitingNode(node.Key))
        {
            return;
        }

        var centre = new Point(
            place.X + node.Width + MarkOverhang - (MarkSize / 2),
            place.Y + GraphSizes.NodeHeight + MarkOverhang - (MarkSize / 2));
        var phase = AnimationsOn ? DateTime.UtcNow.TimeOfDay.TotalMilliseconds / 1800 % 1 : 0.5;
        dc.PushOpacity(0.6 + (0.4 * Math.Sin(phase * Math.PI)));
        dc.DrawEllipse(Ink("Brush.BorderStrong"),
            PenOf(Ink("Brush.Background"), 2), centre, MarkSize / 2, MarkSize / 2);
        DrawGlyph(dc, "Icon.Hourglass", Ink("Brush.Pending"),
            new Point(centre.X - (MarkIconSize / 2), centre.Y - (MarkIconSize / 2)), MarkIconSize);
        dc.Pop();
        if (OnScreenNow(new GraphRect(centre.X - MarkSize, centre.Y - MarkSize, 2 * MarkSize, 2 * MarkSize)))
        {
            _breathing.Add(node.Key);
        }
    }

    /// <summary>
    /// Keeps the cards' frames running while a card turns, something on a card is fetched or an hourglass breathes on
    /// screen, as the scene drawn last says.
    /// </summary>
    /// <param name="moving">Whether a card moves.</param>
    private void KeepTurning(bool moving) => KeepMotion(GraphMotion.Cards, moving);

    /// <summary>
    /// Takes a press for the cards: Alt + click on a node or a card turns it; a click on a turned card's turn back turns
    /// it back, and elsewhere on it chooses the node.
    /// </summary>
    /// <param name="surface">The press on the surface.</param>
    /// <returns><see langword="true"/> when a card took the press.</returns>
    private bool PressCard(GraphPoint surface)
    {
        var model = _model!;
        var card = CardAt(surface);
        var alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
        if (alt && (card ?? model.HitTest(surface, Tolerance).Node) is { } key)
        {
            model.ToggleTurn(key);
            return true;
        }

        if (card is null)
        {
            return false;
        }

        if (CardFrame(card) is { } frame && TurnBackRect(frame).Contains(ToPoint(surface)))
        {
            model.ToggleTurn(card);
        }
        else
        {
            model.Choose(card, null);
        }

        return true;
    }

    /// <summary>Follows the pointer over the cards, for the turn back's light.</summary>
    /// <param name="surface">The pointer on the surface.</param>
    private void HoverCard(GraphPoint surface)
    {
        var card = CardAt(surface);
        var onBack = card is not null && CardFrame(card) is { } frame && TurnBackRect(frame).Contains(ToPoint(surface));
        if (card != _hoveredCard || onBack != _onTurnBack)
        {
            (_hoveredCard, _onTurnBack) = (card, onBack);
            Redraw();
        }
    }

    /// <summary>Gives what a card says on hovering a part of it: the rate's line, a value's steps or the block.</summary>
    /// <param name="surface">The pointer on the surface.</param>
    /// <returns>The text, or <see langword="null"/> when the pointer is not on a card.</returns>
    private string? CardTipAt(GraphPoint surface)
    {
        if (CardAt(surface) is not { } key || _model!.Cards.GetValueOrDefault(key) is not { } card
            || CardFrame(key) is not { } frame)
        {
            return null;
        }

        var point = ToPoint(surface);
        if (TurnBackRect(frame).Contains(point))
        {
            return "Vänd tillbaka · Alt + klick";
        }

        if (point.Y >= frame.Bottom - HoldingsCard.FootHeight)
        {
            return point.X > frame.X + (frame.Width / 2) ? card.BlockTip : card.RateTip;
        }

        var index = (int)((point.Y - frame.Y - HoldingsCard.HeadHeight) / HoldingsCard.RowHeight);
        return index >= 0 && index < card.Rows.Count ? RowTip(card.Rows[index]) : card.Problem ?? card.RateTip;
    }

    /// <summary>Gives what a row says on hovering: how its value was counted, and what waits in the mempool.</summary>
    /// <param name="row">The row.</param>
    /// <returns>The text.</returns>
    private static string RowTip(HoldingsRow row)
    {
        var tip = row.ValueTip ?? row.Full;
        return row.Pending is { } pending ? $"{tip}\n{pending}" : tip;
    }

    /// <summary>Gives the turned card under a point, the one drawn last first.</summary>
    /// <param name="surface">The point on the surface.</param>
    /// <returns>The node's key, or <see langword="null"/>.</returns>
    private string? CardAt(GraphPoint surface)
    {
        if (_model is null)
        {
            return null;
        }

        var point = ToPoint(surface);
        return _shownTurned.Reverse().FirstOrDefault(key => CardFrame(key) is { } frame && frame.Contains(point));
    }

    /// <summary>Gives where a turned card stands on the surface, when it stands still.</summary>
    /// <param name="key">The node.</param>
    /// <returns>The frame, or <see langword="null"/>.</returns>
    private Rect? CardFrame(string key)
    {
        var model = _model!;
        if (_turns.ContainsKey(key) || model.Cards.GetValueOrDefault(key) is not { } card
            || model.Scene?.Node(key) is not { } node || !model.Places.TryGetValue(key, out var place))
        {
            return null;
        }

        return new Rect(place.X, place.Y, Math.Max(node.Width, HoldingsCard.Width), card.Height);
    }

    /// <summary>Gives where the turn back stands in a card's head.</summary>
    /// <param name="frame">The card.</param>
    /// <returns>Its square.</returns>
    private static Rect TurnBackRect(Rect frame) => new(
        frame.Right - 6 - TurnBackBox, frame.Y + ((HoldingsCard.HeadHeight - TurnBackBox) / 2), TurnBackBox, TurnBackBox);
}
