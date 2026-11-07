using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using static Chainsaw.Modules.Clustering.Tests.Synthetic;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>How the clusters are built from stored proposals and splits, without a case.</summary>
public sealed class ClusterGraphTests
{
    private static readonly HeuristicId Cio = new(HeuristicNames.CommonInputOwnership, 1);
    private static readonly HeuristicId Change = new(HeuristicNames.ChangeAddress, 1);

    [Fact]
    public void A_merge_that_would_join_a_split_joins_none_of_its_addresses()
    {
        var split = Split("a", "b", HeuristicNames.CommonInputOwnership);
        var graph = new ClusterGraph([Evidence(1, Cio, "a", "c", "b")], [split]);

        var cluster = graph.ClusterOf(Wpkh("c"));

        Assert.Single(cluster.Members);
        Assert.Equal(LinkState.RefusedBySplit, Assert.Single(cluster.Refused).State);
    }

    [Fact]
    public void A_split_holds_across_a_chain_of_merges()
    {
        var split = Split("a", "c", HeuristicNames.ChangeAddress);
        var graph = new ClusterGraph(
            [Evidence(1, Cio, "a", "b"), Evidence(2, Change, "b", "c"), Evidence(3, Cio, "c", "d")], [split]);

        Assert.Equal(2, graph.ClusterOf(Wpkh("a")).Members.Count);
        Assert.Equal(2, graph.ClusterOf(Wpkh("c")).Members.Count);
        Assert.Null(graph.Path(Wpkh("a"), Wpkh("c")));
    }

    [Fact]
    public void Suggestions_and_stopped_proposals_merge_nothing()
    {
        var graph = new ClusterGraph(
            [
                Evidence(1, Cio, EvidenceStrength.Suggestion, "a", "b"),
                Evidence(2, Cio, EvidenceStrength.Stopped, "a", "c"),
            ],
            []);

        var cluster = graph.ClusterOf(Wpkh("a"));

        Assert.Single(cluster.Members);
        Assert.Single(cluster.Suggestions);
        Assert.Equal(LinkState.Stopped, Assert.Single(cluster.Refused).State);
    }

    [Fact]
    public void The_path_follows_the_links_in_order()
    {
        var graph = new ClusterGraph(
            [Evidence(1, Cio, "a", "b"), Evidence(2, Change, "b", "c"), Evidence(3, Cio, "c", "d")], []);

        var path = graph.Path(Wpkh("a"), Wpkh("d"))!;

        Assert.Equal([1L, 2L, 3L], path.Select(link => link.Evidence.Sequence));
        var d = graph.ClusterOf(Wpkh("a")).Members.Single(member => member.Address == Wpkh("d"));
        Assert.Equal(Wpkh("c"), d.Via);
    }

    private static ClusterEvidence Evidence(long sequence, HeuristicId heuristic, params string[] labels) =>
        Evidence(sequence, heuristic, EvidenceStrength.Merge, labels);

    private static ClusterEvidence Evidence(
        long sequence, HeuristicId heuristic, EvidenceStrength strength, params string[] labels) =>
        new(sequence, heuristic, Txid($"t{sequence}"), [.. labels.Select(Wpkh)], strength, "Skäl.", null, null, 1,
            DateTimeOffset.UnixEpoch);

    private static ClusterSplit Split(string first, string second, string heuristic) =>
        new(1, Wpkh(first), Wpkh(second), heuristic, "Skäl.", 2, DateTimeOffset.UnixEpoch);
}
