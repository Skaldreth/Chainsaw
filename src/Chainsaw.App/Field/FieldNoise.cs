namespace Chainsaw.App.Field;

/// <summary>
/// The small pieces of arithmetic the field is made of: a hash, value noise and fractal noise. Rewritten for Chainsaw
/// after asciify-engine's GPU field (MIT, see THIRD_PARTY.md), so that it runs on the CPU and gives the same values on
/// every computer.
/// </summary>
public static class FieldNoise
{
    /// <summary>Gives the fractional part of a number, also for negative numbers (as GLSL's <c>fract</c>).</summary>
    /// <param name="x">The number.</param>
    /// <returns>A value in [0, 1).</returns>
    public static double Fract(double x) => x - Math.Floor(x);

    /// <summary>Eases a value between two edges, as GLSL's <c>smoothstep</c>.</summary>
    /// <param name="edge0">Where the result starts to rise from 0.</param>
    /// <param name="edge1">Where the result reaches 1.</param>
    /// <param name="x">The value.</param>
    /// <returns>0 below the first edge, 1 above the second and a smooth curve between.</returns>
    public static double Smooth(double edge0, double edge1, double x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>A cheap hash of two numbers to a value in [0, 1), the same for the same input.</summary>
    /// <param name="x">The first number.</param>
    /// <param name="y">The second number.</param>
    /// <returns>A value that looks random.</returns>
    public static double Hash(double x, double y)
    {
        var px = Fract(x * 123.34);
        var py = Fract(y * 456.21);
        var d = px * (px + 45.32) + py * (py + 45.32);
        px += d;
        py += d;
        return Fract(px * py);
    }

    /// <summary>Value noise: the hash at the corners of the unit square, blended smoothly between them.</summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <returns>A value in [0, 1) that changes smoothly with the position.</returns>
    public static double Noise(double x, double y)
    {
        var ix = Math.Floor(x);
        var iy = Math.Floor(y);
        var fx = x - ix;
        var fy = y - iy;
        var ux = fx * fx * (3 - 2 * fx);
        var uy = fy * fy * (3 - 2 * fy);
        var a = Hash(ix, iy);
        var b = Hash(ix + 1, iy);
        var c = Hash(ix, iy + 1);
        var d = Hash(ix + 1, iy + 1);
        var top = a + (b - a) * ux;
        var bottom = c + (d - c) * ux;
        return top + (bottom - top) * uy;
    }

    /// <summary>Fractal noise: five octaves of <see cref="Noise"/>, each finer and half as strong.</summary>
    /// <param name="x">The horizontal position.</param>
    /// <param name="y">The vertical position.</param>
    /// <returns>A value in [0, 1) with detail at several scales.</returns>
    public static double Fbm(double x, double y)
    {
        var value = 0.0;
        var amplitude = 0.5;
        for (var octave = 0; octave < 5; octave++)
        {
            value += amplitude * Noise(x, y);
            x = x * 2.03 + 1.7;
            y = y * 2.03 + 9.2;
            amplitude *= 0.5;
        }

        return value;
    }
}
