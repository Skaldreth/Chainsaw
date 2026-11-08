using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// The pens and measured texts the surface draws again in every frame, kept by what they are made from
/// (<see cref="DrawCache{TKey, TValue}"/>): the pile's cards, the selection view's dots and the amounts on the lines
/// make nothing anew from one frame to the next while the fan opens, the dots come out or a node is dragged
/// (principle 6).
/// </summary>
public sealed partial class GraphCanvas
{
    /// <summary>The pens made so far, frozen.</summary>
    private readonly DrawCache<PenKey, Pen> _pens = new();

    /// <summary>The texts made so far, measured at the screen's scale.</summary>
    private readonly DrawCache<TextKey, FormattedText> _texts = new();

    /// <summary>Makes a text from its key, made once so that asking the cache allocates nothing.</summary>
    private Func<TextKey, FormattedText>? _makeText;

    /// <summary>
    /// Forgets the measured texts when the screen's scale changes, since they were measured for the old one.
    /// </summary>
    /// <param name="oldDpi">The old scale.</param>
    /// <param name="newDpi">The new scale.</param>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _texts.Clear();
        base.OnDpiChanged(oldDpi, newDpi);
    }

    /// <summary>Gives a frozen pen, made once for its brush, width, dashes and ends.</summary>
    /// <param name="brush">The brush.</param>
    /// <param name="width">The width.</param>
    /// <param name="dash">The dashes, or <see langword="null"/> for a whole line.</param>
    /// <param name="dashCap">The ends of the dashes.</param>
    /// <returns>The pen, shared by every frame; it must never be changed.</returns>
    private Pen PenOf(Brush brush, double width, DashStyle? dash = null, PenLineCap dashCap = PenLineCap.Square) =>
        _pens.Get(new PenKey(brush, width, dash, dashCap), MakePen);

    /// <summary>Gives a text in the UI font, made and measured once for what it says and how.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The size.</param>
    /// <param name="ink">The colour.</param>
    /// <param name="weight">The weight.</param>
    /// <returns>The formatted text, shared by every frame; it must never be changed.</returns>
    private FormattedText CachedText(string text, double size, Brush ink, FontWeight weight) =>
        _texts.Get(new TextKey(text, size, ink, weight, double.PositiveInfinity), _makeText ??= MakeText);

    /// <summary>Gives an address's text shortened to its room (<see cref="Fitted"/>), made and measured once.</summary>
    /// <param name="subject">The address, txid or id in full.</param>
    /// <param name="size">The size.</param>
    /// <param name="ink">The colour.</param>
    /// <param name="weight">The weight.</param>
    /// <param name="room">How wide it may be.</param>
    /// <returns>The formatted text, shared by every frame; it must never be changed.</returns>
    private FormattedText CachedFitted(string subject, double size, Brush ink, FontWeight weight, double room) =>
        _texts.Get(new TextKey(subject, size, ink, weight, room), _makeText ??= MakeText);

    /// <summary>Makes and measures a text, shortened when it has a room.</summary>
    /// <param name="key">What it is made from.</param>
    /// <returns>The formatted text.</returns>
    private FormattedText MakeText(TextKey key) => double.IsPositiveInfinity(key.Room)
        ? Text(key.Text, key.Size, key.Ink, key.Weight)
        : Fitted(key.Text, key.Size, key.Ink, key.Weight, key.Room);

    /// <summary>Makes a pen and freezes it, so that drawing it costs nothing more.</summary>
    /// <param name="key">What it is made from.</param>
    /// <returns>The pen.</returns>
    private static Pen MakePen(PenKey key)
    {
        var pen = new Pen(key.Brush, key.Width) { DashStyle = key.Dash ?? DashStyles.Solid, DashCap = key.DashCap };
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        return pen;
    }

    /// <summary>What a pen is made from; brushes and dashes compare as the same instance.</summary>
    /// <param name="Brush">The brush.</param>
    /// <param name="Width">The width.</param>
    /// <param name="Dash">The dashes, or <see langword="null"/>.</param>
    /// <param name="DashCap">The ends of the dashes.</param>
    private readonly record struct PenKey(Brush Brush, double Width, DashStyle? Dash, PenLineCap DashCap);

    /// <summary>What a text is made from.</summary>
    /// <param name="Text">What it says.</param>
    /// <param name="Size">Its size.</param>
    /// <param name="Ink">Its colour, as the same instance.</param>
    /// <param name="Weight">Its weight.</param>
    /// <param name="Room">How wide it may be, or infinity when it is not shortened.</param>
    private readonly record struct TextKey(string Text, double Size, Brush Ink, FontWeight Weight, double Room);
}
