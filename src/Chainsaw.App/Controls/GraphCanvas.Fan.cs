using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;

namespace Chainsaw.App.Controls;

/// <summary>
/// The fanned pile (sketches EX1a, EX1b and EX2b): the rest of the graph dims, the cards lie around the pile, a click
/// chooses one, Shift-click more, and a card dragged out onto the dimmed graph is picked out where it is dropped, with
/// the other chosen cards under it. A click on the dimmed graph, or Esc, closes the fan.
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>How far the pointer must move, in screen units, before a press on a card becomes a drag.</summary>
    private const double CardDragDistance = 4;

    /// <summary>How dark the dimmed graph is under the fan.</summary>
    private const double FanDimOpacity = 0.78;

    /// <summary>A card's corner radius.</summary>
    private const double CardRadius = 7;

    /// <summary>The card pressed in the fan, or <see langword="null"/>.</summary>
    private string? _cardPressed;

    /// <summary>Where on screen the card was pressed.</summary>
    private GraphPoint _cardPressAt;

    /// <summary>Where on the surface a dragged card is now, or <see langword="null"/> while it is not dragged.</summary>
    private GraphPoint? _cardDraggedTo;

    /// <summary>Whether the press under way is on the dimmed graph around an open fan.</summary>
    private bool _fanBackgroundPressed;

    /// <summary>Takes a press while a pile is fanned out: on a card, on the pile, or on the dimmed graph.</summary>
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

    /// <summary>Moves a pressed card, once the pointer has gone far enough to make it a drag.</summary>
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
            Cursor = fan.CardAt(surface) is null ? null : Cursors.Hand;
            return true;
        }

        if (_cardDraggedTo is null && at.DistanceTo(_cardPressAt) < CardDragDistance)
        {
            return true;
        }

        _cardDraggedTo = surface;
        InvalidateVisual();
        return true;
    }

    /// <summary>
    /// Lets go in the fan: a dragged card is picked out where it lands, unless it lands on another card; a click on a
    /// card chooses it (Shift adds); a click on the dimmed graph closes the fan.
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
        _cardPressed = null;
        _cardDraggedTo = null;
        var background = _fanBackgroundPressed;
        _fanBackgroundPressed = false;
        ReleaseMouseCapture();
        if (card is not null && dragged && fan.CardAt(surface) is null)
        {
            var grip = new GraphPoint(
                surface.X - (GraphSizes.NodeWidth / 2), surface.Y - (GraphSizes.NodeHeight / 2));
            _ = _model.PickFromPileAsync(fan.Taking(card), grip);
        }
        else if (card is not null && !dragged)
        {
            fan.Click(card, (Keyboard.Modifiers & ModifierKeys.Shift) != 0);
        }
        else if (background && !OnPile(fan, surface))
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

    /// <summary>The fan's keys: Esc closes it, Enter picks the chosen cards out.</summary>
    /// <param name="key">The key.</param>
    /// <returns><see langword="true"/> when the fan took it.</returns>
    private bool FanKey(Key key)
    {
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

    /// <summary>Says whether a point lies on the fanned pile's own node.</summary>
    /// <param name="fan">The fan.</param>
    /// <param name="surface">The point.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private bool OnPile(PileFanViewModel fan, GraphPoint surface) =>
        _model!.Places.TryGetValue(fan.Pile.Key, out var place) && GraphSizes.NodeFrame(place).Contains(surface);

    /// <summary>
    /// Draws the fan over the graph: the dimmed ground, the pile again on top, a faint line to every card, the cards,
    /// and the cards being dragged at the pointer.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="visible">The visible part of the surface.</param>
    private void DrawFan(DrawingContext dc, GraphRect visible)
    {
        if (_model?.Fan is not { } fan || _model.Scene?.Node(fan.Pile.Key) is not { } pile)
        {
            return;
        }

        dc.PushOpacity(FanDimOpacity);
        dc.DrawRectangle(
            GraphPalette.Find(this, "Brush.Background"),
            null,
            new Rect(visible.X, visible.Y, visible.Width, visible.Height));
        dc.Pop();
        var spoke = new Pen(GraphPalette.Find(this, "Brush.Border"), 1);
        foreach (var card in fan.Shown)
        {
            var frame = PileFanLayout.CardFrame(fan.Places[card.Key]);
            dc.DrawLine(spoke, ToPoint(fan.Centre), ToPoint(frame.Centre));
        }

        DrawNode(dc, pile, _model.Places[pile.Key], new HashSet<string>(StringComparer.Ordinal) { pile.Key });
        var lifted = _cardDraggedTo is not null && _cardPressed is { } pressed ? fan.Taking(pressed) : [];
        foreach (var card in fan.Shown.Where(card => !lifted.Contains(card.Key)))
        {
            DrawCard(dc, card, fan.Places[card.Key], fan.Selected.Contains(card.Key));
        }

        if (_cardDraggedTo is { } to)
        {
            for (var index = 0; index < lifted.Count; index++)
            {
                var card = fan.Pile.Cards.First(one => one.Key == lifted[index]);
                var place = new GraphPoint(
                    to.X - (PileFanLayout.CardWidth / 2), to.Y - (PileFanLayout.CardHeight / 2) + (index * 6));
                DrawCard(dc, card, place, true);
            }
        }
    }

    /// <summary>
    /// Draws a card: a small frame with the sort's colour, its name and the amount between it and the actor.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="card">The card.</param>
    /// <param name="place">Its top-left corner.</param>
    /// <param name="chosen">Whether it is chosen.</param>
    private void DrawCard(DrawingContext dc, PileCard card, GraphPoint place, bool chosen)
    {
        var frame = new Rect(place.X, place.Y, PileFanLayout.CardWidth, PileFanLayout.CardHeight);
        var pen = chosen
            ? new Pen(GraphPalette.Find(this, "Brush.Accent"), 1.5)
            : new Pen(GraphPalette.Brush(this, card.Node.Look.Ink), 1);
        dc.DrawRoundedRectangle(
            GraphPalette.Find(this, chosen ? "Brush.SurfaceRaised" : "Brush.Surface"), pen, frame, CardRadius, CardRadius);
        var name = Fitted(card.Node.Subject, 10.5, GraphPalette.Find(this, "Brush.Text"), FontWeights.Medium,
            PileFanLayout.CardWidth - 12);
        dc.DrawText(name, new Point(place.X + 6, place.Y + 2));
        var amount = Text(AmountText.Number(card.Amount), 9.5, GraphPalette.Find(this, "Brush.TextDim"),
            FontWeights.Normal);
        dc.DrawText(amount, new Point(place.X + 6, place.Y + PileFanLayout.CardHeight - amount.Height - 2));
    }
}
