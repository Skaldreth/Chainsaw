namespace Chainsaw.App.Field;

/// <summary>
/// The field's characters drawn once as masks in device pixels, so that a frame is put together by copying masks
/// instead of laying out text. The masks are made by the window (which knows the font and the screen's scale) and
/// used off the UI thread.
/// </summary>
/// <param name="CellWidth">The width of a character cell, in device-independent pixels.</param>
/// <param name="CellHeight">The height of a character cell, in device-independent pixels.</param>
/// <param name="Scale">Device pixels per device-independent pixel.</param>
/// <param name="Masks">Each character's mask.</param>
public sealed record GlyphAtlas(
    double CellWidth, double CellHeight, double Scale, IReadOnlyDictionary<char, GlyphMask> Masks)
{
    /// <summary>Every character any scene or the chain draws.</summary>
    public static string Characters { get; } = new(
        FieldScene.All.SelectMany(scene => scene.Ramp).Concat(FieldEngine.ChainRamp).Distinct().ToArray());
}

/// <summary>One character's coverage of the pixels from its cell's top left corner, 0 to 255 per pixel.</summary>
/// <param name="Width">The mask's width, in device pixels.</param>
/// <param name="Height">The mask's height, in device pixels.</param>
/// <param name="Alpha">The coverage, row by row.</param>
public sealed record GlyphMask(int Width, int Height, byte[] Alpha);
