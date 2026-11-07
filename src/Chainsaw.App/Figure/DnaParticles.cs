namespace Chainsaw.App.Figure;

/// <summary>
/// The particles around the helix (sketch ID8c): characters that come loose from the strands' edges, now and then a
/// base, and drift slowly outward and down the axis while they fade in and out. The chain itself lets nothing go. It
/// knows nothing of WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
/// <remarks>Not thread-safe: stepped from one timer on the UI thread.</remarks>
public sealed class DnaParticles
{
    /// <summary>How many particles there are at a time: the sketch's <em>Lugna</em>.</summary>
    public const int Count = 110;

    /// <summary>The characters of the dust, the faint ones more often.</summary>
    public const string Dust = "..::·+*";

    /// <summary>The bases, which come loose now and then.</summary>
    public const string Bases = "ATGC";

    /// <summary>How often a particle is a base.</summary>
    private const double BaseShare = 0.14;

    /// <summary>How far into the twist the strands start to let particles go.</summary>
    private const double LooseFrom = 0.3;

    /// <summary>How far from the chain's end particles start, so that none comes from the chain.</summary>
    private const double ClearOfChain = 20;

    /// <summary>How many places are tried for a new particle before the step gives up for now.</summary>
    private const int Tries = 4;

    /// <summary>How far outside the page a particle may start.</summary>
    private const double Margin = 10;

    /// <summary>Picks where and how each particle comes loose.</summary>
    private readonly Random _random;

    /// <summary>The particles.</summary>
    private readonly List<Particle> _items = [];

    /// <summary>Creates the particles, with no particle yet.</summary>
    /// <param name="random">Picks where and how each particle comes loose; seeded in the tests.</param>
    public DnaParticles(Random random) => _random = random;

    /// <summary>Gets the particles.</summary>
    public IReadOnlyList<Particle> Items => _items;

    /// <summary>
    /// Fills the figure with particles at ages spread over their lives, so that the page never opens on a bare helix.
    /// </summary>
    /// <param name="figure">The figure the particles come loose from.</param>
    public void Fill(DnaFigure figure)
    {
        _items.Clear();
        for (var i = 0; i < Count * Tries && _items.Count < Count; i++)
        {
            if (Spawn(figure) is { } particle)
            {
                particle.Age = _random.NextDouble() * particle.Life;
                _items.Add(particle);
            }
        }
    }

    /// <summary>Moves every particle on, and lets a new one come loose in place of each that has faded out.</summary>
    /// <param name="figure">The figure the particles come loose from.</param>
    /// <param name="seconds">The time since the last step.</param>
    public void Step(DnaFigure figure, double seconds)
    {
        for (var i = 0; i < _items.Count; i++)
        {
            var particle = _items[i];
            particle.Age += seconds;
            particle.X += particle.VelocityX * seconds;
            particle.Y += particle.VelocityY * seconds;
            if (particle.Age >= particle.Life && Spawn(figure) is { } fresh)
            {
                _items[i] = fresh;
            }
        }
    }

    /// <summary>
    /// Lets a particle come loose at a random place on the twisted helix, just outside one strand's edge, heading away
    /// from the axis on that side and a little down it, at a few pixels a second.
    /// </summary>
    /// <param name="figure">The figure.</param>
    /// <returns>The particle, or <see langword="null"/> when no place on the page was found.</returns>
    private Particle? Spawn(DnaFigure figure)
    {
        for (var attempt = 0; attempt < Tries; attempt++)
        {
            var along = figure.Start + ClearOfChain + _random.NextDouble() * (figure.End - figure.Start - ClearOfChain);
            var slice = figure.Helix(along);
            if (slice.Twist < LooseFrom)
            {
                continue;
            }

            var strand = _random.NextDouble() < 0.5 ? slice.First : slice.Second;
            var side = _random.NextDouble() < 0.5 ? -1 : 1;
            var across = strand + side * (DnaFigure.StrandThickness / 2 + 2 + _random.NextDouble() * 6);
            var point = figure.At(along, across);
            if (point.X < -Margin || point.Y < -Margin
                || point.X > figure.Width + Margin || point.Y > figure.Height + Margin)
            {
                continue;
            }

            var outward = across == 0 ? side : Math.Sign(across);
            var speedAcross = outward * (5 + _random.NextDouble() * 12);
            var speedAlong = 2 + _random.NextDouble() * 6;
            var isBase = _random.NextDouble() < BaseShare;
            var character = isBase ? Bases[_random.Next(Bases.Length)] : Dust[_random.Next(Dust.Length)];
            return new Particle(character, isBase, 2.5 + _random.NextDouble() * 3)
            {
                X = point.X,
                Y = point.Y,
                VelocityX = figure.Along.X * speedAlong + figure.Across.X * speedAcross,
                VelocityY = figure.Along.Y * speedAlong + figure.Across.Y * speedAcross,
            };
        }

        return null;
    }
}

/// <summary>One particle: a character drifting away from the helix.</summary>
/// <param name="character">The character.</param>
/// <param name="isBase">Whether it is a base, drawn a little stronger.</param>
/// <param name="life">How long it lives, in seconds.</param>
public sealed class Particle(char character, bool isBase, double life)
{
    /// <summary>Gets the character.</summary>
    public char Character { get; } = character;

    /// <summary>Gets whether it is a base, drawn a little stronger.</summary>
    public bool IsBase { get; } = isBase;

    /// <summary>Gets how long it lives, in seconds.</summary>
    public double Life { get; } = life;

    /// <summary>Gets or sets its centre, from the page's left edge.</summary>
    public double X { get; set; }

    /// <summary>Gets or sets its centre, from the page's top edge.</summary>
    public double Y { get; set; }

    /// <summary>Gets or sets its speed to the right, in pixels a second.</summary>
    public double VelocityX { get; set; }

    /// <summary>Gets or sets its speed down, in pixels a second.</summary>
    public double VelocityY { get; set; }

    /// <summary>Gets or sets how long it has lived, in seconds.</summary>
    public double Age { get; set; }

    /// <summary>Gets how strongly it shows: it fades in and out over its life, a base a little stronger than dust.</summary>
    public double Alpha => Math.Sin(Math.PI * Math.Clamp(Age / Life, 0, 1)) * (IsBase ? 0.85 : 0.6);
}
