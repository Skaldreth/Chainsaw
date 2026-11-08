using Chainsaw.App.Graph;

namespace Chainsaw.App.Tests;

/// <summary>
/// What the graph's drawing keeps between frames (principle 6): the cache of pens and texts, and the grid that finds
/// the amounts near an amount; without WPF.
/// </summary>
public sealed class DrawCacheTests
{
    [Fact]
    public void A_value_is_made_once_for_its_key_and_given_again_in_every_frame()
    {
        var cache = new DrawCache<(string Text, double Size), object>();
        var first = cache.Get(("0,038 BTC · 1 tx", 10.5), _ => new object());

        for (var frame = 0; frame < 60; frame++)
        {
            Assert.Same(first, cache.Get(("0,038 BTC · 1 tx", 10.5), _ => new object()));
        }

        cache.Get(("0,038 BTC · 1 tx", 11), _ => new object());
        Assert.Equal(2, cache.Made);
        Assert.Equal(2, cache.Count);
    }

    [Fact]
    public void A_full_cache_starts_over_instead_of_growing_without_end()
    {
        var cache = new DrawCache<int, int>(capacity: 3);

        foreach (var key in Enumerable.Range(0, 3))
        {
            cache.Get(key, value => value * 2);
        }

        Assert.Equal(3, cache.Count);
        Assert.Equal(8, cache.Get(4, value => value * 2));
        Assert.Equal(1, cache.Count);
        Assert.Equal(0, cache.Get(0, value => value * 2));
        Assert.Equal(5, cache.Made);
    }

    [Fact]
    public void Clearing_forgets_every_value_and_a_capacity_must_hold_one()
    {
        var cache = new DrawCache<string, string>(comparer: StringComparer.Ordinal);
        cache.Get("a", key => key);

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => new DrawCache<string, string>(capacity: 0));
    }

    [Fact]
    public void The_grid_finds_what_touches_a_rectangle_across_cells_and_leaves_the_rest()
    {
        var grid = new RoomGrid(cell: 100);
        grid.Add(new GraphRect(90, 90, 30, 30));

        Assert.True(grid.Touches(new GraphRect(115, 115, 10, 10)));
        Assert.True(grid.Touches(new GraphRect(120, 50, 10, 40)));
        Assert.False(grid.Touches(new GraphRect(121, 121, 10, 10)));
        Assert.False(grid.Touches(new GraphRect(-500, -500, 10, 10)));
    }

    [Fact]
    public void Try_add_takes_free_room_only_so_that_two_amounts_never_lie_on_each_other()
    {
        var grid = new RoomGrid();

        Assert.True(grid.TryAdd(new GraphRect(0, 0, 60, 16)));
        Assert.False(grid.TryAdd(new GraphRect(40, 10, 60, 16)));
        Assert.True(grid.TryAdd(new GraphRect(0, 40, 60, 16)));
        Assert.Equal(2, grid.Count);
    }

    [Fact]
    public void The_grid_answers_as_comparing_every_pair_would_on_many_amounts()
    {
        var random = new Random(34);
        var grid = new RoomGrid();
        var all = new List<GraphRect>();
        for (var index = 0; index < 400; index++)
        {
            var rect = new GraphRect(random.Next(-2_000, 2_000), random.Next(-2_000, 2_000), 70, 16);
            var free = !all.Any(rect.Intersects);

            Assert.Equal(free, grid.TryAdd(rect));
            if (free)
            {
                all.Add(rect);
            }
        }

        Assert.Equal(all.Count, grid.Count);
    }
}
