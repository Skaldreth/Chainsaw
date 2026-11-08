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

public sealed class DnaFigureTests
{
    /// <summary>The sketch's page: 1440 × 900 with the 36 px title bar.</summary>
    private static readonly DnaFigure Sketch = new(1440, 864);

    [Fact]
    public void The_chain_comes_in_on_the_slant_from_above_the_page_with_six_joints_as_the_sketch()
    {
        Assert.Equal(6, Sketch.Joints.Count);
        Assert.True(Sketch.Joints[0].Y < 0);
        Assert.All(Sketch.Joints, joint =>
        {
            Assert.Equal(DnaFigure.Slant, joint.Angle);
            Assert.Equal(DnaFigure.Scale, joint.Scale);
        });

        // Each joint is one pitch further down the slant, to the left.
        var pitch = ChainLink.Pitch * DnaFigure.Scale;
        for (var i = 1; i < Sketch.Joints.Count; i++)
        {
            var (a, b) = (Sketch.Joints[i - 1], Sketch.Joints[i]);
            Assert.Equal(pitch, Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2)), 6);
            Assert.True(b.X < a.X);
        }
    }

    [Fact]
    public void The_helix_starts_where_the_last_links_bars_end_about_halfway_down()
    {
        var last = Sketch.Joints[^1];
        var barsEnd = Sketch.At(Sketch.Start, 0);
        var expected = ChainLink.BarsEnd * DnaFigure.Scale;

        Assert.Equal(expected, Math.Sqrt(Math.Pow(barsEnd.X - last.X, 2) + Math.Pow(barsEnd.Y - last.Y, 2)), 6);
        Assert.InRange(barsEnd.Y, 864 * 0.3, 864 * 0.6);
    }

    [Fact]
    public void The_strands_leave_the_bars_where_the_bars_are_and_as_wide()
    {
        var slice = Sketch.Helix(Sketch.Start);
        var bar = ChainLink.BarCenter * DnaFigure.Scale;
        var middle = ChainLink.BarOffset * DnaFigure.Scale;

        Assert.Equal(0, slice.Twist);
        Assert.Equal(middle + bar, slice.First, 6);
        Assert.Equal(middle - bar, slice.Second, 6);
        Assert.Equal(ChainLink.BarWidth * DnaFigure.Scale, DnaFigure.StrandThickness);
    }

    [Fact]
    public void The_helix_widens_towards_the_bottom_of_the_page()
    {
        // Where it leaves the chain the helix is as wide as the link; over a full turn near the bottom it is wider by
        // the widening.
        var chain = ChainLink.BarCenter * DnaFigure.Scale;
        var middle = ChainLink.BarOffset * DnaFigure.Scale;
        var bottom = Enumerable.Range(0, 200)
            .Max(i => Math.Abs(Sketch.Helix(Sketch.End - 400 + i * 2).First - middle));

        Assert.Equal(chain, Sketch.Helix(Sketch.Start).First - middle, 6);
        Assert.Equal(chain * (1 + DnaFigure.Widening), bottom, 0);
    }

    [Fact]
    public void The_strands_cross_at_one_angle_however_wide_the_helix_has_grown()
    {
        double SteepestNear(double along)
        {
            var steepest = 0.0;
            for (var s = along; s < along + 160; s += 0.5)
            {
                steepest = Math.Max(steepest, Math.Abs(Sketch.Helix(s + 0.5).First - Sketch.Helix(s).First) / 0.5);
            }

            return steepest;
        }

        var twisted = Sketch.Start + 200;
        Assert.Equal(DnaFigure.CrossingSlope, SteepestNear(twisted), 1);
        Assert.Equal(DnaFigure.CrossingSlope, SteepestNear(Sketch.End - 300), 1);
    }

    [Fact]
    public void Both_strands_are_traced_off_the_page_and_part_of_them_runs_behind()
    {
        var all = Sketch.Front.Concat(Sketch.Back).ToList();

        Assert.NotEmpty(Sketch.Front);
        Assert.NotEmpty(Sketch.Back);
        Assert.All(all, stretch => Assert.True(stretch.Points.Count > 1));
        Assert.Contains(all, stretch => stretch.Points.Any(p => p.X < 0 || p.Y > 864));
    }

    [Fact]
    public void A_strand_fades_into_the_dark_behind_in_small_steps_instead_of_at_once()
    {
        var shades = Sketch.Front.Concat(Sketch.Back).Select(s => s.Shade).Distinct().Order().ToList();
        Assert.Equal(DnaFigure.Darkest, shades[0], 6);
        Assert.Equal(1, shades[^1], 6);
        Assert.True(shades.Count >= DnaFigure.Shades / 2, $"Only {shades.Count} shades were used.");
        Assert.All(shades.Zip(shades.Skip(1)), pair =>
            Assert.True(pair.Second - pair.First <= (1 - DnaFigure.Darkest) / (DnaFigure.Shades - 1) + 1e-9));
        Assert.All(Sketch.Back, stretch => Assert.True(stretch.Shade < 1));
    }

    [Fact]
    public void Stretches_overlap_so_that_no_seam_shows_between_two_shades()
    {
        var all = Sketch.Front.Concat(Sketch.Back).ToList();

        // Every stretch but the two that start the strands begins with the last step of the stretch before it.
        var joined = all.Count(stretch => all.Any(other => other != stretch
            && other.Points[^2] == stretch.Points[0] && other.Points[^1] == stretch.Points[1]));
        Assert.Equal(all.Count - 2, joined);
    }

    [Fact]
    public void The_last_link_is_shorter_than_the_others_and_its_bars_are_the_strands_start()
    {
        Assert.All(Sketch.Joints.SkipLast(1), joint => Assert.True(joint.HasBars));
        Assert.False(Sketch.Joints[^1].HasBars);
        Assert.True(ChainLink.BarsEnd - ChainLink.JointHeight / 2 < ChainLink.LinkStraight);
    }

    [Fact]
    public void Rungs_join_the_strands_once_the_twist_is_under_way()
    {
        Assert.NotEmpty(Sketch.Rungs);
        Assert.All(Sketch.Rungs, rung =>
        {
            Assert.Equal((rung.First.X + rung.Second.X) / 2, rung.Middle.X, 6);
            Assert.True(rung.First.Y > Sketch.At(Sketch.Start, 0).Y - DnaFigure.StrandThickness * 3);
        });
    }

    [Fact]
    public void A_narrow_page_moves_the_chain_left_with_it()
    {
        var narrow = new DnaFigure(1000, 864);

        Assert.True(narrow.At(0, 0).X < Sketch.At(0, 0).X);
        Assert.Equal(DnaFigure.TopX * 1000 / DnaFigure.DesignWidth, narrow.At(0, 0).X, 6);
        Assert.NotEmpty(narrow.Front);
    }

    [Fact]
    public void A_tiny_page_still_lays_out_without_failing()
    {
        var tiny = new DnaFigure(40, 30);

        Assert.All(tiny.Front.Concat(tiny.Back).SelectMany(s => s.Points), p =>
        {
            Assert.False(double.IsNaN(p.X));
            Assert.False(double.IsNaN(p.Y));
        });
    }
}

public sealed class DnaParticlesTests
{
    private static readonly DnaFigure Figure = new(1440, 864);

    [Fact]
    public void The_helix_opens_with_its_particles_at_ages_spread_over_their_lives()
    {
        var particles = new DnaParticles(new Random(7));

        particles.Fill(Figure);

        Assert.Equal(DnaParticles.Count, particles.Items.Count);
        Assert.All(particles.Items, p => Assert.InRange(p.Age, 0, p.Life));
        Assert.Contains(particles.Items, p => p.Alpha > 0.3);
    }

    [Fact]
    public void Particles_come_from_the_twisted_helix_on_the_page_and_never_from_the_chain()
    {
        var particles = new DnaParticles(new Random(11));

        particles.Fill(Figure);

        Assert.All(particles.Items, p =>
        {
            var along = (p.X - Figure.At(0, 0).X) * Figure.Along.X + p.Y * Figure.Along.Y;
            Assert.True(along > Figure.Start, $"A particle sat at {along}, before the helix at {Figure.Start}.");
            Assert.InRange(p.X, -10, 1450);
            Assert.InRange(p.Y, -10, 874);
        });
    }

    [Fact]
    public void Most_are_dust_and_some_are_bases()
    {
        var particles = new DnaParticles(new Random(3));

        particles.Fill(Figure);

        Assert.All(particles.Items, p =>
            Assert.Contains(p.Character, p.IsBase ? DnaParticles.Bases : DnaParticles.Dust));
        Assert.Contains(particles.Items, p => p.IsBase);
        Assert.True(particles.Items.Count(p => p.IsBase) < DnaParticles.Count / 3);
    }

    [Fact]
    public void A_step_moves_them_away_from_the_axis()
    {
        var particles = new DnaParticles(new Random(5));
        particles.Fill(Figure);
        var before = particles.Items.ToDictionary(p => p, Across);

        particles.Step(Figure, 0.5);

        var moved = particles.Items.Where(before.ContainsKey).ToList();
        Assert.NotEmpty(moved);
        Assert.All(moved, p => Assert.True(Math.Abs(Across(p)) > Math.Abs(before[p])));
    }

    [Fact]
    public void One_that_has_faded_out_is_replaced_and_a_still_step_moves_nothing()
    {
        var particles = new DnaParticles(new Random(9));
        particles.Fill(Figure);
        var places = particles.Items.Select(p => (p.X, p.Y)).ToList();

        particles.Step(Figure, 0);
        Assert.Equal(places, particles.Items.Select(p => (p.X, p.Y)));

        var first = particles.Items[0];
        particles.Step(Figure, first.Life);
        Assert.NotSame(first, particles.Items[0]);
        Assert.Equal(DnaParticles.Count, particles.Items.Count);
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

    /// <summary>Gives a particle's distance across the axis.</summary>
    private static double Across(Particle p) => (p.X - Figure.At(0, 0).X) * Figure.Across.X + p.Y * Figure.Across.Y;
}
