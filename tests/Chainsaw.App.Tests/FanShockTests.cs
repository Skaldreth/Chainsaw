using Chainsaw.App.Graph;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Tests;

/// <summary>
/// The circular shock and the centring that open a pile, shared by the graph and the start page (sketches EX2b and
/// GR8a), and the graph's ring that the start page's piles now fan out in.
/// </summary>
public sealed class FanShockTests
{
    [Fact]
    public void The_first_ring_leaves_at_once_and_the_second_a_little_later()
    {
        Assert.Equal(new ShockRing(FanShock.StartRadius, FanShock.RingOpacity), FanShock.Ring(0, TimeSpan.Zero));
        Assert.Null(FanShock.Ring(1, TimeSpan.Zero));
        Assert.NotNull(FanShock.Ring(1, FanShock.SecondRingDelay));
    }

    [Fact]
    public void A_ring_grows_fast_and_slows_as_it_fades_and_is_gone_when_its_time_is_over()
    {
        var quarter = FanShock.Ring(0, FanShock.RingTime / 4)!.Value;
        var half = FanShock.Ring(0, FanShock.RingTime / 2)!.Value;

        Assert.True(quarter.Radius - FanShock.StartRadius > half.Radius - quarter.Radius);
        Assert.Equal(FanShock.RingOpacity / 2, half.Opacity, 6);
        Assert.True(half.Radius < FanShock.EndRadius);
        Assert.Null(FanShock.Ring(0, FanShock.RingTime));
        Assert.Null(FanShock.Ring(1, FanShock.Length));
    }

    [Fact]
    public void The_shock_lasts_until_the_last_ring_has_faded()
    {
        Assert.Equal(FanShock.SecondRingDelay + FanShock.RingTime, FanShock.Length);
    }

    [Fact]
    public void The_centring_pans_the_pile_to_the_middle_without_zooming()
    {
        var from = new Viewport(10, 20, 1.5);
        var pile = new GraphPoint(100, 40);
        var middle = new GraphPoint(600, 400);

        var start = FanShock.Centring(from, pile, middle, 0);
        var half = FanShock.Centring(from, pile, middle, 0.5);
        var end = FanShock.Centring(from, pile, middle, 1);

        Assert.Equal(from, start);
        Assert.Equal(middle, end.ToScreen(pile));
        Assert.Equal(from.Zoom, end.Zoom);
        var halfway = half.ToScreen(pile);
        var before = from.ToScreen(pile);
        Assert.Equal((before.X + middle.X) / 2, halfway.X, 6);
        Assert.Equal((before.Y + middle.Y) / 2, halfway.Y, 6);
        Assert.Equal(end, FanShock.Centring(from, pile, middle, 2));
    }

    [Fact]
    public void A_ring_that_fits_is_left_as_it_is()
    {
        var ring = PileFanLayout.Ring(8);

        Assert.Same(ring, PileFanLayout.FitRing(ring, 1400, 800));
    }

    [Fact]
    public void A_ring_in_a_small_window_is_drawn_in_but_never_past_where_tiles_would_touch()
    {
        var ring = PileFanLayout.Ring(12);

        var fitted = PileFanLayout.FitRing(ring, 1000, 400);
        var tiny = PileFanLayout.FitRing(ring, 300, 200);

        Assert.All(fitted, centre => Assert.True(Math.Abs(centre.Y) + (PileFanLayout.TileHeight / 2) <= 200));
        Assert.True(fitted.Max(centre => Math.Abs(centre.X)) < ring.Max(centre => Math.Abs(centre.X)));
        var smallest = ring.Max(centre => Math.Abs(centre.X)) * PileFanLayout.SmallestRingFit;
        Assert.Equal(smallest, tiny.Max(centre => Math.Abs(centre.X)), 6);
    }

    [Fact]
    public void The_start_pages_cards_stand_on_the_graphs_ring_from_the_top()
    {
        var ring = PileFanLayout.Ring(3);

        Assert.Equal(3, ring.Count);
        Assert.All(ring, centre => Assert.True(Math.Abs(centre.X) > 0 || Math.Abs(centre.Y) > 0));
        Assert.True(ring[0].Y < 0);
    }
}
