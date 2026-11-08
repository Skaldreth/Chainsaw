using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// A watch's hit to something in the trash (sketches BV2a and SO1a): a line into the trash with the amount and rings,
/// never a silent stub; and a node that shrinks as it nears the bin.
/// </summary>
public sealed class TrashHitTests
{
    private readonly TestWatchSource _source = new();

    /// <summary>The sample graph with U, which V paid, in the trash, found by its address as a real throw is.</summary>
    private static GraphScene SceneWithUThrown() => Scene().WithTrash(new TrashFilter(
    [
        new TrashThrow(
            1,
            [new TrashMember(UKey, null, "U", "Unknown") { Keys = [GraphPile.MemberKey(U)] }],
            new OperatorProfile("Testa", "Utredare", "Enheten"),
            DateTimeOffset.UnixEpoch,
            1),
    ]));

    private GraphWatchOverlay Build(GraphScene scene) => GraphWatches.Build(
        scene,
        new WatchSnapshot(_source.Watches, _source.Records, _source.Hits, _source.Checked, _source.Seen),
        _ => WatchGate.Open);

    [Fact]
    public void A_hit_to_a_thrown_counterparty_is_a_line_into_the_trash_with_its_amount_that_moves_until_seen()
    {
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter(WatchDirection.Out, [WatchSamples.On(U)]));
        _source.Hit(watch, WatchSamples.Out(WatchSamples.On(U), 0.3m));

        var unseen = Build(SceneWithUThrown());
        _source.Seen[watch.Id] = 1;
        var seen = Build(SceneWithUThrown());

        var hit = Assert.Single(unseen.TrashHits);
        Assert.Equal(new WatchTrashHit(VKey, true, false, hit.Tip, "0,3000 BTC", true, watch.Id), hit);
        Assert.True(unseen.HasTrashHit(VKey, isOut: true));
        Assert.False(unseen.HasTrashHit(VKey, isOut: false));
        Assert.DoesNotContain(unseen.Lines, line => line.Kind == WatchLineKind.Hit);
        Assert.True(unseen.Moves);
        Assert.False(Assert.Single(seen.TrashHits).IsNew);
        Assert.False(seen.Moves);
    }

    [Fact]
    public void A_hit_to_a_counterparty_in_the_graph_or_outside_it_is_no_hit_into_the_trash()
    {
        var watch = _source.Add(WatchSamples.Subject(V), WatchSamples.Filter(WatchDirection.Out));
        _source.Hit(watch, WatchSamples.Out(WatchSamples.On(U), 0.3m));
        _source.Hit(watch, WatchSamples.Out(WatchSamples.On(Address(77)), 0.1m));

        Assert.Empty(Build(Scene()).TrashHits);
        Assert.Contains(Build(Scene()).Lines, line => line is { From: VKey, To: UKey, Kind: WatchLineKind.Hit });
    }

    [Fact]
    public void A_paused_watch_draws_no_hit_into_the_trash()
    {
        var watch = _source.Add(
            WatchSamples.Subject(V), WatchSamples.Filter(WatchDirection.Out), state: WatchState.Paused);
        _source.Hit(watch, WatchSamples.Out(WatchSamples.On(U), 0.3m));

        Assert.Empty(Build(SceneWithUThrown()).TrashHits);
    }

    [Fact]
    public void An_address_is_found_in_the_trash_by_its_key_on_its_chain()
    {
        var ether = new ChainAddress(Chain.Ethereum, "0x00000000000000000000000000000000000000aa");
        var trash = new TrashFilter(
        [
            new TrashThrow(
                1,
                [
                    new TrashMember(GraphPile.MemberKey(U), null, "U", "Unknown"),
                    new TrashMember(GraphPile.AccountKey(ether), null, "E", "Unknown"),
                ],
                new OperatorProfile("Testa", "Utredare", "Enheten"),
                DateTimeOffset.UnixEpoch,
                1),
        ]);

        Assert.True(trash.HidesAddress(WatchSamples.On(U)));
        Assert.True(trash.HidesAddress(ether));
        Assert.False(trash.HidesAddress(WatchSamples.On(V)));
        Assert.False(trash.HidesAddress(new ChainAddress(Chain.Tron, U.Value)));
    }

    [Theory]
    [InlineData(600, 1, 0.95)]
    [InlineData(0, 0.55, 0.75)]
    public void A_dragged_node_shrinks_and_fades_a_little_as_it_nears_the_bin(
        double distance, double scale, double opacity)
    {
        var bin = new TrashBinViewModel();

        bin.DragAt(distance);

        Assert.Equal(scale, bin.DraggedScale, 6);
        Assert.Equal(opacity, bin.DraggedOpacity, 6);
    }

    [Fact]
    public void Nothing_dragged_is_drawn_whole()
    {
        var bin = new TrashBinViewModel();
        bin.DragAt(0);

        bin.EndDrag();

        Assert.Equal(1, bin.DraggedScale);
        Assert.Equal(1, bin.DraggedOpacity);
    }

    [Fact]
    public void Halfway_to_the_bin_a_node_has_shrunk_halfway()
    {
        var bin = new TrashBinViewModel();

        bin.DragAt(TrashBinViewModel.FullAt + (TrashBinViewModel.Reach / 2));

        Assert.Equal(1 - (TrashBinViewModel.ShrinkAtBin / 2), bin.DraggedScale, 6);
        Assert.False(bin.IsHot);
    }
}
