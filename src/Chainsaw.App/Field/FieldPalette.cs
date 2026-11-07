namespace Chainsaw.App.Field;

/// <summary>
/// The field's colours as 0xRRGGBB, after the sketch: grey characters on the page, the chain in the accent.
/// </summary>
/// <param name="Page">The page behind the field, the theme's background.</param>
/// <param name="Field">The field's characters.</param>
/// <param name="FieldAlpha">How strongly the field's characters show.</param>
/// <param name="Accent">The accent the chain is drawn in.</param>
/// <param name="Light">The characters the lantern lights.</param>
public sealed record FieldPalette(int Page, int Field, double FieldAlpha, int Accent, int Light)
{
    /// <summary>Gets the sketch's colours: the background <c>#191919</c>, the field <c>#6A6862</c>, Magenta.</summary>
    public static FieldPalette Default { get; } = new(0x191919, 0x6A6862, 0.42, 0xE070B0, 0xD6D6D6);

    /// <summary>
    /// Gives the chain's colour at a place across it: the accent a little deeper on the chain's left side and lighter
    /// on its right, as in the sketch, so that the chain reads as a body and not as a flat stripe.
    /// </summary>
    /// <param name="x">The horizontal position on the page, in device-independent pixels.</param>
    /// <returns>The colour as 0xRRGGBB.</returns>
    public int ChainColor(double x)
    {
        var t = Math.Clamp((x - ChainShape.CenterX + 150) / 300, 0, 1);
        var k = 0.86 + 0.32 * t;
        return (Channel(Accent >> 16, k) << 16) | (Channel(Accent >> 8, k) << 8) | Channel(Accent, k);
    }

    /// <summary>Scales one colour channel, rounded and kept within a byte.</summary>
    /// <param name="value">The channel, in the lowest byte.</param>
    /// <param name="k">The factor.</param>
    /// <returns>The scaled channel.</returns>
    private static int Channel(int value, double k) => Math.Min(255, (int)Math.Floor((value & 0xFF) * k + 0.5));
}
