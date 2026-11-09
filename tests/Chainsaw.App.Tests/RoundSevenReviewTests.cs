using Chainsaw.App.Graph;
using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>The review of round 7: putting back from a search row, the ways out at the edge and the words for a side.</summary>
public sealed class RoundSevenReviewTests
{
    /// <summary>Who threw, in every made-up throw.</summary>
    private static readonly OperatorProfile By = new("Testa", "Utredare", "Enheten");

    /// <summary>The cluster K as the graph holds it.</summary>
    private static GraphNode Cluster => Scene().Snapshot.Graph.Nodes.Single(node => node.Key == KKey);

    [Fact]
    public void A_node_the_user_threw_itself_is_put_back_from_its_row()
    {
        var filter = new TrashFilter([Throw(3, new TrashMember(KKey, null, "K", "Cluster") { Keys = KeysOfK() })]);

        Assert.Equal(new TrashPlace(3, KKey), filter.PlaceOf(Cluster));
    }

    [Fact]
    public void A_later_throw_that_holds_one_of_the_nodes_addresses_never_takes_the_put_back()
    {
        // K was thrown (3), then an address alone (8) that later became part of K: Enter puts K back, not the address.
        var keys = KeysOfK();
        var filter = new TrashFilter(
        [
            Throw(3, new TrashMember(KKey, null, "K", "Cluster") { Keys = keys }),
            Throw(8, new TrashMember("address:x", null, "X", "Unknown") { Keys = [keys[^1]] }),
        ]);

        Assert.Equal(new TrashPlace(3, KKey), filter.PlaceOf(Cluster));
    }

    [Fact]
    public void A_node_still_hidden_by_another_throw_after_the_put_back_opens_the_trash_instead()
    {
        var keys = KeysOfK();
        var filter = new TrashFilter(
        [
            Throw(3, new TrashMember(KKey, null, "K", "Cluster") { Keys = keys }),
            Throw(8, new TrashMember("address:x", null, "X", "Unknown") { Keys = keys }),
        ]);

        Assert.Null(filter.PlaceOf(Cluster));
    }

    [Fact]
    public void A_node_that_only_followed_another_into_the_trash_opens_the_trash_instead()
    {
        var filter = new TrashFilter(
        [
            Throw(
                3,
                new TrashMember(VKey, null, "V", "Unknown"),
                new TrashMember(KKey, VKey, "K", "Cluster") { Keys = KeysOfK() }),
        ]);

        Assert.Null(filter.PlaceOf(Cluster));
    }

    [Fact]
    public void Several_ways_out_of_a_mixer_at_the_edge_never_lie_on_each_other()
    {
        var scene = Scene(new Dictionary<string, IReadOnlyList<Chainsaw.Modules.Attribution.Contracts.Label>>
        {
            [UKey] = [Label(U, Chainsaw.Modules.Attribution.Contracts.LabelKind.Mixer, "Virveln")],
        });
        var line = scene.Snapshot.Graph.Edges.Single(edge => edge.Key == VU);
        var paid = new ExposureResult(
            VKey, ExposureDirection.Out, 3, new TracingMethodId(TracingMethodKind.Fifo, 1), line.Amount,
            [new ExposureReach(UKey, line.Amount, 3, [VKey, UKey], [VU], [])],
            new Dictionary<ExposureRest, AssetAmount>(),
            [new ExposureFlow(VU, VKey, UKey, line.Amount)],
            new Dictionary<string, int> { [VKey] = 0, [UKey] = 3 });
        var got = paid with { Direction = ExposureDirection.In, Reached = [], Flows = [] };
        // More ways out than fit within the span: clamped one by one, the last ones lay on each other.
        var ways = Enumerable.Range(0, 17).Select(index => new ApprovedWayOut(UKey, $"address:x{index}")).ToList();

        var picture = ExposureLayout.Build(scene.Node(VKey)!, paid, got, ExposureNames.Of(scene), ways);

        Assert.Equal(17, picture.Candidates.Select(place => place.At).Distinct().Count());
    }

    [Fact]
    public void Money_got_from_another_chain_is_said_as_coming_from_it()
    {
        Assert.Equal(
            "Kom från en tx som fortsatte på annan kedja",
            ExposureViewModel.RestText(ExposureRest.OtherChain, 2, "klustret", ExposureDirection.In));
        Assert.Equal(
            "Fortsätter på annan kedja",
            ExposureViewModel.RestText(ExposureRest.OtherChain, 2, "klustret", ExposureDirection.Out));
    }

    /// <summary>Gives the keys K is known by in the trash: its addresses.</summary>
    /// <returns>The keys.</returns>
    private static IReadOnlyList<string> KeysOfK() => TrashFilter.KeysOf(Cluster);

    /// <summary>Makes a throw.</summary>
    /// <param name="sequence">Its record.</param>
    /// <param name="members">What lies in it.</param>
    /// <returns>The throw.</returns>
    private static TrashThrow Throw(long sequence, params TrashMember[] members) =>
        new(sequence, members, By, DateTimeOffset.UnixEpoch, sequence);
}
