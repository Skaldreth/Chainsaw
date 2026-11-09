namespace Chainsaw.App.Figure;

/// <summary>
/// The figure under the lock screen's form (sketch ID8f, the user's decision 2026-11-08): two chains that come in from
/// either side, and between them a long link whose two bars twist into a double helix, widest in the middle, that turns
/// whole turns so the right chain's links come to rest upright. This is the figure at one moment; <see cref="BandMotion"/>
/// moves it. It knows nothing of WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
/// <remarks>
/// Everything is laid out along the band's axis, which runs left to right across the page: <em>along</em> is the
/// distance from the page's left edge, <em>across</em> the distance above the axis, both in device-independent pixels.
/// </remarks>
public sealed class ChainBand
{
    /// <summary>The logo's units to pixels on the page.</summary>
    public const double Scale = 0.8;

    /// <summary>How far above the page's bottom edge the axis runs, so the form has the page above it.</summary>
    public const double AxisFromBottom = 204;

    /// <summary>The highest the axis may lie on a low page, as a share of the page's height from the top.</summary>
    public const double HighestAxis = 0.55;

    /// <summary>How far in from each edge the helix starts, as a share of the page's width.</summary>
    public const double ChainShare = 0.3;

    /// <summary>How much wider than the chain the helix grows in its middle: 160 % more.</summary>
    public const double Widening = 1.6;

    /// <summary>How steeply a strand crosses the axis (across per along) before the turn is rounded to whole turns.</summary>
    public const double CrossingSlope = 0.9;

    /// <summary>The distance between two rungs along the axis, in pixels.</summary>
    public const double RungSpacing = 22;

    /// <summary>A rung's width, in the logo's units.</summary>
    public const double RungWidth = 5;

    /// <summary>How many shades the strands are drawn in, from the darkest behind to the accent in front.</summary>
    public const int Shades = 16;

    /// <summary>A strand's colour where it lies deepest behind, as a share of the accent.</summary>
    public const double Darkest = 0.55;

    /// <summary>The step along the axis between two points of a strand, in pixels.</summary>
    public const double StrandStep = 2;

    /// <summary>How much the second strand's turn falls behind half a turn once twisted: the helix's two grooves.</summary>
    private const double GrooveShare = 0.28;

    /// <summary>How far into the twist a strand may pass behind the other; before that, both are in front.</summary>
    private const double BehindFrom = 0.6;

    /// <summary>How far into the twist the rungs begin.</summary>
    private const double RungsFrom = 0.55;

    /// <summary>The shortest rung drawn, in pixels; shorter ones are hidden by the strands where they cross.</summary>
    private const double ShortestRung = 6;

    /// <summary>The depth at which a strand is darkest; deeper it stays so.</summary>
    private const double DarkFrom = -0.6;

    /// <summary>The depth at which a strand has reached the accent; nearer it stays so.</summary>
    private const double LightFrom = 0.2;

    /// <summary>The links whose joints lie off the page to the left, before the first that shows.</summary>
    private const int JointsBefore = 2;

    /// <summary>The share of the helix's length over which the twist comes in at either end.</summary>
    private const double TwistIn = 0.28;

    /// <summary>How the pose moves the figure this moment.</summary>
    private readonly BandPose _pose;

    /// <summary>The strands' turn, every <see cref="StrandStep"/> from <see cref="Start"/>, summed up once.</summary>
    private readonly double[] _turns;

    /// <summary>Lays the figure out on a page.</summary>
    /// <param name="width">The page's width, in device-independent pixels.</param>
    /// <param name="height">The page's height.</param>
    /// <param name="pose">How the figure stands this moment; at rest when left out.</param>
    public ChainBand(double width, double height, BandPose? pose = null)
    {
        _pose = pose ?? BandPose.Rest;
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        AxisY = Math.Max(Height * HighestAxis, Height - AxisFromBottom);
        Radius = ChainLink.BarCenter * Scale;
        Middle = ChainLink.BarOffset * Scale;

        var pitch = ChainLink.Pitch * Scale;
        var barsEnd = ChainLink.BarsEnd * Scale;
        var first = -JointsBefore * pitch;
        LinksPerSide = Math.Max(1, (int)Math.Round(((Width * ChainShare) - barsEnd - first) / pitch));
        var start = first + (LinksPerSide * pitch) + barsEnd;
        var end = Math.Max(start + pitch, Width - start);
        Start = start + _pose.Slide;
        End = end - _pose.Slide;
        Centre = (Start + End) / 2;
        LeftJoints = Enumerable.Range(0, LinksPerSide + 1).Select(k => first + (k * pitch) + _pose.Slide).ToList();
        RightJoints = Enumerable.Range(0, LinksPerSide + 1).Select(k => End + barsEnd + (k * pitch)).ToList();
        _turns = SumTurns(out var wholeTurns);
        WholeTurns = wholeTurns;
    }

    /// <summary>Gets the page's width.</summary>
    public double Width { get; }

    /// <summary>Gets the page's height.</summary>
    public double Height { get; }

    /// <summary>Gets where the axis runs, from the page's top edge.</summary>
    public double AxisY { get; }

    /// <summary>Gets the strands' distance from the middle where they leave the bars.</summary>
    public double Radius { get; }

    /// <summary>Gets where the middle between the bars lies across the axis, which the strands turn around.</summary>
    public double Middle { get; }

    /// <summary>Gets how many links each chain shows before the helix.</summary>
    public int LinksPerSide { get; }

    /// <summary>Gets where along the axis the left chain gives way to the helix.</summary>
    public double Start { get; }

    /// <summary>Gets where along the axis the helix gives way to the right chain.</summary>
    public double End { get; }

    /// <summary>Gets the helix's middle along the axis.</summary>
    public double Centre { get; }

    /// <summary>Gets the left chain's joints along the axis, from the left; all but the last have their bars below.</summary>
    public IReadOnlyList<double> LeftJoints { get; }

    /// <summary>Gets the right chain's joints along the axis, from the helix outward; each has its bars beyond it.</summary>
    public IReadOnlyList<double> RightJoints { get; }

    /// <summary>Gets how many whole turns the helix makes once twisted, so the right chain rests upright.</summary>
    public int WholeTurns { get; }

    /// <summary>Gets the strands' turn where they reach the right chain, which turns the right chain about its axis.</summary>
    public double EndTurn => _turns[^1];

    /// <summary>
    /// Gets how wide the right chain's links look as it turns about its axis with the helix: 1 upright, 0 edge on,
    /// below 0 turned over.
    /// </summary>
    public double RightSquash => Math.Cos(EndTurn);

    /// <summary>Gets a strand's thickness, the bars' width, in pixels.</summary>
    public static double StrandThickness => ChainLink.BarWidth * Scale;

    /// <summary>Gets a rung's thickness, in pixels.</summary>
    public static double RungThickness => RungWidth * Scale;

    /// <summary>Gives a point on the page at a place along the axis and across it.</summary>
    /// <param name="along">The place along the axis.</param>
    /// <param name="across">The distance above the axis.</param>
    /// <returns>The point.</returns>
    public FigurePoint At(double along, double across) => new(along, AxisY - across);

    /// <summary>Gives how far the twist has come in at a place: 0 at the chains, 1 in the middle.</summary>
    /// <param name="along">The place along the axis.</param>
    /// <returns>The envelope, from 0 to 1.</returns>
    public double Envelope(double along)
    {
        var u = (along - Start) / Math.Max(1, End - Start);
        return Smooth(0.02, TwistIn, u) * Smooth(0.98, 1 - TwistIn, u);
    }

    /// <summary>
    /// Gives the two strands at a place along the axis. Before <see cref="Start"/> and after <see cref="End"/> they run
    /// straight on as the bars; between, they twist as far as the pose lets them.
    /// </summary>
    /// <param name="along">The place along the axis.</param>
    /// <returns>The strands there.</returns>
    public HelixSlice Helix(double along)
    {
        var envelope = Envelope(along);
        var twist = envelope * Math.Clamp(_pose.Twist, 0, 1);
        var radius = RadiusAt(along, _pose.Twist);
        var first = RawTurn(along);
        if (_pose.Flat > 0)
        {
            // Once a lens lies in the middle, its strands are held apart for longer, so it squares off.
            var lens = first - _pose.LensTurn;
            const double quarter = Math.PI / 2;
            if (Math.Abs(lens) < quarter)
            {
                first = _pose.LensTurn + (Math.Sign(lens) * quarter * Math.Pow(Math.Abs(lens) / quarter, 1 + (3 * _pose.Flat)));
            }
        }

        var groove = GrooveShare * envelope * Math.Max(0, _pose.Twist) * (1 - _pose.Flat);
        var second = first + (Math.PI * (1 - groove));
        return new HelixSlice(
            twist,
            Middle + (radius * Math.Cos(first)),
            Middle + (radius * Math.Cos(second)),
            Math.Sin(first),
            Math.Sin(second),
            first - _pose.LensTurn);
    }

    /// <summary>Gives strand one's turn at a place, with the pose's turn about the axis but before any squaring off.</summary>
    /// <param name="along">The place along the axis.</param>
    /// <returns>The turn, in radians.</returns>
    public double RawTurn(double along)
    {
        var place = Math.Clamp((along - Start) / StrandStep, 0, _turns.Length - 1);
        var index = Math.Min((int)place, _turns.Length - 2);
        var summed = _turns[index] + ((place - index) * (_turns[index + 1] - _turns[index]));
        return summed + (_pose.Shift * Envelope(along));
    }

    /// <summary>
    /// Gives the turn to the right that brings a whole lens into the middle of the helix, where its strands lie furthest
    /// apart: the shape that becomes the node Nytt ärende when the vault opens.
    /// </summary>
    /// <returns>The turn at the middle to aim for, and how far that is from where it stands.</returns>
    public (double LensTurn, double Shift) LensAhead()
    {
        var at = RawTurn(Centre);
        var lens = Math.Ceiling((at / Math.PI) + 0.2) * Math.PI;
        return (lens, lens - at);
    }

    /// <summary>Gives where the lens in the middle starts and ends: the crossings on either side of the middle.</summary>
    /// <returns>Its two ends along the axis.</returns>
    public (double From, double To) Lens()
    {
        var from = Centre;
        var to = Centre;
        while (from > Start && Math.Abs(Helix(from).Lens) < Math.PI / 2)
        {
            from -= StrandStep;
        }

        while (to < End && Math.Abs(Helix(to).Lens) < Math.PI / 2)
        {
            to += StrandStep;
        }

        return (from, to);
    }

    /// <summary>
    /// Gives a strand's shade at a depth, as a share of the accent: dark behind, the accent in front and a smooth step
    /// between, so that a strand fades as it turns away. Until the twist is under way both lie in front, as the bars.
    /// </summary>
    /// <param name="depth">The strand's depth, from -1 behind to 1 in front.</param>
    /// <param name="twist">How far the twist has come in.</param>
    /// <returns>The shade's step, from 0 for the darkest to <see cref="Shades"/> - 1 for the accent.</returns>
    public static int ShadeStep(double depth, double twist) =>
        ShadeStepSeen((twist * depth) + (1 - twist));

    /// <summary>Gives the shade's step for how much of a strand is seen, from -1 behind to 1 in front.</summary>
    /// <param name="seen">How much is seen.</param>
    /// <returns>The step.</returns>
    public static int ShadeStepSeen(double seen) => (int)Math.Round(Smooth(DarkFrom, LightFrom, seen) * (Shades - 1));

    /// <summary>Gives the share of the accent a shade's step stands for.</summary>
    /// <param name="step">The step.</param>
    /// <returns>The share, from <see cref="Darkest"/> to 1.</returns>
    public static double ShadeOf(int step) => Darkest + ((1 - Darkest) * step / (Shades - 1));

    /// <summary>Gives whether a strand lies in front of the other at a depth, as far as the twist has come in.</summary>
    /// <param name="depth">The strand's depth.</param>
    /// <param name="twist">How far the twist has come in.</param>
    /// <returns>Whether it is drawn in front.</returns>
    public static bool InFront(double depth, double twist) => depth >= -0.05 || twist < BehindFrom;

    /// <summary>Gives whether a rung is drawn at a slice, and where its two ends lie across the axis.</summary>
    /// <param name="slice">The strands there.</param>
    /// <param name="ends">The rung's ends, from strand one's edge to strand two's.</param>
    /// <returns>Whether the rung is drawn there.</returns>
    public static bool RungAt(HelixSlice slice, out (double From, double To) ends)
    {
        var half = StrandThickness / 2;
        var direction = Math.Sign(slice.Second - slice.First);
        ends = (slice.First + (direction * half), slice.Second - (direction * half));
        return slice.Twist >= RungsFrom && (ends.To - ends.From) * direction >= ShortestRung;
    }

    /// <summary>The smooth step from 0 to 1 between two edges; edges in falling order give a falling step.</summary>
    /// <param name="from">Where it starts to rise.</param>
    /// <param name="to">Where it reaches 1.</param>
    /// <param name="x">The place.</param>
    /// <returns>The step.</returns>
    public static double Smooth(double from, double to, double x)
    {
        var t = Math.Clamp((x - from) / (to - from), 0, 1);
        return t * t * (3 - (2 * t));
    }

    /// <summary>Gives the strands' distance from the middle at a place, widest in the helix's middle.</summary>
    /// <param name="along">The place along the axis.</param>
    /// <param name="twist">How far the twist has come in; the widening follows it, past 1 while it springs.</param>
    /// <returns>The distance, in pixels.</returns>
    private double RadiusAt(double along, double twist)
    {
        var u = Math.Clamp((along - Start) / Math.Max(1, End - Start), 0, 1);
        return Radius * Math.Max(0.3, 1 + (Widening * twist * Math.Pow(Math.Sin(Math.PI * u), 2)));
    }

    /// <summary>
    /// Sums the turn along the helix: at each step it turns by the crossing slope over the radius, as far as the twist has
    /// come in, scaled so the whole twisted helix makes whole turns and the right chain comes to rest upright.
    /// </summary>
    /// <param name="wholeTurns">How many whole turns the twisted helix makes.</param>
    /// <returns>The turn at every <see cref="StrandStep"/> from <see cref="Start"/>.</returns>
    private double[] SumTurns(out int wholeTurns)
    {
        var count = Math.Max(2, (int)Math.Ceiling((End - Start) / StrandStep) + 1);
        var full = 0.0;
        for (var i = 1; i < count; i++)
        {
            var middle = Start + ((i - 0.5) * StrandStep);
            full += CrossingSlope * Envelope(middle) / RadiusAt(middle, 1) * StrandStep;
        }

        wholeTurns = Math.Max(1, (int)Math.Round(full / (2 * Math.PI)));
        var scale = full > 0 ? wholeTurns * 2 * Math.PI / full : 0;
        var twist = Math.Max(0, _pose.Twist);
        var turns = new double[count];
        for (var i = 1; i < count; i++)
        {
            var middle = Start + ((i - 0.5) * StrandStep);
            turns[i] = turns[i - 1] + (scale * CrossingSlope * Envelope(middle) * twist / RadiusAt(middle, _pose.Twist) * StrandStep);
        }

        return turns;
    }
}

/// <summary>How the band stands at one moment, set by <see cref="BandMotion"/>.</summary>
/// <param name="Slide">How far the chains have yet to slide in from the sides, in pixels; 0 once in.</param>
/// <param name="Twist">How far the helix has twisted, from 0 (two straight bars) to 1, past 1 while it springs.</param>
/// <param name="Shift">How far the helix has turned about its axis towards a lens in the middle, in radians.</param>
/// <param name="Flat">How far the lens in the middle has squared off, from 0 to 1.</param>
/// <param name="LensTurn">The turn at the middle where the lens lies.</param>
public sealed record BandPose(double Slide, double Twist, double Shift, double Flat, double LensTurn)
{
    /// <summary>Gets the band at rest: in, twisted and still.</summary>
    public static BandPose Rest { get; } = new(0, 1, 0, 0, 0);
}

/// <summary>A point on the page, in device-independent pixels.</summary>
/// <param name="X">From the page's left edge.</param>
/// <param name="Y">From the page's top edge.</param>
public readonly record struct FigurePoint(double X, double Y);

/// <summary>The two strands at one place along the axis.</summary>
/// <param name="Twist">How far the twist has come in, from 0 at the chains to 1.</param>
/// <param name="First">Strand one's place across the axis.</param>
/// <param name="Second">Strand two's place across the axis.</param>
/// <param name="FirstDepth">Strand one's depth: below zero it passes behind.</param>
/// <param name="SecondDepth">Strand two's depth.</param>
/// <param name="Lens">Strand one's turn from the lens in the middle: within a quarter turn it is inside the lens.</param>
public readonly record struct HelixSlice(
    double Twist, double First, double Second, double FirstDepth, double SecondDepth, double Lens);
