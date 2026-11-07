using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Chainsaw.App.Field;

namespace Chainsaw.App.Controls;

/// <summary>
/// Draws the field's characters once as masks in device pixels, in the field's monospaced font, so that every frame
/// can be put together off the UI thread (see <see cref="FieldRasterizer"/>).
/// </summary>
public static class GlyphAtlasFactory
{
    /// <summary>The height of a character cell and the font's size, in device-independent pixels (sketch ID7).</summary>
    public const double CellHeight = 11;

    /// <summary>The field's font: monospaced, medium weight, as the sketch's.</summary>
    private static readonly Typeface Font = new(
        new FontFamily("Cascadia Mono, Consolas"), FontStyles.Normal, FontWeights.Medium, FontStretches.Normal);

    /// <summary>Draws every character the field uses at a screen's scale.</summary>
    /// <param name="scale">Device pixels per device-independent pixel.</param>
    /// <returns>The masks and the cell's size.</returns>
    public static GlyphAtlas Create(double scale)
    {
        var cellWidth = Text("0", CellHeight).WidthIncludingTrailingWhitespace;
        var masks = new Dictionary<char, GlyphMask>();
        foreach (var character in GlyphAtlas.Characters.Where(c => c != ' '))
        {
            masks[character] = Mask(character, cellWidth, scale);
        }

        return new GlyphAtlas(cellWidth, CellHeight, scale, masks);
    }

    /// <summary>Draws one character white on nothing and keeps how much it covers each pixel.</summary>
    /// <param name="character">The character.</param>
    /// <param name="cellWidth">The cell's width, in device-independent pixels.</param>
    /// <param name="scale">Device pixels per device-independent pixel.</param>
    /// <returns>The mask.</returns>
    private static GlyphMask Mask(char character, double cellWidth, double scale)
    {
        var text = Text(character.ToString(), CellHeight * scale);
        var width = (int)Math.Ceiling(cellWidth * scale) + 1;
        var height = (int)Math.Ceiling(text.Height);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawText(text, new Point(0, 0));
        }

        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        var alpha = new byte[width * height];
        for (var i = 0; i < alpha.Length; i++)
        {
            alpha[i] = pixels[i * 4 + 3];
        }

        return new GlyphMask(width, height, alpha);
    }

    /// <summary>Lays out a text in the field's font.</summary>
    /// <param name="text">The text.</param>
    /// <param name="size">The font's size, in the pixels it is drawn in.</param>
    /// <returns>The laid out text.</returns>
    private static FormattedText Text(string text, double size) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Font, size, Brushes.White, 1.0);
}
