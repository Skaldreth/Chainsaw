namespace Chainsaw.App.Field;

/// <summary>
/// The lantern (sketch ID7): while Ctrl is held, a soft light follows the mouse over the field and leaves a short
/// trail that fades. It lights up as Ctrl is pressed and fades out when it is let go.
/// </summary>
public sealed class Lantern
{
    /// <summary>How long a point of the trail lives, in seconds.</summary>
    private const double TrailLife = 0.6;

    /// <summary>How many points the trail keeps at most.</summary>
    private const int TrailLength = 12;

    /// <summary>The points the light has left behind, oldest first.</summary>
    private readonly List<TrailPoint> _trail = [];

    /// <summary>Gets the light's horizontal position.</summary>
    public double X { get; private set; }

    /// <summary>Gets the light's vertical position.</summary>
    public double Y { get; private set; }

    /// <summary>Gets how strong the light is, from 0 (off) to 1.</summary>
    public double Strength { get; private set; }

    /// <summary>Gets how many points the trail has.</summary>
    public int TrailCount => _trail.Count;

    /// <summary>Gets whether the lantern still shows anything, so that a still field must be drawn again.</summary>
    public bool IsActive => Strength > 0 || _trail.Count > 0;

    /// <summary>Moves the light towards the mouse and lets it rise or fade.</summary>
    /// <param name="seconds">The time since the last step, in seconds.</param>
    /// <param name="pointer">The mouse over the field, or <see langword="null"/> when it is elsewhere.</param>
    public void Step(double seconds, FieldPointer? pointer)
    {
        var on = pointer is { Ctrl: true };
        if (pointer is { Ctrl: true } p)
        {
            Follow(seconds, p);
        }

        Strength = Math.Clamp(Strength + (on ? seconds * 6 : -seconds * 3), 0, 1);
        for (var i = 0; i < _trail.Count; i++)
        {
            _trail[i] = _trail[i] with { Age = _trail[i].Age + seconds };
        }

        _trail.RemoveAll(point => point.Age >= TrailLife);
        if (_trail.Count > TrailLength)
        {
            _trail.RemoveRange(0, _trail.Count - TrailLength);
        }
    }

    /// <summary>Gives how much the light reaches a point: the light itself and its fading trail.</summary>
    /// <param name="x">The point's horizontal position.</param>
    /// <param name="y">The point's vertical position.</param>
    /// <param name="radiusSquared">The square of the light's reach.</param>
    /// <returns>A value in [0, 1].</returns>
    public double Glow(double x, double y, double radiusSquared)
    {
        if (Strength <= 0)
        {
            return 0;
        }

        var glow = Strength * Math.Exp(-(Square(x - X) + Square(y - Y)) / radiusSquared);
        foreach (var point in _trail)
        {
            var distance = Square(x - point.X) + Square(y - point.Y);
            glow += (1 - point.Age / TrailLife) * 0.5 * Strength * Math.Exp(-distance / (radiusSquared * 0.55));
        }

        return Math.Min(glow, 1);
    }

    /// <summary>Moves the light part of the way to the mouse, and leaves a trail point when it moved.</summary>
    /// <param name="seconds">The time since the last step.</param>
    /// <param name="pointer">The mouse.</param>
    private void Follow(double seconds, FieldPointer pointer)
    {
        if (Strength == 0)
        {
            X = pointer.X;
            Y = pointer.Y;
        }

        var (oldX, oldY) = (X, Y);
        var k = 1 - Math.Exp(-9 * seconds);
        X += (pointer.X - X) * k;
        Y += (pointer.Y - Y) * k;
        if (Math.Sqrt(Square(X - oldX) + Square(Y - oldY)) > 2)
        {
            _trail.Add(new TrailPoint(oldX, oldY, 0));
        }
    }

    /// <summary>Squares a number.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The number times itself.</returns>
    private static double Square(double value) => value * value;

    /// <summary>A point the light has left behind.</summary>
    /// <param name="X">The horizontal position.</param>
    /// <param name="Y">The vertical position.</param>
    /// <param name="Age">How long ago it was left, in seconds.</param>
    private readonly record struct TrailPoint(double X, double Y, double Age);
}
