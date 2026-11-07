namespace Chainsaw.App.Field;

/// <summary>
/// Puts a frame of the field into pixels (32-bit, 0x00RRGGBB, as WPF's <c>Bgr32</c>), off the UI thread: the page,
/// then the field's characters, the chain and the lantern's light, each blended by its mask.
/// </summary>
public static class FieldRasterizer
{
    /// <summary>Draws a frame into a buffer of pixels.</summary>
    /// <param name="frame">The frame.</param>
    /// <param name="atlas">The characters' masks, made for the same cell size.</param>
    /// <param name="palette">The colours.</param>
    /// <param name="pixels">The buffer, row by row; at least <paramref name="width"/> times <paramref name="height"/>.</param>
    /// <param name="width">The buffer's width, in device pixels.</param>
    /// <param name="height">The buffer's height, in device pixels.</param>
    /// <exception cref="ArgumentException">The buffer is too small.</exception>
    public static void Draw(FieldFrame frame, GlyphAtlas atlas, FieldPalette palette, int[] pixels, int width, int height)
    {
        if (pixels.Length < width * height)
        {
            throw new ArgumentException("The buffer is smaller than its size.", nameof(pixels));
        }

        Array.Fill(pixels, palette.Page, 0, width * height);
        var target = new Target(pixels, width, height);
        for (var row = 0; row < frame.Rows; row++)
        {
            for (var column = 0; column < frame.Columns; column++)
            {
                var character = frame.Background[row * frame.Columns + column];
                if (character != ' ')
                {
                    Blend(target, atlas, column, row, character, palette.Field, palette.FieldAlpha);
                }
            }
        }

        foreach (var mark in frame.Chain)
        {
            var color = palette.ChainColor((mark.Column + 0.5) * atlas.CellWidth);
            Blend(target, atlas, mark.Column, mark.Row, mark.Character, color, mark.Alpha);
        }

        foreach (var mark in frame.Lit)
        {
            Blend(target, atlas, mark.Column, mark.Row, mark.Character, palette.Light, mark.Alpha);
        }
    }

    /// <summary>Blends one character's mask in a colour over the pixels of its cell.</summary>
    /// <param name="target">The pixels.</param>
    /// <param name="atlas">The masks.</param>
    /// <param name="column">The cell's column.</param>
    /// <param name="row">The cell's row.</param>
    /// <param name="character">The character.</param>
    /// <param name="color">The colour as 0xRRGGBB.</param>
    /// <param name="alpha">How strongly it shows, from 0 to 1.</param>
    private static void Blend(
        Target target, GlyphAtlas atlas, int column, int row, char character, int color, double alpha)
    {
        if (!atlas.Masks.TryGetValue(character, out var mask) || alpha <= 0)
        {
            return;
        }

        var left = (int)Math.Round(column * atlas.CellWidth * atlas.Scale);
        var top = (int)Math.Round(row * atlas.CellHeight * atlas.Scale);
        var strength = (int)Math.Round(Math.Min(1, alpha) * 256);
        var (red, green, blue) = ((color >> 16) & 0xFF, (color >> 8) & 0xFF, color & 0xFF);
        for (var y = 0; y < mask.Height; y++)
        {
            var py = top + y;
            if (py < 0 || py >= target.Height)
            {
                continue;
            }

            for (var x = 0; x < mask.Width; x++)
            {
                var px = left + x;
                var coverage = mask.Alpha[y * mask.Width + x];
                if (coverage == 0 || px < 0 || px >= target.Width)
                {
                    continue;
                }

                var a = coverage * strength >> 8;
                ref var pixel = ref target.Pixels[py * target.Width + px];
                pixel = Mix(pixel, red, green, blue, a);
            }
        }
    }

    /// <summary>Mixes a colour into a pixel.</summary>
    /// <param name="pixel">The pixel as 0x00RRGGBB.</param>
    /// <param name="red">The colour's red.</param>
    /// <param name="green">The colour's green.</param>
    /// <param name="blue">The colour's blue.</param>
    /// <param name="a">How much of the colour, 0 to 255.</param>
    /// <returns>The mixed pixel.</returns>
    private static int Mix(int pixel, int red, int green, int blue, int a)
    {
        var r = (pixel >> 16) & 0xFF;
        var g = (pixel >> 8) & 0xFF;
        var b = pixel & 0xFF;
        r += (red - r) * a / 255;
        g += (green - g) * a / 255;
        b += (blue - b) * a / 255;
        return (r << 16) | (g << 8) | b;
    }

    /// <summary>The pixels being drawn into and their size.</summary>
    /// <param name="Pixels">The pixels, row by row.</param>
    /// <param name="Width">The width.</param>
    /// <param name="Height">The height.</param>
    private readonly record struct Target(int[] Pixels, int Width, int Height);
}
