namespace Chainsaw.App.Field;

/// <summary>
/// One scene of the field: the characters from faint to strong, how the tone is stretched, and the tone itself at a
/// point and a time. The tone of every scene is a value in [0, 1] that picks a character from <see cref="Ramp"/>.
/// </summary>
public sealed class FieldScene
{
    /// <summary>The signature of a scene's tone: position (centred, scaled by the page's aspect) and time.</summary>
    /// <param name="x">The horizontal position, about -0.9 to 0.9 on a wide page.</param>
    /// <param name="y">The vertical position, -0.5 to 0.5.</param>
    /// <param name="t">The time in the field's seconds.</param>
    /// <param name="dx">The width of one cell in the same units, for scenes that look at their neighbours.</param>
    /// <param name="dy">The height of one cell in the same units.</param>
    /// <returns>The tone, in [0, 1].</returns>
    public delegate double ToneFunction(double x, double y, double t, double dx, double dy);

    /// <summary>Creates a scene.</summary>
    /// <param name="kind">Which scene it is.</param>
    /// <param name="ramp">The characters from empty to strong; the first is a space.</param>
    /// <param name="contrast">How much the tone is stretched around its middle.</param>
    /// <param name="brightness">How much is added to the tone after the stretch.</param>
    /// <param name="tone">The tone at a point and a time.</param>
    private FieldScene(FieldSceneKind kind, string ramp, double contrast, double brightness, ToneFunction tone)
    {
        Kind = kind;
        Ramp = ramp;
        Contrast = contrast;
        Brightness = brightness;
        Tone = tone;
    }

    /// <summary>Gets which scene it is.</summary>
    public FieldSceneKind Kind { get; }

    /// <summary>Gets the characters from empty to strong; the first is a space.</summary>
    public string Ramp { get; }

    /// <summary>Gets how much the tone is stretched around its middle.</summary>
    public double Contrast { get; }

    /// <summary>Gets how much is added to the tone after the stretch.</summary>
    public double Brightness { get; }

    /// <summary>Gets the tone at a point and a time.</summary>
    public ToneFunction Tone { get; }

    /// <summary>Gets the four scenes of the sketch.</summary>
    public static IReadOnlyList<FieldScene> All { get; } =
    [
        new(FieldSceneKind.Swell, " .:-=+*#", 0.62, -0.19, SwellTone),
        new(FieldSceneKind.Drift, " .:-=+*#", 1, 0.05, DriftTone),
        new(FieldSceneKind.Contour, " .·-~=", 1, 0, ContourTone),
        new(FieldSceneKind.Mesh, " .:-=+*#%@", 1, 0, MeshTone),
    ];

    /// <summary>Gets a scene by its kind.</summary>
    /// <param name="kind">The kind.</param>
    /// <returns>The scene; every kind has one.</returns>
    public static FieldScene For(FieldSceneKind kind) => All.First(scene => scene.Kind == kind);

    /// <summary>Gives the tone at a point after the scene's contrast and brightness, clamped to [0, 1].</summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <param name="t">The time.</param>
    /// <param name="dx">The width of one cell.</param>
    /// <param name="dy">The height of one cell.</param>
    /// <returns>The tone that picks the character.</returns>
    public double ShapedTone(double x, double y, double t, double dx, double dy) =>
        Math.Clamp((Tone(x, y, t, dx, dy) - 0.5) * Contrast + 0.5 + Brightness, 0, 1);

    /// <summary>Three sine waves at different angles and speeds, the second bent by the first.</summary>
    /// <inheritdoc cref="ToneFunction"/>
    private static double SwellTone(double x, double y, double t, double dx, double dy)
    {
        const double Turn = 6.2831853;
        var s = Math.Sin(((x * 0.94 + y * 0.33) / 0.95 - t / 9) * Turn);
        var c = Math.Sin(((x * 0.6 - y * 0.8) / 0.58 - t / 13) * Turn + s * 0.9);
        var k = Math.Sin(((-x * 0.29 + y * 0.96) / 0.36 + t / 17) * Turn);
        return FieldNoise.Smooth(0.3, 0.9, 0.5 + 0.5 * (s * 0.5 + c * 0.35 + k * 0.15));
    }

    /// <summary>Fractal noise warped by two more fields of fractal noise, all drifting slowly.</summary>
    /// <inheritdoc cref="ToneFunction"/>
    private static double DriftTone(double x, double y, double t, double dx, double dy)
    {
        var tt = t * 0.045;
        var qx = x * 1.15;
        var qy = y * 1.15;
        var wx = FieldNoise.Fbm(qx * 0.7 + tt, qy * 0.7 - tt * 0.7);
        var wy = FieldNoise.Fbm(qx * 0.7 + 4.3 - tt * 0.8, qy * 0.7 + tt + 1.7);
        return FieldNoise.Smooth(0.36, 0.74, FieldNoise.Fbm(qx + wx * 1.3 + tt * 0.4, qy + wy * 1.3 + tt * 0.25));
    }

    /// <summary>Four gaussian blobs on slow orbits, added together.</summary>
    /// <inheritdoc cref="ToneFunction"/>
    private static double MeshTone(double x, double y, double t, double dx, double dy)
    {
        var tt = (t + 40) * 0.07;
        var sum = 0.0;
        for (var n = 0; n < 4; n++)
        {
            var f = n * 1.7;
            var cx = 0.66 * Math.Sin(tt * (0.7 + f * 0.13) + f);
            var cy = 0.34 * Math.Cos(tt * (0.9 - f * 0.07) + f * 1.3);
            var size = 0.3 + 0.07 * Math.Sin(tt + f);
            var ox = x - cx;
            var oy = y - cy;
            sum += Math.Exp(-(ox * ox + oy * oy) / (size * size)) * (0.8 - n * 0.1);
        }

        return FieldNoise.Smooth(0.06, 1.1, sum);
    }

    /// <summary>
    /// Contour lines of <see cref="Height"/>: a cell is on a line when the height is near a whole number, measured in
    /// how much the height changes across one cell, so that the lines keep their width on steep and flat ground.
    /// </summary>
    /// <inheritdoc cref="ToneFunction"/>
    private static double ContourTone(double x, double y, double t, double dx, double dy)
    {
        var height = Height(x, y, t);
        var change = Math.Abs(Height(x + dx, y, t) - height) + Math.Abs(Height(x, y + dy, t) - height);
        var width = Math.Max(change, 1e-4);
        var distance = Math.Abs(FieldNoise.Fract(height + 0.5) - 0.5) / width;
        var major = ((((int)Math.Floor(height + 0.5)) % 5) + 5) % 5 == 0 ? 1 : 0;
        return (1 - FieldNoise.Smooth(0.35, 1.25 - major * 0.25, distance)) * (0.55 + 0.45 * major);
    }

    /// <summary>The landscape of the contour scene: three octaves of noise and a slow sway, thirteen lines high.</summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <param name="t">The time.</param>
    /// <returns>The height.</returns>
    private static double Height(double x, double y, double t)
    {
        var tt = (t + 40) * 0.02;
        var qx = x * 1.15 + tt;
        var qy = y * 1.15 - tt * 0.6;
        var h = FieldNoise.Noise(qx, qy) * 0.62
            + FieldNoise.Noise(qx * 2.07 + 3.1, qy * 2.07 + 3.1) * 0.27
            + FieldNoise.Noise(qx * 4.3 - tt + 7, qy * 4.3 - tt + 7) * 0.11;
        return (h + 0.08 * Math.Sin(tt * 2 + x * 0.8)) * 13;
    }
}
