namespace Chainsaw.App.Figure;

/// <summary>
/// The figure behind the lock screen and the first-run setup (sketch ID8c, the user's decision 2026-11-07): the chain
/// comes in on the slant from the top, and about halfway down the page its links let go. The two bars of the last link
/// run on as the two strands of a double helix, as wide as the bars, that opens up towards the bottom of the page, with
/// the base pairs as rungs between them. It knows nothing of WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
/// <remarks>
/// Everything is laid out along the chain's axis: <em>along</em> runs down the axis from where it meets the top of the
/// page, <em>across</em> runs at right angles to it, both in device-independent pixels.
/// </remarks>
public sealed class DnaFigure
{
    /// <summary>The chain's slant from the vertical, in degrees, as the logo's.</summary>
    public const double Slant = 40;

    /// <summary>The logo's units to pixels on the page.</summary>
    public const double Scale = 0.75;

    /// <summary>The page width the sketch is drawn for; a narrower page moves the chain left with it.</summary>
    public const double DesignWidth = 1440;

    /// <summary>Where the chain's axis meets the top of the page on a page of <see cref="DesignWidth"/>.</summary>
    public const double TopX = 640;

    /// <summary>How far down the chain the links let go, as a share of the axis on the page.</summary>
    public const double Split = 0.5;

    /// <summary>How much wider than the chain the helix has grown at the bottom: 260 % more.</summary>
    public const double Widening = 2.6;

    /// <summary>
    /// How steeply a strand crosses the axis (across per along). The twist slows as the helix widens, so that the
    /// strands cross at this angle all the way down and the helix never lies down in flat bands.
    /// </summary>
    public const double CrossingSlope = 0.76;

    /// <summary>The distance between two rungs along the axis, in pixels.</summary>
    public const double RungSpacing = 22;

    /// <summary>A rung's width, in the logo's units.</summary>
    public const double RungWidth = 5;

    /// <summary>The share of the helix over which the twist starts.</summary>
    private const double TwistShare = 0.3;

    /// <summary>
    /// How far the strands run straight on from the bars before they start to twist and widen, in pixels, so that the
    /// chain lets go of them without a bend at the bars' ends.
    /// </summary>
    private const double CalmLength = 4;

    /// <summary>How many shades the strands are drawn in, from the darkest behind to the accent in front.</summary>
    public const int Shades = 16;

    /// <summary>A strand's colour where it lies deepest behind, as a share of the accent.</summary>
    public const double Darkest = 0.55;

    /// <summary>The depth at which a strand is darkest; deeper it stays so.</summary>
    private const double DarkFrom = -0.6;

    /// <summary>The depth at which a strand has reached the accent; nearer it stays so.</summary>
    private const double LightFrom = 0.2;

    /// <summary>How much the second strand's turn falls behind half a turn once twisted: the helix's two grooves.</summary>
    private const double GrooveShare = 0.28;

    /// <summary>How far into the twist a strand may pass behind the other; before that, both are in front.</summary>
    private const double BehindFrom = 0.6;

    /// <summary>How far into the twist the rungs begin.</summary>
    private const double RungsFrom = 0.55;

    /// <summary>The shortest rung drawn, in pixels; shorter ones are hidden by the strands where they cross.</summary>
    private const double ShortestRung = 6;

    /// <summary>The step along the axis between two points of a strand, in pixels.</summary>
    private const double StrandStep = 2;

    /// <summary>Joints above the page, so that the chain enters from above.</summary>
    private const int JointsAbove = 2;

    /// <summary>How far in from the page's left edge the helix's own length ends, so that it widens on the page.</summary>
    private const double LeftMargin = 40;

    /// <summary>How far past the page's edges a strand is still drawn, so that it leaves the page whole.</summary>
    private const double Overdraw = 200;

    /// <summary>The axis's direction down the page.</summary>
    private readonly FigurePoint _along;

    /// <summary>The direction across the axis, to the axis's right as it runs down.</summary>
    private readonly FigurePoint _across;

    /// <summary>Where the axis meets the top of the page.</summary>
    private readonly double _topX;

    /// <summary>The helix's own length, from its start to where it has grown to its widest.</summary>
    private readonly double _growth;

    /// <summary>Where the twist has fully started.</summary>
    private readonly double _twisted;

    /// <summary>The strands' distance from the axis where they leave the bars.</summary>
    private readonly double _radius;

    /// <summary>Where the middle between the bars lies across the axis, which the strands turn around.</summary>
    private readonly double _middle;

    /// <summary>Where the strands start to twist and widen, <see cref="CalmLength"/> below the bars.</summary>
    private readonly double _calm;

    /// <summary>The strands' turn, every <see cref="StrandStep"/> from <see cref="Start"/>, summed up once.</summary>
    private readonly double[] _turns;

    /// <summary>Lays the figure out on a page.</summary>
    /// <param name="width">The page's width, in device-independent pixels.</param>
    /// <param name="height">The page's height.</param>
    public DnaFigure(double width, double height)
    {
        Width = width;
        Height = height;
        var angle = Slant * Math.PI / 180;
        _along = new FigurePoint(-Math.Sin(angle), Math.Cos(angle));
        _across = new FigurePoint(Math.Cos(angle), Math.Sin(angle));
        _topX = TopX * Math.Min(1, width / DesignWidth);
        _radius = ChainLink.BarCenter * Scale;
        _middle = ChainLink.BarOffset * Scale;

        var pitch = ChainLink.Pitch * Scale;
        var first = -JointsAbove * pitch;
        var bars = ChainLink.BarsEnd * Scale;
        var length = Math.Min(height / _along.Y, Math.Max(0, _topX - LeftMargin) / -_along.X);
        var joints = (int)Math.Round((length * Split - bars - first) / pitch);
        Start = first + joints * pitch + bars;
        End = Math.Min(height / _along.Y + Overdraw / 2, (_topX + Overdraw) / -_along.X);
        _calm = Start + CalmLength;
        _growth = Math.Max(1, length - _calm);
        _twisted = _calm + TwistShare * _growth;
        _turns = SumTurns();

        Joints = Enumerable.Range(0, joints + 1)
            .Select(k => (Place: At(first + k * pitch, 0), HasBars: k < joints))
            .Select(j => new JointPlacement(j.Place.X, j.Place.Y, Slant, Scale, j.HasBars))
            .ToList();
        (Front, Back) = Strands();
        Rungs = LayRungs();
    }

    /// <summary>Gets the page's width.</summary>
    public double Width { get; }

    /// <summary>Gets the page's height.</summary>
    public double Height { get; }

    /// <summary>Gets where along the axis the chain ends and the helix starts.</summary>
    public double Start { get; }

    /// <summary>Gets where along the axis the helix has left the page.</summary>
    public double End { get; }

    /// <summary>Gets the axis's direction down the page.</summary>
    public FigurePoint Along => _along;

    /// <summary>Gets the direction across the axis.</summary>
    public FigurePoint Across => _across;

    /// <summary>
    /// Gets the chain's joints from the top, each with its link's bars below it but the last, whose short bars are the
    /// strands' start.
    /// </summary>
    public IReadOnlyList<JointPlacement> Joints { get; }

    /// <summary>Gets the stretches of the strands in front, drawn last, shaded by their depth.</summary>
    public IReadOnlyList<StrandStretch> Front { get; }

    /// <summary>Gets the stretches of the strands behind the other, drawn first, shaded by their depth.</summary>
    public IReadOnlyList<StrandStretch> Back { get; }

    /// <summary>Gets the rungs between the strands: the base pairs.</summary>
    public IReadOnlyList<Rung> Rungs { get; }

    /// <summary>Gets a strand's thickness, the bars' width, in pixels.</summary>
    public static double StrandThickness => ChainLink.BarWidth * Scale;

    /// <summary>Gets a rung's thickness, in pixels.</summary>
    public static double RungThickness => RungWidth * Scale;

    /// <summary>Gives the point on the page at a place along the axis and across it.</summary>
    /// <param name="along">The place along the axis.</param>
    /// <param name="across">The distance across it.</param>
    /// <returns>The point.</returns>
    public FigurePoint At(double along, double across) => new(
        _topX + _along.X * along + _across.X * across,
        _along.Y * along + _across.Y * across);

    /// <summary>
    /// Gives the two strands at a place along the axis. The helix eases out of the bars and widens to
    /// <see cref="Widening"/> over its own length, and it turns at <see cref="CrossingSlope"/> over its radius as the
    /// twist comes in, so that the strands cross at the same angle however wide the helix has grown and never bend
    /// sharply where the chain lets them go.
    /// </summary>
    /// <param name="along">The place along the axis.</param>
    /// <returns>The strands there.</returns>
    public HelixSlice Helix(double along)
    {
        var radius = Radius(along);
        var twist = Twist(along);
        var first = Turn(along);
        var second = first + Math.PI * (1 - GrooveShare * twist);
        return new HelixSlice(
            twist,
            _middle + radius * Math.Cos(first),
            _middle + radius * Math.Cos(second),
            Math.Sin(first),
            Math.Sin(second));
    }

    /// <summary>
    /// Gives the strands' distance from the axis: the bars' own until the calm run ends, then growing from rest, slowly
    /// at first and steadily towards the bottom, so that the strands leave the bars without a kink.
    /// </summary>
    /// <param name="along">The place along the axis.</param>
    /// <returns>The distance.</returns>
    private double Radius(double along)
    {
        var u = Math.Clamp((along - _calm) / _growth, 0, 1);
        return _radius * (1 + Widening * u * u * (2 - u));
    }

    /// <summary>Gives how far the twist has started, from 0 through the calm run to 1.</summary>
    /// <param name="along">The place along the axis.</param>
    /// <returns>The twist.</returns>
    private double Twist(double along) => Smooth(_calm, _twisted, along);

    /// <summary>Gives the strands' turn at a place along the axis, between the summed steps.</summary>
    /// <param name="along">The place along the axis.</param>
    /// <returns>The turn, in radians.</returns>
    private double Turn(double along)
    {
        var place = Math.Clamp((along - Start) / StrandStep, 0, _turns.Length - 1);
        var index = Math.Min((int)place, _turns.Length - 2);
        return index < 0 ? 0 : _turns[index] + (place - index) * (_turns[index + 1] - _turns[index]);
    }

    /// <summary>
    /// Sums the turn down the helix: at each step it turns by <see cref="CrossingSlope"/> over the radius, as far as the
    /// twist has come in. Turning by the rate rather than scaling the whole turn by the twist keeps the bend gentle where
    /// the twist starts.
    /// </summary>
    /// <returns>The turn at every <see cref="StrandStep"/> from <see cref="Start"/>.</returns>
    private double[] SumTurns()
    {
        var count = Math.Max(2, (int)Math.Ceiling((End - Start) / StrandStep) + 2);
        var turns = new double[count];
        for (var i = 1; i < count; i++)
        {
            var middle = Start + (i - 0.5) * StrandStep;
            turns[i] = turns[i - 1] + CrossingSlope * Twist(middle) / Radius(middle) * StrandStep;
        }

        return turns;
    }

    /// <summary>The smooth step from 0 to 1 between two edges.</summary>
    /// <param name="from">Where it starts to rise.</param>
    /// <param name="to">Where it reaches 1.</param>
    /// <param name="x">The place.</param>
    /// <returns>The step.</returns>
    private static double Smooth(double from, double to, double x)
    {
        var t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>
    /// Gives a strand's shade at a depth, as a share of the accent: dark behind, the accent in front and a smooth step
    /// between, so that a strand fades as it turns away instead of changing colour at once. Until the twist is under way
    /// both strands lie in front, as the bars they leave.
    /// </summary>
    /// <param name="depth">The strand's depth, from -1 behind to 1 in front.</param>
    /// <param name="twist">How far the twist has started.</param>
    /// <returns>The shade's step, from 0 for the darkest to <see cref="Shades"/> - 1 for the accent.</returns>
    private static int ShadeStep(double depth, double twist)
    {
        var seen = twist * depth + (1 - twist);
        return (int)Math.Round(Smooth(DarkFrom, LightFrom, seen) * (Shades - 1));
    }

    /// <summary>Gives the share of the accent a shade's step stands for.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The share, from <see cref="Darkest"/> to 1.</returns>
    private static double ShadeOf(int step) => Darkest + (1 - Darkest) * step / (Shades - 1);

    /// <summary>
    /// Traces both strands from inside the last bars down off the page, cut where the shade steps or where a strand
    /// passes behind the other. Each stretch reaches one step into the next, so that no seam shows between them, and the
    /// stretches behind can be drawn first.
    /// </summary>
    /// <returns>The stretches in front and behind.</returns>
    private (List<StrandStretch> Front, List<StrandStretch> Back) Strands()
    {
        var front = new List<StrandStretch>();
        var back = new List<StrandStretch>();
        var overlap = (ChainLink.BarsEnd - ChainLink.BarsStart) * Scale;
        foreach (var strand in new[] { 1, -1 })
        {
            // A strand starts inside the last joint, where the last link's bars would start, and runs on as its bars.
            var points = new List<FigurePoint> { At(Start - overlap, _middle + strand * _radius) };
            (int Step, bool InFront)? current = null;
            for (var along = Start; along <= End; along += StrandStep)
            {
                var slice = Helix(along);
                var point = At(along, strand > 0 ? slice.First : slice.Second);
                var depth = strand > 0 ? slice.FirstDepth : slice.SecondDepth;
                var key = (ShadeStep(depth, slice.Twist), depth >= -0.05 || slice.Twist < BehindFrom);
                if (current is { } was && was != key)
                {
                    points.Add(point);
                    (was.InFront ? front : back).Add(new StrandStretch(points, ShadeOf(was.Step)));
                    points = [points[^2]];
                }

                current = key;
                points.Add(point);
            }

            if (points.Count > 1 && current is { } last)
            {
                (last.InFront ? front : back).Add(new StrandStretch(points, ShadeOf(last.Step)));
            }
        }

        return (front, back);
    }

    /// <summary>Lays a rung every <see cref="RungSpacing"/> once the twist is under way, from strand to strand.</summary>
    /// <returns>The rungs.</returns>
    private List<Rung> LayRungs()
    {
        var rungs = new List<Rung>();
        var half = StrandThickness / 2;
        for (var along = Start + RungSpacing / 2; along <= End; along += RungSpacing)
        {
            var slice = Helix(along);
            if (slice.Twist < RungsFrom)
            {
                continue;
            }

            var direction = Math.Sign(slice.Second - slice.First);
            var from = slice.First + direction * half;
            var to = slice.Second - direction * half;
            if ((to - from) * direction < ShortestRung)
            {
                continue;
            }

            rungs.Add(new Rung(At(along, from), At(along, (from + to) / 2), At(along, to)));
        }

        return rungs;
    }
}

/// <summary>A point on the page, in device-independent pixels.</summary>
/// <param name="X">From the page's left edge.</param>
/// <param name="Y">From the page's top edge.</param>
public readonly record struct FigurePoint(double X, double Y);

/// <summary>Where one of the chain's joints stands, with its link's bars below it.</summary>
/// <param name="X">The joint's centre, from the page's left edge.</param>
/// <param name="Y">The joint's centre, from the page's top edge.</param>
/// <param name="Angle">Its turn clockwise from upright, in degrees.</param>
/// <param name="Scale">The logo's units to pixels.</param>
/// <param name="HasBars">Whether its link's bars are drawn below it; the last joint's are the strands' start.</param>
public readonly record struct JointPlacement(double X, double Y, double Angle, double Scale, bool HasBars);

/// <summary>A stretch of one strand in one shade.</summary>
/// <param name="Points">Its points, from the top.</param>
/// <param name="Shade">Its colour as a share of the accent, from <see cref="DnaFigure.Darkest"/> to 1.</param>
public sealed record StrandStretch(IReadOnlyList<FigurePoint> Points, double Shade);

/// <summary>A rung between the strands: a light half from strand one and a darker half from strand two.</summary>
/// <param name="First">Where it leaves strand one.</param>
/// <param name="Middle">Where the two halves meet.</param>
/// <param name="Second">Where it reaches strand two.</param>
public readonly record struct Rung(FigurePoint First, FigurePoint Middle, FigurePoint Second);

/// <summary>The two strands at one place along the axis.</summary>
/// <param name="Twist">How far the twist has started, from 0 at the chain to 1.</param>
/// <param name="First">Strand one's place across the axis.</param>
/// <param name="Second">Strand two's place across the axis.</param>
/// <param name="FirstDepth">Strand one's depth: below zero it passes behind.</param>
/// <param name="SecondDepth">Strand two's depth.</param>
public readonly record struct HelixSlice(double Twist, double First, double Second, double FirstDepth, double SecondDepth);
