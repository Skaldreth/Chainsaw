using static Chainsaw.Modules.Clustering.Tests.Synthetic;

namespace Chainsaw.Modules.Clustering.Tests;

/// <summary>Stage 15: the clusters of many addresses at once are the clusters one at a time, built once.</summary>
public sealed class BatchTests : IDisposable
{
    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void Many_addresses_at_once_get_the_same_clusters_as_one_at_a_time()
    {
        _harness.Analyse(Patterns.PaymentWithChange());
        var payer = Wpkh("payer-1");
        var shop = Pkh("shop");

        var clusters = _harness.Service.GetClusters(_harness.Case, [payer, shop, payer]);

        Assert.Equal(
            _harness.ClusterOf(payer).Addresses.OrderBy(address => address.Value),
            clusters[payer].Addresses.OrderBy(address => address.Value));
        Assert.Single(clusters[shop].Members);
        foreach (var member in clusters[payer].Addresses)
        {
            // The other members share the same cluster, explained from the first address asked for.
            Assert.Same(clusters[payer], clusters[member]);
        }
    }

    [Fact]
    public void An_address_nothing_links_is_its_own_cluster()
    {
        var lonely = Wpkh("lonely");

        var cluster = Assert.Single(_harness.Service.GetClusters(_harness.Case, [lonely])).Value;

        Assert.Equal(lonely, cluster.Start);
        Assert.Single(cluster.Members);
    }
}
