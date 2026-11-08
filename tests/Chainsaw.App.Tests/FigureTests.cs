using Chainsaw.App.Figure;

namespace Chainsaw.App.Tests;

public sealed class ChainLinkTests
{
    [Fact]
    public void The_joint_is_the_logos_joint_in_the_assets()
    {
        var svg = File.ReadAllText(Path.Combine(RepositoryRoot(), "assets", "logo", "chainsaw-chain-joint.svg"));
        var start = svg.IndexOf(" d=\"", StringComparison.Ordinal) + 4;
        var data = svg[start..svg.IndexOf('"', start)];

        Assert.Equal(ChainLink.Joint, data);
    }

    [Fact]
    public void A_link_has_forty_units_of_straight_bars_between_joints()
    {
        Assert.Equal(40, ChainLink.LinkStraight);
        Assert.Equal(160, ChainLink.Pitch);
    }

    [Fact]
    public void The_bars_continue_the_links_where_they_leave_the_joints_bottom_edge()
    {
        // The joint's bottom edge: the left link runs from -61.6 to -31.7, the right from 31 to 61.
        foreach (var corner in new[] { "-61.6 60.5", "-31.7 60.5", " 31 60.5", " 61 60.5" })
        {
            Assert.Contains(corner, ChainLink.Joint, StringComparison.Ordinal);
        }

        Assert.Equal("M-61.6 58H-31.7V102.5H-61.6ZM31 58H61V102.5H31Z", ChainLink.Bars);
    }

    [Fact]
    public void The_strands_run_down_the_middle_of_the_bars_as_wide_as_them()
    {
        var (left, right) = ((-61.6 + -31.7) / 2, (31 + 61) / 2.0);

        Assert.Equal(ChainLink.BarOffset - ChainLink.BarCenter, left, tolerance: 0.01);
        Assert.Equal(ChainLink.BarOffset + ChainLink.BarCenter, right, tolerance: 0.01);
        Assert.InRange(ChainLink.BarWidth, 29.9, 30);
    }

    /// <summary>Finds the repository's root from where the tests run.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Chainsaw.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository was not found.");
    }
}

public sealed class ChainBandTests
{
    /// <summary>The sketch's page: 1440 × 900 with the 36 px title bar.</summary>
    private static readonly ChainBand Sketch = new(1440, 864);

    [Fact]
    public void The_band_runs_under_the_form_and_stays_on_a_low_page()
    {
        Assert.Equal(864 - ChainBand.AxisFromBottom, Sketch.AxisY, 6);
        Assert.Equal(400 * ChainBand.HighestAxis, new ChainBand(1440, 400).AxisY, 6);
    }

    [Fact]
    public void Two_chains_come_in_from_the_sides_and_the_helix_lies_between_them_in_the_middle()
    {
        var barsEnd = ChainLink.BarsEnd * ChainBand.Scale;

        Assert.Equal(720, Sketch.Centre, 6);
        Assert.Equal(1440 - Sketch.Start, Sketch.End, 6);
        Assert.InRange(Sketch.Start, 1440 * 0.2, 1440 * 0.4);
        Assert.Equal(Sketch.LinksPerSide + 1, Sketch.LeftJoints.Count);
        Assert.True(Sketch.LeftJoints[0] < 0);
        Assert.Equal(Sketch.Start, Sketch.LeftJoints[^1] + barsEnd, 6);
        Assert.Equal(Sketch.End + barsEnd, Sketch.RightJoints[0], 6);
        Assert.True(Sketch.RightJoints[^1] > 1440);
    }

    [Fact]
    public void The_strands_leave_the_left_bars_and_meet_the_right_chain_upright_after_whole_turns()
    {
        var start = Sketch.Helix(Sketch.Start);
        var end = Sketch.Helix(Sketch.End);

        Assert.Equal(Sketch.Middle + Sketch.Radius, start.First, 6);
        Assert.Equal(Sketch.Middle - Sketch.Radius, start.Second, 6);
        Assert.Equal(Sketch.WholeTurns * 2 * Math.PI, Sketch.EndTurn, 6);
        Assert.Equal(1, Sketch.RightSquash, 6);
        Assert.Equal(Sketch.Middle + Sketch.Radius, end.First, 6);
        Assert.Equal(Sketch.Middle - Sketch.Radius, end.Second, 6);
    }

    [Fact]
    public void The_helix_is_widest_in_the_middle()
    {
        var widest = Enumerable.Range(0, 200)
            .Select(i => Sketch.Centre - 200 + (i * 2))
            .Max(along => Math.Abs(Sketch.Helix(along).First - Sketch.Middle));

        Assert.Equal(Sketch.Radius * (1 + ChainBand.Widening), widest, 0);
    }

    [Fact]
    public void Before_it_lets_go_the_long_link_is_two_straight_bars()
    {
        var taut = new ChainBand(1440, 864, BandPose.Rest with { Twist = 0 });

        Assert.All(Enumerable.Range(0, 50).Select(i => taut.Start + (i * 10)), along =>
        {
            Assert.Equal(taut.Middle + taut.Radius, taut.Helix(along).First, 6);
            Assert.Equal(taut.Middle - taut.Radius, taut.Helix(along).Second, 6);
        });
        Assert.Equal(1, taut.RightSquash, 6);
    }

    [Fact]
    public void Turned_to_the_lens_the_strands_lie_furthest_apart_in_the_middle_and_square_off()
    {
        var (lens, shift) = Sketch.LensAhead();
        var turned = new ChainBand(1440, 864, BandPose.Rest with { Shift = shift, LensTurn = lens });
        var square = new ChainBand(1440, 864, BandPose.Rest with { Shift = shift, LensTurn = lens, Flat = 1 });
        var middle = square.Helix(square.Centre);
        var (from, to) = turned.Lens();
        var quarter = from + ((turned.Centre - from) / 3);

        Assert.True(shift > 0, "The helix turns to the right.");
        Assert.Equal(lens, turned.RawTurn(turned.Centre), 6);
        Assert.True(from < turned.Centre && turned.Centre < to);
        Assert.Equal(2 * Sketch.Radius * (1 + ChainBand.Widening), Math.Abs(middle.First - middle.Second), 0);
        Assert.True(
            Math.Abs(square.Helix(quarter).First - square.Helix(quarter).Second)
            > Math.Abs(turned.Helix(quarter).First - turned.Helix(quarter).Second));
    }

    [Fact]
    public void A_strand_is_shaded_by_its_depth_and_both_are_in_front_before_the_twist()
    {
        Assert.Equal(ChainBand.Shades - 1, ChainBand.ShadeStep(1, 1));
        Assert.Equal(0, ChainBand.ShadeStep(-1, 1));
        Assert.Equal(ChainBand.Shades - 1, ChainBand.ShadeStep(-1, 0));
        Assert.Equal(ChainBand.Darkest, ChainBand.ShadeOf(0), 6);
        Assert.Equal(1, ChainBand.ShadeOf(ChainBand.Shades - 1), 6);
        Assert.True(ChainBand.InFront(-1, 0));
        Assert.False(ChainBand.InFront(-1, 1));
    }

    [Fact]
    public void A_tiny_page_still_lays_out_without_failing()
    {
        var tiny = new ChainBand(40, 30);
        var slice = tiny.Helix(tiny.Centre);

        Assert.False(double.IsNaN(slice.First));
        Assert.False(double.IsNaN(tiny.EndTurn));
    }
}

public sealed class BandMotionTests
{
    /// <summary>Steps a motion on for a time, in steps of a sixtieth of a second, with the window still.</summary>
    private static void Run(BandMotion motion, double seconds, FigurePoint push = default)
    {
        for (var t = 0.0; t < seconds; t += 1 / 60.0)
        {
            motion.Step(1 / 60.0, push);
        }
    }

    /// <summary>Gives a motion past its start, at rest on the sketch's page.</summary>
    private static BandMotion Settled()
    {
        var motion = new BandMotion(new Random(1));
        motion.Frame(1440, 864);
        Run(motion, BandMotion.StartOver + 2);
        return motion;
    }

    /// <summary>Gives the middle of the band's height on the page: the helix's middle link joint's place.</summary>
    private static double Middle(BandMotion motion)
    {
        var frame = motion.Frame(1440, 864);
        var front = frame.Front.Concat(frame.Back).SelectMany(s => s.Points).Where(p => Math.Abs(p.X - 720) < 1).ToList();
        return front.Average(p => p.Y);
    }

    [Fact]
    public void The_chains_slide_in_and_the_long_link_lets_go_into_the_helix()
    {
        var motion = new BandMotion(new Random(1));
        var rest = new ChainBand(1440, 864);
        var first = motion.Frame(1440, 864).Links[0];

        Assert.True(first.X < rest.LeftJoints[0] - 100, "The chains start off to the side.");
        Run(motion, BandMotion.ChainsIn + 0.1);
        var taut = motion.Frame(1440, 864);
        Assert.Equal(rest.LeftJoints[0], taut.Links[0].X, 0);
        Assert.Empty(taut.Rungs);

        Run(motion, BandMotion.StartOver);
        Assert.NotEmpty(motion.Frame(1440, 864).Rungs);
        Assert.NotEmpty(motion.Particles);
    }

    [Fact]
    public void Standing_still_it_is_drawn_at_rest_at_once_without_particles()
    {
        var motion = new BandMotion(new Random(1)) { IsStill = true };

        motion.Step(1, new FigurePoint(5000, 5000));
        var frame = motion.Frame(1440, 864);

        Assert.Equal(new ChainBand(1440, 864).LeftJoints[0], frame.Links[0].X, 6);
        Assert.NotEmpty(frame.Rungs);
        Assert.Empty(motion.Particles);
        Assert.False(motion.IsMoving);
    }

    [Fact]
    public void A_dragged_window_swings_the_band_as_a_bow_that_settles_quickly()
    {
        var motion = Settled();
        var still = Middle(motion);

        Run(motion, 0.15, new FigurePoint(0, 6000));
        var swung = Middle(motion);
        Run(motion, 4);
        var settled = Middle(motion);

        Assert.True(Math.Abs(swung - still) > 2, $"It swung {Math.Abs(swung - still):0.0} px.");
        Assert.True(Math.Abs(swung - still) <= 3 * 18 + 1, "It never swings wildly.");
        Assert.True(Math.Abs(settled - still) < 0.5, "It settles again.");
    }

    [Fact]
    public void A_sideways_drag_swings_it_too_through_its_sag()
    {
        var motion = Settled();
        var still = Middle(motion);

        Run(motion, 0.15, new FigurePoint(6000, 0));

        Assert.True(Math.Abs(Middle(motion) - still) > 1);
    }

    [Fact]
    public void A_wrong_key_jerks_it_pulses_it_red_and_lets_a_puff_go()
    {
        var motion = Settled();
        var before = motion.Particles.Count;
        var still = Middle(motion);

        motion.Wrong();
        Run(motion, 0.1);

        Assert.True(motion.Tint() > 0.3);
        Assert.True(motion.Particles.Count > before);
        Assert.True(Math.Abs(Middle(motion) - still) > 2);
        Run(motion, BandMotion.PulseLength);
        Assert.Equal(0, motion.Tint());
    }

    [Fact]
    public void A_typed_character_lets_a_base_go()
    {
        var motion = Settled();
        var bases = motion.Particles.Count(p => p.IsBase);

        motion.Typed();

        Assert.Equal(bases + 1, motion.Particles.Count(p => p.IsBase));
    }

    [Fact]
    public void The_right_key_turns_the_lens_into_the_node_and_then_lets_the_page_go()
    {
        var motion = Settled();
        var node = new NodeTarget(610, 353, 220, 54, 10);

        motion.Right(node);
        Assert.True(motion.IsUnlocking);
        Run(motion, BandMotion.FormFade.To);
        Assert.Equal(1, motion.FormGone(), 6);
        Assert.Equal(0, motion.PageGone(), 6);

        Run(motion, BandMotion.Morph.To - BandMotion.FormFade.To + 0.02);
        var frame = motion.Frame(1440, 864);
        Assert.All(frame.Links, link => Assert.True(link.Alpha < 0.01));
        Assert.Equal(1, frame.Node?.Alpha ?? 0, 6);
        Assert.All(frame.Front.SelectMany(s => s.Points), p =>
        {
            Assert.InRange(p.X, node.X - 0.5, node.X + node.Width + 0.5);
            Assert.InRange(p.Y, node.Y - 0.5, node.Y + node.Height + 0.5);
        });
        Assert.False(motion.IsUnlocked);

        Run(motion, BandMotion.Reveal.To - BandMotion.Morph.To);
        Assert.True(motion.IsUnlocked);
        Assert.Equal(1, motion.PageGone(), 6);
    }

    [Fact]
    public void Standing_still_the_right_key_goes_straight_on()
    {
        var motion = new BandMotion(new Random(1)) { IsStill = true };

        motion.Right(new NodeTarget(0, 0, 220, 54, 10));
        motion.Step(0.016, default);

        Assert.True(motion.IsUnlocked);
    }

    [Fact]
    public void The_nodes_outline_runs_from_its_left_middle_over_the_top_to_its_right_middle()
    {
        var node = new NodeTarget(100, 200, 220, 54, 10);
        var top = BandMotion.Outline(node, top: true);
        var bottom = BandMotion.Outline(node, top: false);

        Assert.Equal(new FigurePoint(100, 227), BandMotion.Along(top, 0));
        Assert.Equal(new FigurePoint(320, 227), BandMotion.Along(top, 1));
        Assert.Equal(200, BandMotion.Along(top, 0.5).Y, 6);
        Assert.Equal(254, BandMotion.Along(bottom, 0.5).Y, 6);
    }
}

public sealed class DnaParticlesTests
{
    private static readonly ChainBand Band = new(1440, 864);

    [Fact]
    public void The_helix_keeps_its_particles_and_never_lets_them_go_from_the_chains()
    {
        var particles = new DnaParticles(new Random(7));

        for (var i = 0; i < 20; i++)
        {
            particles.Keep(Band);
        }

        Assert.Equal(DnaParticles.Count, particles.Items.Count);
        Assert.All(particles.Items, p => Assert.InRange(p.X, Band.Start, Band.End));
    }

    [Fact]
    public void A_puff_lets_many_go_from_the_middle_at_once()
    {
        var particles = new DnaParticles(new Random(3));

        particles.Burst(Band, 50);

        Assert.InRange(particles.Items.Count, 40, 50);
        Assert.All(particles.Items, p => Assert.InRange(p.X, Band.Centre - 140, Band.Centre + 140));
    }

    [Fact]
    public void Most_are_dust_and_some_are_bases()
    {
        var particles = new DnaParticles(new Random(3));

        for (var i = 0; i < 20; i++)
        {
            particles.Keep(Band);
        }

        Assert.All(particles.Items, p =>
            Assert.Contains(p.Character, p.IsBase ? DnaParticles.Bases : DnaParticles.Dust));
        Assert.Contains(particles.Items, p => p.IsBase);
        Assert.True(particles.Items.Count(p => p.IsBase) < DnaParticles.Count / 3);
    }

    [Fact]
    public void They_drift_away_from_the_axis_and_go_once_faded()
    {
        var particles = new DnaParticles(new Random(5));
        particles.Keep(Band);
        var before = particles.Items.ToDictionary(p => p, p => Math.Abs(Band.AxisY - p.Y));

        particles.Step(0.5);

        Assert.All(particles.Items, p => Assert.True(Math.Abs(Band.AxisY - p.Y) >= before[p] - 1e-9));
        particles.Step(10);
        Assert.Empty(particles.Items);
    }

    [Fact]
    public void A_typed_character_lets_one_base_go()
    {
        var particles = new DnaParticles(new Random(9));

        particles.LetGoBase(Band);

        Assert.Single(particles.Items);
        Assert.True(particles.Items[0].IsBase);
    }

    [Fact]
    public void A_particle_fades_in_and_out_and_a_base_shows_stronger_than_dust()
    {
        var dust = new Particle('.', isBase: false, life: 4) { Age = 2 };
        var basePair = new Particle('A', isBase: true, life: 4) { Age = 2 };

        Assert.Equal(0, new Particle('.', isBase: false, life: 4).Alpha, 6);
        Assert.Equal(0.6, dust.Alpha, 6);
        Assert.Equal(0.85, basePair.Alpha, 6);
        Assert.Equal(0, new Particle('.', isBase: false, life: 4) { Age = 4 }.Alpha, 6);
    }
}
