namespace Chainsaw.App.Field;

/// <summary>
/// The quiet area around the form (sketch ID7): the field fades out towards the form, so that the characters never
/// stand behind what the user reads or writes. The chain and the lantern's light fade with it.
/// </summary>
public static class CalmArea
{
    /// <summary>How far outside the form the field is fully quiet, in pixels.</summary>
    public const double Margin = 32;

    /// <summary>How far beyond the quiet margin the field takes to reach its full strength, in pixels.</summary>
    public const double FadeDistance = 140;

    /// <summary>Tells how open the field is at a point: 0 inside the quiet area, rising to 1 further out.</summary>
    /// <param name="form">The form's bounds, or <see langword="null"/> when no form is shown.</param>
    /// <param name="x">The point's horizontal position.</param>
    /// <param name="y">The point's vertical position.</param>
    /// <returns>A value in [0, 1] that the tone is multiplied with.</returns>
    public static double Openness(FieldRect? form, double x, double y)
    {
        if (form is not { } rect)
        {
            return 1;
        }

        var dx = Math.Max(Math.Max(rect.Left - Margin - x, 0), x - (rect.Right + Margin));
        var dy = Math.Max(Math.Max(rect.Top - Margin - y, 0), y - (rect.Bottom + Margin));
        return FieldNoise.Smooth(0, FadeDistance, Math.Sqrt(dx * dx + dy * dy));
    }
}
