using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using Chainsaw.App.Graph;

namespace Chainsaw.App.Controls;

/// <summary>
/// What the surface draws again in every frame, made once and kept by what it is made from
/// (<see cref="DrawCache{TKey, TValue}"/>): the brushes and icons looked up in the resources, the pens, the measured
/// texts and the curves of the lines. A frame then allocates almost nothing, every brush and pen in it is frozen (so
/// that WPF sends it to the render thread once instead of watching it for changes), and no resource is looked up
/// through the tree for each node (principle 6).
/// </summary>
/// <remarks>
/// The colours are not changed while Chainsaw runs (there is one theme), so a brush looked up once stays right; a
/// change of the screen's scale forgets the texts, which were measured for the old one.
/// </remarks>
public sealed partial class GraphCanvas
{
    /// <summary>
    /// How many measured texts are kept before the cache starts over: room for the names, addresses, counts and amounts
    /// of a large graph, so that a frame of it never empties the cache it needs.
    /// </summary>
    private const int TextCapacity = 16384;

    /// <summary>The culture the texts are measured in.</summary>
    private static readonly CultureInfo Swedish = CultureInfo.GetCultureInfo("sv-SE");

    /// <summary>
    /// How many pens are kept before the cache starts over: a zoom makes a pen for each icon's colour at every step's
    /// width, and the lines' pens must survive it.
    /// </summary>
    private const int PenCapacity = 16384;

    /// <summary>The pens made so far, frozen.</summary>
    private readonly DrawCache<PenKey, Pen> _pens = new(PenCapacity);

    /// <summary>The texts made so far, measured at the screen's scale.</summary>
    private readonly DrawCache<TextKey, FormattedText> _texts = new(TextCapacity);

    /// <summary>The brushes looked up so far, by resource key, frozen.</summary>
    private readonly Dictionary<string, Brush> _inks = new(StringComparer.Ordinal);

    /// <summary>The icons of the 24 grid looked up so far, by resource key, or <see langword="null"/> when missing.</summary>
    private readonly Dictionary<string, Geometry?> _glyphs = new(StringComparer.Ordinal);

    /// <summary>The typefaces of the UI font made so far, by weight.</summary>
    private readonly Dictionary<FontWeight, Typeface> _typefaces = [];

    /// <summary>The geometry of each line's curve, kept as long as the curve itself is.</summary>
    private readonly ConditionalWeakTable<EdgeCurve, StreamGeometry> _curveGeometries = new();

    /// <summary>Makes a text from its key, made once so that asking the cache allocates nothing.</summary>
    private Func<TextKey, FormattedText>? _makeText;

    /// <summary>The screen's pixels per device-independent pixel, read once per scale.</summary>
    private double _pixelsPerDip;

    /// <summary>
    /// Forgets the measured texts when the screen's scale changes, since they were measured for the old one.
    /// </summary>
    /// <param name="oldDpi">The old scale.</param>
    /// <param name="newDpi">The new scale.</param>
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        _texts.Clear();
        _pixelsPerDip = 0;
        Redraw();
        base.OnDpiChanged(oldDpi, newDpi);
    }

    /// <summary>
    /// Gives a brush of the theme by its resource key, looked up once and frozen; a brush that is missing is grey,
    /// as <see cref="GraphPalette.Find"/> gives it.
    /// </summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The brush, shared by every frame.</returns>
    private Brush Ink(string key)
    {
        if (!_inks.TryGetValue(key, out var brush))
        {
            brush = FrozenCopy(GraphPalette.Find(this, key));
            _inks[key] = brush;
        }

        return brush;
    }

    /// <summary>
    /// Pushes an opacity only when it makes something fainter: at 1 it would change nothing and still cost a layer.
    /// </summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="opacity">The opacity.</param>
    /// <returns>Whether it was pushed, for <see cref="PopFade"/>.</returns>
    private static bool PushFade(DrawingContext dc, double opacity)
    {
        if (opacity >= 1)
        {
            return false;
        }

        dc.PushOpacity(opacity);
        return true;
    }

    /// <summary>Takes back an opacity pushed by <see cref="PushFade"/>.</summary>
    /// <param name="dc">Where to draw.</param>
    /// <param name="pushed">Whether it was pushed.</param>
    private static void PopFade(DrawingContext dc, bool pushed)
    {
        if (pushed)
        {
            dc.Pop();
        }
    }

    /// <summary>Gives an ink's brush (<see cref="GraphPalette.Brush"/>), looked up once and frozen.</summary>
    /// <param name="ink">The ink.</param>
    /// <returns>The brush.</returns>
    private Brush InkOf(GraphInk ink) => Ink(GraphPalette.KeyOf(ink));

    /// <summary>Gives a mark's disc and the brush of the icon on it (<see cref="GraphPalette.Mark"/>), frozen.</summary>
    /// <param name="badge">The mark.</param>
    /// <returns>The disc's brush and the icon's.</returns>
    private (Brush Disc, Brush Icon) MarkInks(NodeBadge badge)
    {
        var (disc, icon) = GraphPalette.MarkKeys(badge);
        return (Ink(disc), Ink(icon));
    }

    /// <summary>Gives an icon of the 24 grid by its resource key, looked up once.</summary>
    /// <param name="key">The resource key.</param>
    /// <returns>The frozen geometry, or <see langword="null"/> when there is no such icon.</returns>
    private Geometry? GlyphOf(string key)
    {
        if (!_glyphs.TryGetValue(key, out var glyph))
        {
            glyph = TryFindResource(key) is Geometry found ? FrozenCopy(found) : null;
            _glyphs[key] = glyph;
        }

        return glyph;
    }

    /// <summary>Gives a frozen pen, made once for its brush, width, dashes, ends and corners.</summary>
    /// <param name="brush">The brush.</param>
    /// <param name="width">The width.</param>
    /// <param name="dash">The dashes, or <see langword="null"/> for a whole line.</param>
    /// <param name="dashCap">The ends of the dashes.</param>
    /// <param name="ends">The ends of the line.</param>
    /// <param name="join">How the line turns its corners.</param>
    /// <returns>The pen, shared by every frame; it must never be changed.</returns>
    private Pen PenOf(
        Brush brush,
        double width,
        DashStyle? dash = null,
        PenLineCap dashCap = PenLineCap.Square,
        PenLineCap ends = PenLineCap.Flat,
        PenLineJoin join = PenLineJoin.Miter) =>
        _pens.Get(new PenKey(brush, width, dash, dashCap, ends, join), MakePen);

    /// <summary>Gives a frozen pen with round ends, dashes and corners, as the icons and the watch lines draw.</summary>
    /// <param name="brush">The brush.</param>
    /// <param name="width">The width.</param>
    /// <param name="dash">The dashes, or <see langword="null"/> for a whole line.</param>
    /// <returns>The pen, shared by every frame; it must never be changed.</returns>
    private Pen RoundPen(Brush brush, double width, DashStyle? dash = null) =>
        PenOf(brush, width, dash, PenLineCap.Round, PenLineCap.Round, PenLineJoin.Round);

    /// <summary>Gives a text in the UI font, made and measured once for what it says and how.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The size.</param>
    /// <param name="ink">The colour.</param>
    /// <param name="weight">The weight.</param>
    /// <returns>The formatted text, shared by every frame; it must never be changed.</returns>
    private FormattedText Text(string text, double size, Brush ink, FontWeight weight) =>
        _texts.Get(new TextKey(text, size, ink, weight, double.PositiveInfinity, false), _makeText ??= MakeText);

    /// <summary>Gives a text on one line, cut with an ellipsis at its room, made and measured once.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The size.</param>
    /// <param name="ink">The colour.</param>
    /// <param name="weight">The weight.</param>
    /// <param name="room">How wide it may be.</param>
    /// <returns>The formatted text, shared by every frame; it must never be changed.</returns>
    private FormattedText TrimmedText(string text, double size, Brush ink, FontWeight weight, double room) =>
        _texts.Get(new TextKey(text, size, ink, weight, room, true), _makeText ??= MakeText);

    /// <summary>Gives an address's text shortened to its room (<see cref="Fitted"/>), made and measured once.</summary>
    /// <param name="subject">The address, txid or id in full.</param>
    /// <param name="size">The size.</param>
    /// <param name="ink">The colour.</param>
    /// <param name="weight">The weight.</param>
    /// <param name="room">How wide it may be.</param>
    /// <returns>The formatted text, shared by every frame; it must never be changed.</returns>
    private FormattedText CachedFitted(string subject, double size, Brush ink, FontWeight weight, double room) =>
        _texts.Get(new TextKey(subject, size, ink, weight, room, false), _makeText ??= MakeText);

    /// <summary>Makes and measures a text: whole, shortened in the middle, or cut with an ellipsis.</summary>
    /// <param name="key">What it is made from.</param>
    /// <returns>The formatted text.</returns>
    private FormattedText MakeText(TextKey key)
    {
        if (double.IsPositiveInfinity(key.Room))
        {
            return NewText(key.Text, key.Size, key.Ink, key.Weight);
        }

        if (!key.Trimmed)
        {
            return Fitted(key.Text, key.Size, key.Ink, key.Weight, key.Room);
        }

        var trimmed = NewText(key.Text, key.Size, key.Ink, key.Weight);
        trimmed.MaxTextWidth = key.Room;
        trimmed.MaxLineCount = 1;
        trimmed.Trimming = TextTrimming.CharacterEllipsis;
        return trimmed;
    }

    /// <summary>
    /// Makes a new text in the UI font, for a text that is changed after it is made (wrapped, centred or struck
    /// through); every other text is taken from the cache with <see cref="Text"/>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The size.</param>
    /// <param name="ink">The colour.</param>
    /// <param name="weight">The weight.</param>
    /// <returns>The formatted text, the caller's own.</returns>
    private FormattedText NewText(string text, double size, Brush ink, FontWeight weight) =>
        new(text, Swedish, FlowDirection.LeftToRight, TypefaceOf(weight), size, ink, PixelsPerDip);

    /// <summary>Gives the UI font's typeface at a weight, made once.</summary>
    /// <param name="weight">The weight.</param>
    /// <returns>The typeface.</returns>
    private Typeface TypefaceOf(FontWeight weight)
    {
        if (!_typefaces.TryGetValue(weight, out var typeface))
        {
            var family = TryFindResource("Font.Ui") as FontFamily ?? new FontFamily("Segoe UI");
            typeface = new Typeface(family, FontStyles.Normal, weight, FontStretches.Normal);
            _typefaces[weight] = typeface;
        }

        return typeface;
    }

    /// <summary>Gets the screen's pixels per device-independent pixel, read once for each scale.</summary>
    private double PixelsPerDip
    {
        get
        {
            if (_pixelsPerDip <= 0)
            {
                _pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            }

            return _pixelsPerDip;
        }
    }

    /// <summary>Gives a line's curve as a frozen geometry, made once for each curve.</summary>
    /// <param name="curve">The curve.</param>
    /// <returns>The geometry, shared by every frame.</returns>
    private StreamGeometry CurveGeometry(EdgeCurve curve) =>
        _curveGeometries.GetValue(curve, static made => Geometry(made));

    /// <summary>Makes a pen and freezes it, so that drawing it costs nothing more.</summary>
    /// <param name="key">What it is made from.</param>
    /// <returns>The pen.</returns>
    private static Pen MakePen(PenKey key)
    {
        var pen = new Pen(key.Brush, key.Width)
        {
            DashStyle = key.Dash ?? DashStyles.Solid,
            DashCap = key.DashCap,
            StartLineCap = key.Ends,
            EndLineCap = key.Ends,
            LineJoin = key.Join,
        };
        if (pen.CanFreeze)
        {
            pen.Freeze();
        }

        return pen;
    }

    /// <summary>
    /// Gives a frozen copy of a resource, or the resource itself when it is already frozen, so that the resource in
    /// the theme is never frozen in place.
    /// </summary>
    /// <typeparam name="T">The kind of resource.</typeparam>
    /// <param name="value">The resource.</param>
    /// <returns>The frozen resource.</returns>
    private static T FrozenCopy<T>(T value)
        where T : Freezable =>
        value.IsFrozen || !value.CanFreeze ? value : (T)value.GetCurrentValueAsFrozen();

    /// <summary>What a pen is made from; brushes and dashes compare as the same instance.</summary>
    /// <param name="Brush">The brush.</param>
    /// <param name="Width">The width.</param>
    /// <param name="Dash">The dashes, or <see langword="null"/>.</param>
    /// <param name="DashCap">The ends of the dashes.</param>
    /// <param name="Ends">The ends of the line.</param>
    /// <param name="Join">How the line turns its corners.</param>
    private readonly record struct PenKey(
        Brush Brush, double Width, DashStyle? Dash, PenLineCap DashCap, PenLineCap Ends, PenLineJoin Join);

    /// <summary>What a text is made from.</summary>
    /// <param name="Text">What it says.</param>
    /// <param name="Size">Its size.</param>
    /// <param name="Ink">Its colour, as the same instance.</param>
    /// <param name="Weight">Its weight.</param>
    /// <param name="Room">How wide it may be, or infinity when it is not shortened.</param>
    /// <param name="Trimmed">Whether it is cut with an ellipsis at its room rather than shortened in the middle.</param>
    private readonly record struct TextKey(
        string Text, double Size, Brush Ink, FontWeight Weight, double Room, bool Trimmed);
}
