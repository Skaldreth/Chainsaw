namespace Chainsaw.App.Figure;

/// <summary>
/// The particles around the helix (sketches ID8c and ID8f): characters that come loose from the strands' edges, now and
/// then a base, and drift slowly away from the axis while they fade in and out. The chains let nothing go. A puff lets
/// many go at once, faster and shorter, when the long link lets go or a wrong key jerks the band, and a typed character
/// lets one base go. It knows nothing of WPF, so that it is tested in Chainsaw.App.Tests.
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

    /// <summary>How many particles a puff may add above <see cref="Count"/>, so a wild typist never floods the page.</summary>
    private const int MostAtOnce = Count + 160;

    /// <summary>How often a particle is a base.</summary>
    private const double BaseShare = 0.14;

    /// <summary>How far into the twist the strands start to let particles go.</summary>
    private const double LooseFrom = 0.3;

    /// <summary>How far from the chains particles start, so that none comes from a chain.</summary>
    private const double ClearOfChain = 20;

    /// <summary>How many places are tried for a new particle before giving up for now.</summary>
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

    /// <summary>Lets every particle go at once, as when the figure starts again.</summary>
    public void Clear() => _items.Clear();

    /// <summary>Lets new particles come loose until there are <see cref="Count"/>, a few a step.</summary>
    /// <param name="band">The band the particles come loose from.</param>
    public void Keep(ChainBand band)
    {
        for (var i = 0; i < 20 && _items.Count < Count; i++)
        {
            if (Spawn(band, puff: false) is not { } particle)
            {
                return;
            }

            _items.Add(particle);
        }
    }

    /// <summary>Lets a puff of particles go from the middle of the helix, faster and shorter-lived than the rest.</summary>
    /// <param name="band">The band.</param>
    /// <param name="count">How many.</param>
    public void Burst(ChainBand band, int count)
    {
        for (var i = 0; i < count && _items.Count < MostAtOnce; i++)
        {
            if (Spawn(band, puff: true) is { } particle)
            {
                _items.Add(particle);
            }
        }
    }

    /// <summary>Lets one base go from the helix, a little faster than the dust: a character was typed.</summary>
    /// <param name="band">The band.</param>
    public void LetGoBase(ChainBand band)
    {
        if (_items.Count >= MostAtOnce || Spawn(band, puff: false) is not { } particle)
        {
            return;
        }

        _items.Add(new Particle(Bases[_random.Next(Bases.Length)], isBase: true, life: 2.2)
        {
            X = particle.X,
            Y = particle.Y,
            VelocityX = particle.VelocityX * 1.8,
            VelocityY = particle.VelocityY * 1.8,
        });
    }

    /// <summary>Moves every particle on, and lets those that have faded out go.</summary>
    /// <param name="seconds">The time since the last step.</param>
    public void Step(double seconds)
    {
        foreach (var particle in _items)
        {
            particle.Age += seconds;
            particle.X += particle.VelocityX * seconds;
            particle.Y += particle.VelocityY * seconds;
        }

        _items.RemoveAll(particle => particle.Age >= particle.Life);
    }

    /// <summary>
    /// Lets a particle come loose at a random place on the twisted helix, just outside one strand's edge, heading away
    /// from the axis on that side and a little along it, at a few pixels a second; a puff's near the middle and faster.
    /// </summary>
    /// <param name="band">The band.</param>
    /// <param name="puff">Whether it belongs to a puff.</param>
    /// <returns>The particle, or <see langword="null"/> when no place on the page was found.</returns>
    private Particle? Spawn(ChainBand band, bool puff)
    {
        var from = band.Start + ClearOfChain;
        var to = puff ? Math.Min(band.End - ClearOfChain, from + 260) : band.End - ClearOfChain;
        if (puff)
        {
            var half = (to - from) / 2;
            from = band.Centre - half;
            to = band.Centre + half;
        }

        for (var attempt = 0; attempt < Tries; attempt++)
        {
            var along = from + (_random.NextDouble() * Math.Max(1, to - from));
            var slice = band.Helix(along);
            if (slice.Twist < LooseFrom && !puff)
            {
                continue;
            }

            var strand = _random.NextDouble() < 0.5 ? slice.First : slice.Second;
            var side = _random.NextDouble() < 0.5 ? -1 : 1;
            var across = strand + (side * ((ChainBand.StrandThickness / 2) + 2 + (_random.NextDouble() * 6)));
            var point = band.At(along, across);
            if (point.X < -Margin || point.Y < -Margin || point.X > band.Width + Margin || point.Y > band.Height + Margin)
            {
                continue;
            }

            var outward = across == band.Middle ? side : Math.Sign(across - band.Middle);
            var speed = puff ? 4 : 1;
            var speedAcross = outward * (5 + (_random.NextDouble() * 12)) * speed;
            var speedAlong = (_random.NextDouble() - 0.5) * 8 * (puff ? 3 : 1);
            var isBase = _random.NextDouble() < BaseShare;
            var character = isBase ? Bases[_random.Next(Bases.Length)] : Dust[_random.Next(Dust.Length)];
            var life = puff ? 1.2 + (_random.NextDouble() * 1.6) : 2.5 + (_random.NextDouble() * 3);
            return new Particle(character, isBase, life)
            {
                X = point.X,
                Y = point.Y,
                VelocityX = speedAlong,
                VelocityY = -speedAcross,
                Age = puff ? 0 : _random.NextDouble() * 0.2 * life,
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
