using Chainsaw.App.Graph;

namespace Chainsaw.App.Tests;

/// <summary>
/// When the graph's scene is drawn again and when its drawing is only moved (principle 6): a pan inside the margin
/// and a zoom under way move it, everything else makes it again; without WPF.
/// </summary>
public sealed class SceneWindowTests
{
    /// <summary>The part on screen in the tests: 1000 × 600 at zoom 1, from the origin.</summary>
    private static readonly GraphRect Screen = new(0, 0, 1000, 600);

    [Fact]
    public void Nothing_is_covered_before_the_scene_is_drawn()
    {
        var window = new SceneWindow();

        Assert.False(window.Covers(Screen, 1, zooming: false));
        Assert.Null(window.Region);
    }

    [Fact]
    public void The_scene_is_drawn_over_the_screen_and_a_margin_on_each_side()
    {
        var window = new SceneWindow();

        var region = window.Take(Screen, 1, SceneWindow.Margin);

        Assert.Equal(new GraphRect(-250, -150, 1500, 900), region);
        Assert.Equal(region, window.Region);
    }

    [Fact]
    public void A_pan_inside_the_margin_moves_the_drawing_and_one_past_it_draws_again()
    {
        var window = new SceneWindow();
        window.Take(Screen, 1, SceneWindow.Margin);

        Assert.True(window.Covers(Screen with { X = 240, Y = -150 }, 1, zooming: false));
        Assert.False(window.Covers(Screen with { X = 260 }, 1, zooming: false));
    }

    [Fact]
    public void A_new_zoom_draws_again_once_the_zoom_has_ended()
    {
        var window = new SceneWindow();
        window.Take(Screen, 1, SceneWindow.Margin);
        var zoomedIn = new GraphRect(100, 60, 800, 480);

        Assert.True(window.Covers(zoomedIn, 1.25, zooming: true));
        Assert.False(window.Covers(zoomedIn, 1.25, zooming: false));
    }

    [Fact]
    public void A_zoom_out_past_the_margin_draws_again_even_while_it_is_under_way()
    {
        var window = new SceneWindow();
        window.Take(Screen, 1, SceneWindow.Margin);

        Assert.False(window.Covers(new GraphRect(-500, -300, 2000, 1200), 0.5, zooming: true));
    }

    [Fact]
    public void A_change_to_what_is_drawn_makes_the_drawing_stale()
    {
        var window = new SceneWindow();
        window.Take(Screen, 1, SceneWindow.Margin);

        window.Forget();

        Assert.False(window.Covers(Screen, 1, zooming: false));
    }

    [Fact]
    public void Without_a_margin_only_the_screen_is_drawn_and_any_pan_draws_again()
    {
        var window = new SceneWindow();

        Assert.Equal(Screen, window.Take(Screen, 1, margin: 0));
        Assert.True(window.Covers(Screen, 1, zooming: false));
        Assert.False(window.Covers(Screen with { X = 1 }, 1, zooming: false));
    }

    [Fact]
    public void An_empty_screen_is_never_covered()
    {
        var window = new SceneWindow();
        window.Take(Screen, 1, SceneWindow.Margin);

        Assert.False(window.Covers(new GraphRect(0, 0, 0, 0), 1, zooming: false));
    }
}
