namespace Chainsaw.App.Figure;

/// <summary>
/// Moves the lock screen's band (sketches ID8f and LS1b, the user's decisions 2026-11-08) and says what to draw at each
/// moment. At the start the chains slide in, stop hard, the long link between them is pulled taut and trembles, and then
/// lets go into the helix with a springing jolt. Afterwards the band hangs as a taut rope held at the window's edges,
/// with a little sag: when the window is dragged it swings as one bow and settles quickly. It answers the key: each
/// character lets a base go, a wrong key jerks it, pulses it red and lets a puff of characters go, and the right key
/// pulls it straight, turns a whole lens into the middle, squares it off and shrinks it into the node Nytt ärende.
/// It knows nothing of WPF, so that it is tested in Chainsaw.App.Tests.
/// </summary>
/// <remarks>Not thread-safe: stepped from one timer on the UI thread.</remarks>
public sealed class BandMotion
{
    /// <summary>When the chains have slid in and stop hard, in seconds from the start.</summary>
    public const double ChainsIn = 0.8;

    /// <summary>When the long link lets go into the helix, in seconds from the start.</summary>
    public const double LetGo = 1.5;

    /// <summary>When the start is over and only the particles live, in seconds from the start.</summary>
    public const double StartOver = 4;

    /// <summary>How long the red pulse after a wrong key lasts, in seconds.</summary>
    public const double PulseLength = 0.7;

    /// <summary>When the form has faded after the right key, in seconds from it.</summary>
    public static readonly (double From, double To) FormFade = (0.35, 0.8);

    /// <summary>When the lens becomes the node after the right key, in seconds from it.</summary>
    public static readonly (double From, double To) Morph = (0.85, 1.75);

    /// <summary>When the page fades and shows the shell with the real node under the drawn one, in seconds from the key.</summary>
    public static readonly (double From, double To) Reveal = (1.75, 2.15);

    /// <summary>How far the chains slide in from the sides at the start, in pixels.</summary>
    private const double SlideIn = 560;

    /// <summary>How far the chains slide out when the lens becomes the node, in pixels.</summary>
    private const double SlideOut = 520;

    /// <summary>The rope's lowest tone: stram (the user's decision 2026-11-08), in swings a second.</summary>
    private const double Tone = 2.2;

    /// <summary>How quickly the rope's swing dies away, as a share of critical damping.</summary>
    private const double Damping = 0.1;

    /// <summary>How far the rope may swing at most, in pixels, so a wild drag never tears the chain apart.</summary>
    private const double LargestSwing = 18;

    /// <summary>How much the band sags in the middle, in pixels, before the right key pulls it straight.</summary>
    private const double Sag = 7;

    /// <summary>
    /// How fast the taut link trembles before it lets go, in swings a second: slow enough to be seen as a tremble at the
    /// screen's sixty frames a second, not as a shimmer.
    /// </summary>
    private const double TrembleTone = 8;

    /// <summary>How far the taut link trembles before it lets go, in pixels.</summary>
    private const double TrembleWidth = 0.9;

    /// <summary>How far beyond the page's edges the rope is held, in pixels.</summary>
    private const double Hold = 60;

    /// <summary>The node's outline's width once the lens has become it, as the graph's node's edge.</summary>
    private const double NodeEdge = 1.5;

    /// <summary>The rope's three lowest swings: their place and speed.</summary>
    private readonly (double Place, double Speed)[] _swings = new (double, double)[3];

    /// <summary>The particles around the helix.</summary>
    private readonly DnaParticles _particles;

    /// <summary>The time since the figure was shown, in seconds.</summary>
    private double _time;

    /// <summary>The time since the last wrong key, in seconds; long ago when none has been.</summary>
    private double _sinceWrong = double.MaxValue;

    /// <summary>Which way the next wrong key jerks the band.</summary>
    private int _jerk = 1;

    /// <summary>Whether the chains' hard stop and the jolt of the letting go have been given to the rope.</summary>
    private bool _stopped;

    /// <summary>Whether the letting go has jolted the rope and puffed its characters.</summary>
    private bool _letGo;

    /// <summary>The time since the right key, in seconds; null until it is given.</summary>
    private double? _sinceRight;

    /// <summary>The node the lens becomes, on the page.</summary>
    private NodeTarget _node;

    /// <summary>The lens the right key aims at: the turn at the middle and how far it was from there.</summary>
    private (double LensTurn, double Shift)? _lens;

    /// <summary>The page's size the figure was last laid out for.</summary>
    private (double Width, double Height) _page;

    /// <summary>The band as it stands at this step, laid out once and drawn from; null when it must be laid out again.</summary>
    private ChainBand? _current;

    /// <summary>Creates the motion, at the start.</summary>
    /// <param name="random">Picks where and how each particle comes loose; seeded in the tests.</param>
    public BandMotion(Random random) => _particles = new DnaParticles(random);

    /// <summary>
    /// Gets or sets whether the figure stands still: with Windows' animations turned off it is drawn at rest, without
    /// the start, the rope or the particles' drift, and the right key goes straight on.
    /// </summary>
    public bool IsStill { get; set; }

    /// <summary>Gets whether the right key has been given and the page is on its way to the shell.</summary>
    public bool IsUnlocking => _sinceRight is not null;

    /// <summary>Gets the time since the right key, in seconds, or 0 before it.</summary>
    public double SinceRight => _sinceRight ?? 0;

    /// <summary>Gets whether the way to the shell is over and the page can go.</summary>
    public bool IsUnlocked => _sinceRight is { } since && (IsStill || since >= Reveal.To);

    /// <summary>Gets whether anything moves, so the figure must be drawn again at the next step.</summary>
    public bool IsMoving => !IsStill || IsUnlocking;

    /// <summary>Gets the particles around the helix.</summary>
    public IReadOnlyList<Particle> Particles => _particles.Items;

    /// <summary>Starts the figure again from the start, as when the lock screen is shown.</summary>
    public void Restart()
    {
        _time = 0;
        _sinceWrong = double.MaxValue;
        _sinceRight = null;
        _lens = null;
        _current = null;
        _stopped = false;
        _letGo = false;
        Array.Clear(_swings);
        _particles.Clear();
    }

    /// <summary>Moves the figure on.</summary>
    /// <param name="seconds">The time since the last step.</param>
    /// <param name="window">How fast the window's speed changed: its acceleration on the screen, in pixels a second².</param>
    public void Step(double seconds, FigurePoint window)
    {
        if (IsStill)
        {
            seconds = 0;
            window = default;
        }

        _time += seconds;
        _sinceWrong = _sinceWrong == double.MaxValue ? _sinceWrong : _sinceWrong + seconds;
        if (_sinceRight is { } since)
        {
            _sinceRight = since + seconds;
            var calm = Math.Exp(-6 * seconds);
            for (var i = 0; i < _swings.Length; i++)
            {
                _swings[i].Speed *= calm;
            }
        }

        _current = null;
        var band = Current();
        Jolts(band);
        Swing(seconds, window);
        if (!IsStill && _time >= LetGo && !IsUnlocking)
        {
            _particles.Keep(band);
        }

        _particles.Step(seconds);
    }

    /// <summary>A character was typed in the field: a base lets go of the helix.</summary>
    public void Typed()
    {
        if (!IsStill && !IsUnlocking)
        {
            _particles.LetGoBase(Current());
        }
    }

    /// <summary>A wrong key: the band jerks like a locked chain, pulses red and lets a puff of characters go.</summary>
    public void Wrong()
    {
        if (IsUnlocking)
        {
            return;
        }

        _sinceWrong = 0;
        _jerk = -_jerk;
        _swings[0].Speed += 260 * _jerk;
        _swings[2].Speed += 40 * _jerk;
        if (!IsStill)
        {
            _particles.Burst(Current(), 70);
        }
    }

    /// <summary>The right key: the band starts on its way to the node Nytt ärende.</summary>
    /// <param name="node">Where the node stands on the page.</param>
    public void Right(NodeTarget node)
    {
        _node = node;
        _sinceRight = 0;
        _lens = null;
        _current = null;
    }

    /// <summary>
    /// Moves the node the band is on its way to without starting the way again: the shell can lay the node out anew
    /// while the band moves, as when the latest cases come in after the unlock and the node moves into their ring.
    /// </summary>
    /// <param name="node">Where the node stands on the page now.</param>
    public void MoveNode(NodeTarget node) => _node = node;

    /// <summary>Gives how red the band is after a wrong key, from 0 to nearly 1.</summary>
    /// <returns>The share of the red pulse.</returns>
    public double Tint() =>
        _sinceWrong < PulseLength ? 0.9 * Math.Sin(Math.PI * _sinceWrong / PulseLength) : 0;

    /// <summary>Gives how far the form has faded after the right key, from 0 to 1.</summary>
    /// <returns>The share.</returns>
    public double FormGone() => _sinceRight is { } since ? ChainBand.Smooth(FormFade.From, FormFade.To, since) : 0;

    /// <summary>Gives how far the page has faded to show the shell after the right key, from 0 to 1.</summary>
    /// <returns>The share.</returns>
    public double PageGone() => _sinceRight is { } since ? ChainBand.Smooth(Reveal.From, Reveal.To, since) : 0;

    /// <summary>Gives what to draw now on a page of a size.</summary>
    /// <param name="width">The page's width.</param>
    /// <param name="height">The page's height.</param>
    /// <returns>The frame.</returns>
    public BandFrame Frame(double width, double height)
    {
        if (_page != (width, height))
        {
            _page = (width, height);
            _current = null;
        }

        var band = Current();
        var tint = Tint();
        if (_sinceRight is { } since && since > Morph.From)
        {
            return MorphFrame(band, since, tint);
        }

        var links = Links(band, 0, 1).ToList();
        var (front, back) = Strands(band, band.Start - Overlap, band.End + Overlap, 1);
        return new BandFrame(links, back, Rungs(band, band.Start, band.End, 1, default).ToList(), front, null, tint);
    }

    /// <summary>Gives the strands' overlap into the joints at either end, where the bars would start.</summary>
    private static double Overlap => (ChainLink.BarsEnd - ChainLink.BarsStart) * ChainBand.Scale;

    /// <summary>Eases a share in and out: slow at both ends.</summary>
    /// <param name="t">The share, from 0 to 1.</param>
    /// <returns>The eased share.</returns>
    private static double Ease(double t) => t * t * (3 - (2 * t));

    /// <summary>Gives the band at this step, laid out once for the step and the page.</summary>
    /// <returns>The band.</returns>
    private ChainBand Current() => _current ??= Band();

    /// <summary>Lays the band out as it stands now, on the page it was last laid out for.</summary>
    /// <returns>The band.</returns>
    private ChainBand Band()
    {
        var (width, height) = _page.Width > 0 ? _page : (1440, 864);
        var start = Start();
        var probe = new ChainBand(width, height, start);
        if (_sinceRight is not { } since)
        {
            return probe;
        }

        // The right key turns the helix about its axis, to the right, until a whole lens lies in the middle.
        _lens ??= probe.LensAhead();
        var shift = _lens.Value.Shift * (IsStill ? 1 : Spring(since - 0.05, 1.1, 0.7));
        var flat = IsStill ? 1 : ChainBand.Smooth(0.3, 0.8, since);
        return new ChainBand(width, height, start with { Shift = shift, Flat = flat, LensTurn = _lens.Value.LensTurn });
    }

    /// <summary>Gives the start's pose: the chains sliding in, then the long link twisting with a spring.</summary>
    /// <returns>The pose.</returns>
    private BandPose Start()
    {
        if (IsStill)
        {
            return BandPose.Rest;
        }

        var k = Math.Min(1, _time / ChainsIn);
        var slide = -Math.Pow(1 - k, 3) * SlideIn;
        return BandPose.Rest with { Slide = slide, Twist = Spring(_time - LetGo, 0.95, 0.42) };
    }

    /// <summary>Gives a damped spring's way from 0 to 1, past 1 and back.</summary>
    /// <param name="t">The time since it was let go.</param>
    /// <param name="tone">Its swings a second.</param>
    /// <param name="damping">Its damping, as a share of critical.</param>
    /// <returns>Its place.</returns>
    private static double Spring(double t, double tone, double damping)
    {
        if (t <= 0)
        {
            return 0;
        }

        var w = 2 * Math.PI * tone;
        var wd = w * Math.Sqrt(1 - (damping * damping));
        return 1 - (Math.Exp(-damping * w * t)
            * (Math.Cos(wd * t) + (damping / Math.Sqrt(1 - (damping * damping)) * Math.Sin(wd * t))));
    }

    /// <summary>Gives the rope the chains' hard stop and the jolt of the letting go, once each.</summary>
    /// <param name="band">The band now.</param>
    private void Jolts(ChainBand band)
    {
        if (IsStill)
        {
            return;
        }

        if (!_stopped && _time >= ChainsIn)
        {
            _stopped = true;
            _swings[0].Speed += 70;
        }

        if (!_letGo && _time >= LetGo)
        {
            _letGo = true;
            _swings[0].Speed -= 50;
            _swings[2].Speed += 15;
            _particles.Burst(band, 70);
        }
    }

    /// <summary>
    /// Swings the rope: three swings of a string held beyond the page's edges, pushed by the window's acceleration. The
    /// sag lets a pull along the band swing it too, so a window dragged sideways sets the bow going.
    /// </summary>
    /// <param name="seconds">The time since the last step.</param>
    /// <param name="window">The window's acceleration.</param>
    private void Swing(double seconds, FigurePoint window)
    {
        var across = window.Y;
        var along = -window.X;
        for (var i = 0; i < _swings.Length; i++)
        {
            var n = i + 1;
            var w = 2 * Math.PI * Tone * n;
            var pull = n == 1 ? across + (0.6 * Math.Abs(along)) : 0.3 * across;
            var force = n % 2 == 1 ? 0.35 * pull * 4 / (n * Math.PI) : 0;
            var (place, speed) = _swings[i];
            speed += (force - (w * w * place) - (2 * Damping * w * speed)) * seconds;
            place = Math.Clamp(place + (speed * seconds), -LargestSwing, LargestSwing);
            _swings[i] = (place, speed);
        }
    }

    /// <summary>Gives how far the rope stands off the axis at a place, and its slope there.</summary>
    /// <param name="band">The band.</param>
    /// <param name="along">The place along the axis.</param>
    /// <returns>The offset above the axis and its slope.</returns>
    private (double Offset, double Slope) Sway(ChainBand band, double along)
    {
        var length = band.Width + (2 * Hold);
        var x = (along + Hold) / length;
        var tremble = !IsStill && _time > ChainsIn && _time < LetGo
            ? TrembleWidth * ChainBand.Smooth(ChainsIn, LetGo, _time) * Math.Sin(2 * Math.PI * TrembleTone * _time)
            : 0;
        var straight = _sinceRight is { } since ? ChainBand.Smooth(0, 0.45, since) : 0;
        var bow = tremble - (Sag * (1 - straight));
        var offset = bow * Math.Sin(Math.PI * x);
        var slope = bow * Math.PI * Math.Cos(Math.PI * x);
        for (var i = 0; i < _swings.Length; i++)
        {
            var n = i + 1;
            offset += _swings[i].Place * Math.Sin(n * Math.PI * x);
            slope += _swings[i].Place * n * Math.PI * Math.Cos(n * Math.PI * x);
        }

        return (offset, slope / length);
    }

    /// <summary>Gives a point on the swaying band.</summary>
    /// <param name="band">The band.</param>
    /// <param name="along">The place along the axis.</param>
    /// <param name="across">The distance above the axis.</param>
    /// <returns>The point on the page.</returns>
    private FigurePoint At(ChainBand band, double along, double across) =>
        band.At(along, across + Sway(band, along).Offset);

    /// <summary>Gives both chains' links, slid out to the sides by a distance and faded.</summary>
    /// <param name="band">The band.</param>
    /// <param name="slideOut">How far each chain has slid out, in pixels.</param>
    /// <param name="alpha">How strongly they show.</param>
    /// <returns>The links.</returns>
    private IEnumerable<LinkPose> Links(ChainBand band, double slideOut, double alpha)
    {
        for (var k = 0; k < band.LeftJoints.Count; k++)
        {
            yield return Link(band, band.LeftJoints[k] - slideOut, k < band.LeftJoints.Count - 1, 1, alpha);
        }

        foreach (var joint in band.RightJoints)
        {
            yield return Link(band, joint + slideOut, true, band.RightSquash, alpha);
        }
    }

    /// <summary>Gives one link, turned with the rope's slope where it hangs.</summary>
    /// <param name="band">The band.</param>
    /// <param name="along">Its joint's place along the axis.</param>
    /// <param name="hasBars">Whether its bars are drawn.</param>
    /// <param name="squash">How wide it looks as its chain turns about its axis.</param>
    /// <param name="alpha">How strongly it shows.</param>
    /// <returns>The link.</returns>
    private LinkPose Link(ChainBand band, double along, bool hasBars, double squash, double alpha)
    {
        var place = At(band, along, 0);
        var angle = -90 - (Math.Atan(Sway(band, along).Slope) * 180 / Math.PI);
        return new LinkPose(place.X, place.Y, angle, ChainBand.Scale, squash, hasBars, alpha);
    }

    /// <summary>
    /// Traces both strands between two places, cut where the shade steps or a strand passes behind the other, through a
    /// map that may move each point. Each stretch reaches one step into the next, so no seam shows between them.
    /// </summary>
    /// <param name="band">The band.</param>
    /// <param name="from">Where to start along the axis.</param>
    /// <param name="to">Where to stop.</param>
    /// <param name="alpha">How strongly they show.</param>
    /// <param name="map">Moves a point: its place on the band, its share of the way and its strand; none keeps it.</param>
    /// <param name="width">The strands' width; the bars' when left out.</param>
    /// <returns>The stretches in front and behind.</returns>
    private (List<StrandStretch> Front, List<StrandStretch> Back) Strands(
        ChainBand band, double from, double to, double alpha, Func<StrandPoint, StrandPoint>? map = null, double? width = null)
    {
        var front = new List<StrandStretch>();
        var back = new List<StrandStretch>();
        var thickness = width ?? ChainBand.StrandThickness;
        var steps = Math.Max(2, (int)Math.Ceiling((to - from) / ChainBand.StrandStep));
        foreach (var one in new[] { true, false })
        {
            var points = new List<FigurePoint>();
            (int Step, bool InFront)? current = null;
            for (var i = 0; i <= steps; i++)
            {
                var along = from + ((to - from) * i / steps);
                var slice = band.Helix(along);
                var depth = one ? slice.FirstDepth : slice.SecondDepth;
                var raw = new StrandPoint(
                    At(band, along, one ? slice.First : slice.Second),
                    (slice.Twist * depth) + (1 - slice.Twist),
                    ChainBand.InFront(depth, slice.Twist),
                    (double)i / steps,
                    one);
                var point = map is null ? raw : map(raw);
                var key = (ChainBand.ShadeStepSeen(point.Seen), point.InFront);
                if (current is { } was && was != key)
                {
                    points.Add(point.Place);
                    (was.InFront ? front : back).Add(new StrandStretch(points, ChainBand.ShadeOf(was.Step), thickness, alpha));
                    points = [points[^2]];
                }

                current = key;
                points.Add(point.Place);
            }

            if (points.Count > 1 && current is { } last)
            {
                (last.InFront ? front : back).Add(new StrandStretch(points, ChainBand.ShadeOf(last.Step), thickness, alpha));
            }
        }

        return (front, back);
    }

    /// <summary>Gives the rungs between two places, moved by an offset and faded.</summary>
    /// <param name="band">The band.</param>
    /// <param name="from">Where to start along the axis.</param>
    /// <param name="to">Where to stop.</param>
    /// <param name="alpha">How strongly they show.</param>
    /// <param name="offset">How far they have moved on the page.</param>
    /// <returns>The rungs.</returns>
    private IEnumerable<RungPose> Rungs(ChainBand band, double from, double to, double alpha, FigurePoint offset)
    {
        var first = band.Start + (ChainBand.RungSpacing / 2);
        var k = Math.Max(0, Math.Ceiling((from - first) / ChainBand.RungSpacing));
        for (var along = first + (k * ChainBand.RungSpacing); along <= to; along += ChainBand.RungSpacing)
        {
            if (!ChainBand.RungAt(band.Helix(along), out var ends))
            {
                continue;
            }

            FigurePoint Moved(double across)
            {
                var place = At(band, along, across);
                return new FigurePoint(place.X + offset.X, place.Y + offset.Y);
            }

            yield return new RungPose(Moved(ends.From), Moved((ends.From + ends.To) / 2), Moved(ends.To), alpha);
        }
    }

    /// <summary>
    /// Gives the frame while the lens becomes the node: the chains and the helix outside the lens slide out and fade,
    /// and each of the lens's strands pulls in to one half of the node's outline, thinning to its edge.
    /// </summary>
    /// <param name="band">The band, with its lens squared off in the middle.</param>
    /// <param name="since">The time since the right key.</param>
    /// <param name="tint">How red it still is.</param>
    /// <returns>The frame.</returns>
    private BandFrame MorphFrame(ChainBand band, double since, double tint)
    {
        var m = ChainBand.Smooth(Morph.From, Morph.To, since);
        var e = Ease(m);
        var gone = ChainBand.Smooth(Morph.From, Morph.From + 0.5, since);
        var (lensFrom, lensTo) = band.Lens();
        var links = Links(band, SlideOut * gone * gone, 1 - gone).ToList();
        var (leftFront, leftBack) = Strands(band, band.Start - Overlap, lensFrom, 1 - gone);
        var (rightFront, rightBack) = Strands(band, lensTo, band.End + Overlap, 1 - gone);

        // Strand one lies above the axis in the middle when the lens's turn is a whole turn, below it otherwise.
        var oneOnTop = Math.Cos(_lens?.LensTurn ?? 0) > 0;
        var top = Outline(_node, true);
        var bottom = Outline(_node, false);
        StrandPoint ToNode(StrandPoint point)
        {
            var target = Along(point.IsOne == oneOnTop ? top : bottom, point.Share);
            var place = new FigurePoint(
                point.Place.X + ((target.X - point.Place.X) * e), point.Place.Y + ((target.Y - point.Place.Y) * e));
            return point with { Place = place, Seen = (point.Seen * (1 - e)) + e, InFront = true };
        }

        var width = ChainBand.StrandThickness + ((NodeEdge - ChainBand.StrandThickness) * e);
        var (lensFront, lensBack) = Strands(band, lensFrom, lensTo, 1, ToNode, width);
        var lensMiddle = At(band, band.Centre, 0);
        var moved = new FigurePoint(
            (_node.X + (_node.Width / 2) - lensMiddle.X) * e, (_node.Y + (_node.Height / 2) - lensMiddle.Y) * e);
        var rungs = m < 0.35 ? Rungs(band, lensFrom, lensTo, 1 - (m / 0.35), moved).ToList() : [];
        var face = new NodeFace(_node, ChainBand.Smooth(0.55, 1, m));
        return new BandFrame(
            links,
            [.. leftBack, .. rightBack, .. leftFront, .. rightFront, .. lensBack],
            rungs,
            lensFront,
            face,
            tint);
    }

    /// <summary>
    /// Gives one half of the node's outline, from its left middle over the top (or under the bottom) to its right
    /// middle, as points with their length along it.
    /// </summary>
    /// <param name="node">The node.</param>
    /// <param name="top">Whether it is the top half.</param>
    /// <returns>The points and the length up to each.</returns>
    public static (List<FigurePoint> Points, List<double> Lengths) Outline(NodeTarget node, bool top)
    {
        var r = Math.Min(node.Radius, Math.Min(node.Width, node.Height) / 2);
        var middle = node.Y + (node.Height / 2);
        var left = node.X;
        var right = node.X + node.Width;
        var centreY = middle + ((top ? -1 : 1) * ((node.Height / 2) - r));
        var points = new List<FigurePoint> { new(left, middle) };
        void Arc(double centreX, double from, double to)
        {
            for (var i = 0; i <= 8; i++)
            {
                var a = from + ((to - from) * i / 8);
                points.Add(new FigurePoint(centreX + (r * Math.Cos(a)), centreY + (r * Math.Sin(a))));
            }
        }

        Arc(left + r, Math.PI, top ? 1.5 * Math.PI : 0.5 * Math.PI);
        Arc(right - r, top ? 1.5 * Math.PI : 0.5 * Math.PI, top ? 2 * Math.PI : 0);
        points.Add(new FigurePoint(right, middle));
        var lengths = new List<double> { 0 };
        for (var i = 1; i < points.Count; i++)
        {
            lengths.Add(lengths[^1] + Math.Sqrt(
                Math.Pow(points[i].X - points[i - 1].X, 2) + Math.Pow(points[i].Y - points[i - 1].Y, 2)));
        }

        return (points, lengths);
    }

    /// <summary>Gives the point a share of the way along an outline.</summary>
    /// <param name="outline">The outline.</param>
    /// <param name="share">The share of its length, from 0 to 1.</param>
    /// <returns>The point.</returns>
    public static FigurePoint Along((List<FigurePoint> Points, List<double> Lengths) outline, double share)
    {
        var (points, lengths) = outline;
        var want = Math.Clamp(share, 0, 1) * lengths[^1];
        var i = 1;
        while (i < lengths.Count - 1 && lengths[i] < want)
        {
            i++;
        }

        var k = (want - lengths[i - 1]) / Math.Max(1e-9, lengths[i] - lengths[i - 1]);
        return new FigurePoint(
            points[i - 1].X + ((points[i].X - points[i - 1].X) * k), points[i - 1].Y + ((points[i].Y - points[i - 1].Y) * k));
    }

    /// <summary>One point of a strand on its way: where it is, how much of it is seen and which strand it belongs to.</summary>
    /// <param name="Place">Its place on the page.</param>
    /// <param name="Seen">How much of it is seen, from -1 behind to 1 in front.</param>
    /// <param name="InFront">Whether it is drawn in front of the other strand.</param>
    /// <param name="Share">Its share of the way between the two places traced.</param>
    /// <param name="IsOne">Whether it is strand one.</param>
    private readonly record struct StrandPoint(FigurePoint Place, double Seen, bool InFront, double Share, bool IsOne);
}

/// <summary>Where the node Nytt ärende stands on the page, which the lens becomes after the right key.</summary>
/// <param name="X">Its left edge.</param>
/// <param name="Y">Its top edge.</param>
/// <param name="Width">Its width.</param>
/// <param name="Height">Its height.</param>
/// <param name="Radius">Its corners' radius.</param>
public readonly record struct NodeTarget(double X, double Y, double Width, double Height, double Radius);

/// <summary>What to draw at one moment, back to front.</summary>
/// <param name="Links">The chains' links.</param>
/// <param name="Back">The strands' stretches drawn first: behind the other strand, or fading out.</param>
/// <param name="Rungs">The rungs between the strands.</param>
/// <param name="Front">The strands' stretches drawn last.</param>
/// <param name="Node">The node's face coming up under the outline, while the lens becomes the node.</param>
/// <param name="Tint">How far the accent has turned red after a wrong key, from 0 to nearly 1.</param>
public sealed record BandFrame(
    IReadOnlyList<LinkPose> Links,
    IReadOnlyList<StrandStretch> Back,
    IReadOnlyList<RungPose> Rungs,
    IReadOnlyList<StrandStretch> Front,
    NodeFace? Node,
    double Tint);

/// <summary>One link of a chain: its joint, and its bars below it unless it is the last before the helix.</summary>
/// <param name="X">The joint's centre, from the page's left edge.</param>
/// <param name="Y">The joint's centre, from the page's top edge.</param>
/// <param name="Angle">Its turn clockwise from upright, in degrees.</param>
/// <param name="Scale">The logo's units to pixels.</param>
/// <param name="Squash">How wide it looks as its chain turns about its axis: 1 upright.</param>
/// <param name="HasBars">Whether its bars are drawn.</param>
/// <param name="Alpha">How strongly it shows.</param>
public readonly record struct LinkPose(
    double X, double Y, double Angle, double Scale, double Squash, bool HasBars, double Alpha);

/// <summary>A stretch of one strand in one shade.</summary>
/// <param name="Points">Its points, from the left.</param>
/// <param name="Shade">Its colour as a share of the accent, from <see cref="ChainBand.Darkest"/> to 1.</param>
/// <param name="Width">Its width, in pixels.</param>
/// <param name="Alpha">How strongly it shows.</param>
public sealed record StrandStretch(IReadOnlyList<FigurePoint> Points, double Shade, double Width, double Alpha);

/// <summary>A rung between the strands: a light half from strand one and a darker half from strand two.</summary>
/// <param name="First">Where it leaves strand one.</param>
/// <param name="Middle">Where the two halves meet.</param>
/// <param name="Second">Where it reaches strand two.</param>
/// <param name="Alpha">How strongly it shows.</param>
public readonly record struct RungPose(FigurePoint First, FigurePoint Middle, FigurePoint Second, double Alpha);

/// <summary>The node's face, coming up under the outline while the lens becomes the node.</summary>
/// <param name="Target">Where the node stands.</param>
/// <param name="Alpha">How strongly the face shows.</param>
public readonly record struct NodeFace(NodeTarget Target, double Alpha);
