using Chainsaw.App.Graph;

namespace Chainsaw.App.Motion;

/// <summary>
/// The graph's smooth zoom, as in Gawk (the user's decision 2026-11-07 on Gawk's soft motions, and 2026-11-08 on the
/// zoom): a turn of the wheel, a touchpad's small steps or + and − set a zoom to go to, and the viewport eases there
/// frame by frame with the point under the pointer standing still. Steps that come while a zoom is under way add to
/// where it is going, so that a quick turn of the wheel zooms far in one motion without jumps.
/// </summary>
/// <remarks>
/// Without WPF, so that it can be tested: the canvas gives it the time of each frame. The zoom eases on its logarithm,
/// so that zooming in and out feel the same step for step. It holds no viewport of its own: each frame starts from the
/// viewport on screen, so that nothing jumps if something else moved the surface in between.
/// </remarks>
public sealed class ZoomMotion
{
    /// <summary>The ease's time constant, in seconds: most of the way is done within a few tenths of a second.</summary>
    public const double TimeConstant = 0.07;

    /// <summary>How close to the target, in the logarithm of the zoom, the ease lands on it.</summary>
    public const double Snap = 0.002;

    /// <summary>The wheel's delta for one notch, as Windows reports it.</summary>
    public const double NotchDelta = 120;

    /// <summary>The zoom being eased to, or <see langword="null"/> while no zoom is under way.</summary>
    private double? _target;

    /// <summary>The point on screen that stands still while the zoom runs.</summary>
    private GraphPoint _anchor;

    /// <summary>Gets whether a zoom is under way.</summary>
    public bool IsRunning => _target is not null;

    /// <summary>Gets the zoom being eased to, or <see langword="null"/> while no zoom is under way.</summary>
    public double? Target => _target;

    /// <summary>
    /// Gets how much a turn of the wheel zooms: <see cref="Viewport.WheelStep"/> for a notch, and a share of it for the
    /// touchpad's smaller steps, so that the zoom follows the fingers.
    /// </summary>
    /// <param name="delta">The wheel's delta; above 0 zooms in.</param>
    /// <returns>The factor.</returns>
    public static double WheelFactor(double delta) => Math.Pow(Viewport.WheelStep, delta / NotchDelta);

    /// <summary>
    /// Asks to zoom by a factor around a point on screen: from the zoom already under way, if any, so that steps add
    /// up. With animations the viewport stays for now and <see cref="Step"/> eases it; without, it jumps at once.
    /// </summary>
    /// <param name="current">The viewport on screen.</param>
    /// <param name="anchor">The point on screen that stays where it is.</param>
    /// <param name="factor">How much to zoom; above 1 zooms in.</param>
    /// <param name="animate">Whether Windows shows animations; without, the zoom is a jump.</param>
    /// <returns>The viewport to show now.</returns>
    public Viewport Aim(Viewport current, GraphPoint anchor, double factor, bool animate)
    {
        ArgumentNullException.ThrowIfNull(current);
        var target = Math.Clamp((_target ?? current.Zoom) * factor, Viewport.MinimumZoom, Viewport.MaximumZoom);
        _anchor = anchor;
        if (!animate)
        {
            _target = null;
            return current.ZoomTo(anchor, target);
        }

        _target = target == current.Zoom ? null : target;
        return current;
    }

    /// <summary>
    /// Eases the viewport one frame towards the zoom asked for, keeping the anchor where it is; the zoom ends once it
    /// lands on the target.
    /// </summary>
    /// <param name="current">The viewport on screen.</param>
    /// <param name="seconds">The time since the previous frame.</param>
    /// <returns>The viewport for this frame; the same one when no zoom is under way.</returns>
    public Viewport Step(Viewport current, double seconds)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (_target is not { } target)
        {
            return current;
        }

        var goal = Math.Log(target);
        var eased = Easing.Toward(Math.Log(current.Zoom), goal, seconds, TimeConstant, Snap);
        if (eased == goal)
        {
            _target = null;
            return current.ZoomTo(_anchor, target);
        }

        return current.ZoomTo(_anchor, Math.Exp(eased));
    }

    /// <summary>Stops the zoom where it is, as when a pan or a press takes hold of the surface.</summary>
    public void Stop() => _target = null;
}
