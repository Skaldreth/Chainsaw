using Chainsaw.App.Field;

namespace Chainsaw.App.Tests;

public sealed class FieldNoiseTests
{
    [Theory]
    [InlineData(1.25, 0.25)]
    [InlineData(-1.25, 0.75)]
    [InlineData(3, 0)]
    public void Fract_keeps_the_part_after_the_floor(double x, double expected) =>
        Assert.Equal(expected, FieldNoise.Fract(x), 10);

    [Fact]
    public void Smooth_is_zero_and_one_outside_its_edges_and_half_in_the_middle()
    {
        Assert.Equal(0, FieldNoise.Smooth(0.2, 0.8, 0.1));
        Assert.Equal(1, FieldNoise.Smooth(0.2, 0.8, 0.9));
        Assert.Equal(0.5, FieldNoise.Smooth(0.2, 0.8, 0.5), 10);
    }

    [Fact]
    public void The_noise_is_the_same_every_time_and_stays_within_its_range()
    {
        for (var i = 0; i < 200; i++)
        {
            var (x, y) = (i * 0.37 - 20, i * 0.91 - 50);
            Assert.InRange(FieldNoise.Hash(x, y), 0, 1);
            Assert.InRange(FieldNoise.Noise(x, y), 0, 1);
            Assert.InRange(FieldNoise.Fbm(x, y), 0, 1);
            Assert.Equal(FieldNoise.Noise(x, y), FieldNoise.Noise(x, y));
        }
    }

    [Fact]
    public void Value_noise_is_the_hash_at_whole_numbers()
    {
        Assert.Equal(FieldNoise.Hash(3, -2), FieldNoise.Noise(3, -2), 12);
    }

    [Fact]
    public void The_noise_changes_smoothly()
    {
        Assert.True(Math.Abs(FieldNoise.Noise(1.5, 2.5) - FieldNoise.Noise(1.501, 2.5)) < 0.01);
    }
}

public sealed class FieldSceneTests
{
    [Fact]
    public void The_four_scenes_of_the_sketch_exist_with_a_space_first_in_every_ramp()
    {
        Assert.Equal(
            [FieldSceneKind.Swell, FieldSceneKind.Drift, FieldSceneKind.Contour, FieldSceneKind.Mesh],
            FieldScene.All.Select(s => s.Kind));
        Assert.All(FieldScene.All, scene => Assert.Equal(' ', scene.Ramp[0]));
        Assert.Equal(" .:-=+*#%@", FieldScene.For(FieldSceneKind.Mesh).Ramp);
    }

    [Theory]
    [InlineData(FieldSceneKind.Swell)]
    [InlineData(FieldSceneKind.Drift)]
    [InlineData(FieldSceneKind.Contour)]
    [InlineData(FieldSceneKind.Mesh)]
    public void Every_scene_gives_a_tone_within_its_range_that_moves_with_time(FieldSceneKind kind)
    {
        var scene = FieldScene.For(kind);
        var changed = false;
        for (var i = 0; i < 400; i++)
        {
            var (x, y) = (i % 20 * 0.09 - 0.9, i / 20 * 0.05 - 0.5);
            var now = scene.ShapedTone(x, y, 10, 0.006, 0.012);
            Assert.InRange(now, 0, 1);
            changed |= Math.Abs(now - scene.ShapedTone(x, y, 25, 0.006, 0.012)) > 1e-6;
        }

        Assert.True(changed);
    }
}

public sealed class CalmAreaTests
{
    private static readonly FieldRect Form = new(700, 200, 1100, 600);

    [Fact]
    public void The_field_is_quiet_on_the_form_and_its_margin()
    {
        Assert.Equal(0, CalmArea.Openness(Form, 900, 400));
        Assert.Equal(0, CalmArea.Openness(Form, 700 - CalmArea.Margin, 400));
    }

    [Fact]
    public void The_field_opens_further_out_and_fully_without_a_form()
    {
        Assert.InRange(CalmArea.Openness(Form, 700 - CalmArea.Margin - 70, 400), 0.01, 0.99);
        Assert.Equal(1, CalmArea.Openness(Form, 100, 400));
        Assert.Equal(1, CalmArea.Openness(null, 900, 400));
    }
}

public sealed class LanternTests
{
    [Fact]
    public void Without_ctrl_the_lantern_stays_off()
    {
        var lantern = new Lantern();

        lantern.Step(0.1, new FieldPointer(100, 100, Ctrl: false));

        Assert.False(lantern.IsActive);
        Assert.Equal(0, lantern.Glow(100, 100, 1000));
    }

    [Fact]
    public void With_ctrl_the_light_rises_where_the_mouse_is()
    {
        var lantern = new Lantern();

        lantern.Step(0.1, new FieldPointer(100, 100, Ctrl: true));
        lantern.Step(0.1, new FieldPointer(100, 100, Ctrl: true));

        Assert.Equal(1, lantern.Strength);
        Assert.Equal(100, lantern.X);
        Assert.Equal(1, lantern.Glow(100, 100, 1000), 6);
        Assert.True(lantern.Glow(400, 100, 1000) < 0.01);
    }

    [Fact]
    public void The_light_follows_the_mouse_and_leaves_a_short_trail_that_fades()
    {
        var lantern = new Lantern();
        lantern.Step(0.1, new FieldPointer(0, 0, Ctrl: true));

        for (var i = 1; i <= 30; i++)
        {
            lantern.Step(0.033, new FieldPointer(i * 30, 0, Ctrl: true));
        }

        Assert.InRange(lantern.X, 700, 900);
        Assert.InRange(lantern.TrailCount, 1, 12);

        for (var i = 0; i < 30; i++)
        {
            lantern.Step(0.033, null);
        }

        Assert.False(lantern.IsActive);
    }

    [Fact]
    public void Letting_go_of_ctrl_fades_the_light()
    {
        var lantern = new Lantern();
        lantern.Step(0.1, new FieldPointer(10, 10, Ctrl: true));
        lantern.Step(0.1, new FieldPointer(10, 10, Ctrl: true));

        lantern.Step(0.1, new FieldPointer(10, 10, Ctrl: false));

        Assert.InRange(lantern.Strength, 0.6, 0.8);
    }
}

public sealed class PathPolygonsTests
{
    [Fact]
    public void A_square_holds_its_inside_and_not_its_outside()
    {
        var square = PathPolygons.Parse("M0 0 10 0 10 10 0 10Z");

        Assert.True(square.Contains(5, 5));
        Assert.False(square.Contains(15, 5));
    }

    [Fact]
    public void Horizontal_and_vertical_lines_are_read()
    {
        var square = PathPolygons.Parse("M0 0H10V10H0Z");

        Assert.Equal(1, square.RingCount);
        Assert.True(square.Contains(9, 9));
    }

    [Fact]
    public void A_ring_that_runs_against_another_cuts_a_hole()
    {
        var frame = PathPolygons.Parse("M0 0 10 0 10 10 0 10ZM3 3 3 7 7 7 7 3Z");

        Assert.True(frame.Contains(1, 5));
        Assert.False(frame.Contains(5, 5));
    }

    [Fact]
    public void Two_rings_in_the_same_direction_fill_together()
    {
        var both = PathPolygons.Parse("M0 0 10 0 10 10 0 10Z").Union(PathPolygons.Parse("M5 5 15 5 15 15 5 15Z"));

        Assert.True(both.Contains(7, 7));
        Assert.True(both.Contains(12, 12));
    }

    [Fact]
    public void Curves_are_refused_rather_than_drawn_wrong()
    {
        Assert.Throws<FormatException>(() => PathPolygons.Parse("M0 0C1 1 2 2 3 3Z"));
    }
}

public sealed class ChainShapeTests
{
    [Fact]
    public void The_joint_is_the_logos_joint_in_the_assets()
    {
        var svg = File.ReadAllText(Path.Combine(RepositoryRoot(), "assets", "logo", "chainsaw-chain-joint.svg"));
        var start = svg.IndexOf(" d=\"", StringComparison.Ordinal) + 4;
        var data = svg[start..svg.IndexOf('"', start)];

        Assert.Equal(ChainShape.Joint, data);
    }

    [Fact]
    public void The_chain_has_forty_units_of_straight_link_between_joints()
    {
        Assert.Equal(40, ChainShape.LinkStraight);
        Assert.Equal((120 + 40) * ChainShape.Scale, ChainShape.Pitch, 10);
    }

    [Fact]
    public void The_chain_stands_straight_and_repeats_down_the_page()
    {
        for (var y = 0.5; y < 900; y += 7)
        {
            for (var x = 250.5; x < 420; x += 5)
            {
                Assert.Equal(ChainShape.Contains(x, y), ChainShape.Contains(x, y + ChainShape.Pitch));
            }
        }
    }

    [Fact]
    public void The_chain_runs_down_its_centre_line_and_nowhere_else()
    {
        var left = ChainShape.CenterX - 62 * ChainShape.Scale - 1;
        var right = ChainShape.CenterX + 62 * ChainShape.Scale + 1;
        for (var y = 0.5; y < 900; y += 3)
        {
            Assert.False(ChainShape.Contains(left, y));
            Assert.False(ChainShape.Contains(right, y));
        }

        var covered = Enumerable.Range(0, 900).Count(y => ChainShape.Contains(ChainShape.CenterX - 46, y + 0.5));
        Assert.True(covered > 600);
    }

    [Fact]
    public void A_link_has_its_hole_between_the_bars()
    {
        // In the straight stretch below a joint, the middle is empty and the bars on either side are filled.
        var y = -2 * ChainShape.Pitch + 2 * ChainShape.Pitch + 80 * ChainShape.Scale;

        Assert.False(ChainShape.Contains(ChainShape.CenterX, y));
        Assert.True(ChainShape.Contains(ChainShape.CenterX - 46 * ChainShape.Scale, y));
        Assert.True(ChainShape.Contains(ChainShape.CenterX + 46 * ChainShape.Scale, y));
    }

    [Fact]
    public void The_cover_lies_in_the_left_strip_only()
    {
        const double CellWidth = 6.6;
        var columns = 218;
        var cover = ChainShape.Cover(columns, 80, CellWidth, 11);

        for (var row = 0; row < 80; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var value = cover[row * columns + column];
                Assert.InRange(value, 0, 1);
                if (column * CellWidth > 400 || (column + 1) * CellWidth < 260)
                {
                    Assert.Equal(0, value);
                }
            }
        }

        Assert.Contains(cover, value => value == 1);
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

public sealed class FieldEngineTests
{
    private static FieldInput Input(
        double seconds = 1, bool animate = true, FieldPointer? pointer = null, FieldRect? form = null) =>
        new(1440, 864, 6.6, 11, seconds, animate, pointer, form);

    [Fact]
    public void A_frame_has_a_character_for_every_cell()
    {
        var frame = new FieldEngine().Next(Input());

        Assert.Equal(219, frame.Columns);
        Assert.Equal(79, frame.Rows);
        Assert.Equal(frame.Columns * frame.Rows, frame.Background.Length);
        Assert.Contains(frame.Background, c => c != ' ');
    }

    [Fact]
    public void The_chain_is_drawn_in_its_strip_in_its_own_characters_over_an_empty_field()
    {
        var frame = new FieldEngine().Next(Input());

        Assert.NotEmpty(frame.Chain);
        Assert.All(frame.Chain, mark =>
        {
            Assert.InRange(mark.Column * 6.6, 260, 400);
            Assert.Contains(mark.Character, FieldEngine.ChainRamp[1..]);
            Assert.InRange(mark.Alpha, 0.55, 1);
            Assert.Equal(' ', frame.Background[mark.Row * frame.Columns + mark.Column]);
        });
    }

    [Fact]
    public void The_field_is_quiet_around_the_form()
    {
        var form = new FieldRect(760, 250, 1140, 610);
        var frame = new FieldEngine().Next(Input(form: form));

        for (var row = 0; row < frame.Rows; row++)
        {
            for (var column = 0; column < frame.Columns; column++)
            {
                var (x, y) = ((column + 0.5) * 6.6, (row + 0.5) * 11);
                if (x > form.Left && x < form.Right && y > form.Top && y < form.Bottom)
                {
                    Assert.Equal(' ', frame.Background[row * frame.Columns + column]);
                }
            }
        }
    }

    [Fact]
    public void The_field_moves_with_time()
    {
        var engine = new FieldEngine();
        var first = engine.Next(Input(seconds: 1)).Background;
        var later = engine.Next(Input(seconds: 30)).Background;

        Assert.NotEqual(first, later);
    }

    [Fact]
    public void With_windows_animations_off_the_field_stands_still_and_rests()
    {
        var engine = new FieldEngine();
        var first = engine.Next(Input(seconds: 1, animate: false)).Background;

        Assert.False(engine.NeedsFrame(Input(seconds: 30, animate: false)));
        Assert.Equal(first, engine.Next(Input(seconds: 30, animate: false)).Background);
    }

    [Fact]
    public void A_still_field_is_drawn_again_when_the_page_or_the_scene_changes()
    {
        var engine = new FieldEngine();
        engine.Next(Input(animate: false));

        Assert.True(engine.NeedsFrame(Input(animate: false, form: new FieldRect(1, 2, 3, 4))));
        Assert.True(engine.NeedsFrame(Input(animate: false) with { Width = 1000 }));
        engine.Scene = FieldSceneKind.Mesh;
        Assert.True(engine.NeedsFrame(Input(animate: false)));
    }

    [Fact]
    public void The_lantern_lights_the_field_under_the_mouse_while_ctrl_is_held()
    {
        var engine = new FieldEngine();
        var pointer = new FieldPointer(1200, 700, Ctrl: true);
        engine.Next(Input(seconds: 1, animate: false, pointer: pointer));
        var frame = engine.Next(Input(seconds: 1.2, animate: false, pointer: pointer));

        Assert.True(engine.NeedsFrame(Input(seconds: 1.3, animate: false, pointer: pointer)));
        Assert.NotEmpty(frame.Lit);
        Assert.All(frame.Lit, mark => Assert.True(Math.Abs(mark.Column * 6.6 - 1200) < 400));
    }

    [Fact]
    public void Without_ctrl_nothing_is_lit()
    {
        var engine = new FieldEngine();
        var pointer = new FieldPointer(1200, 700, Ctrl: false);

        engine.Next(Input(pointer: pointer));

        Assert.Empty(engine.Next(Input(seconds: 2, pointer: pointer)).Lit);
    }
}

public sealed class FieldRasterizerTests
{
    private static readonly GlyphAtlas Atlas = new(
        4, 4, 1, GlyphAtlas.Characters.Where(c => c != ' ').ToDictionary(c => c, _ => Full()));

    private static GlyphMask Full() => new(2, 2, [255, 255, 255, 255]);

    [Fact]
    public void An_empty_frame_is_the_page()
    {
        var pixels = new int[16 * 8];
        var frame = new FieldFrame(4, 2, 4, 4, new string(' ', 8).ToCharArray(), [], []);

        FieldRasterizer.Draw(frame, Atlas, FieldPalette.Default, pixels, 16, 8);

        Assert.All(pixels, pixel => Assert.Equal(0x191919, pixel));
    }

    [Fact]
    public void A_character_blends_its_colour_by_its_strength()
    {
        var pixels = new int[16 * 8];
        var background = new string(' ', 8).ToCharArray();
        background[0] = '#';
        var frame = new FieldFrame(4, 2, 4, 4, background, [], [new FieldMark(1, 1, '#', 1)]);

        FieldRasterizer.Draw(frame, Atlas, FieldPalette.Default, pixels, 16, 8);

        Assert.NotEqual(0x191919, pixels[0]);
        Assert.True((pixels[0] & 0xFF) < 0x6A);
        Assert.Equal(0xD6D6D6, pixels[4 * 16 + 4]);
        Assert.Equal(0x191919, pixels[2]);
    }

    [Fact]
    public void Characters_at_the_edge_are_cut_off_and_a_small_buffer_is_refused()
    {
        var frame = new FieldFrame(1, 1, 4, 4, ['#'], [], []);

        FieldRasterizer.Draw(frame, Atlas, FieldPalette.Default, new int[1], 1, 1);
        Assert.Throws<ArgumentException>(() =>
            FieldRasterizer.Draw(frame, Atlas, FieldPalette.Default, new int[1], 2, 2));
    }

    [Fact]
    public void The_chain_is_the_accent_deeper_on_its_left_and_lighter_on_its_right()
    {
        var palette = FieldPalette.Default;
        var middle = palette.ChainColor(ChainShape.CenterX);

        Assert.True((palette.ChainColor(ChainShape.CenterX - 150) >> 16) < (middle >> 16));
        Assert.True((palette.ChainColor(ChainShape.CenterX + 150) & 0xFF) > (middle & 0xFF));
        Assert.Equal(0xFF, palette.ChainColor(10_000) >> 16);
    }

    [Fact]
    public void The_atlas_has_every_character_of_every_scene_once()
    {
        var characters = GlyphAtlas.Characters;

        Assert.Equal(characters.Length, characters.Distinct().Count());
        Assert.All(FieldScene.All.SelectMany(s => s.Ramp).Concat(FieldEngine.ChainRamp), c => Assert.Contains(c, characters));
    }
}
