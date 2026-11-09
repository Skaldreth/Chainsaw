using Chainsaw.App.Graph;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>
/// An unfolded account cluster in the graph (sketch KE1): an area in the service's tone behind its accounts, always
/// drawn, named as a likely service, and the accounts' inspector says why.
/// </summary>
public sealed class AccountClusterAreaTests
{
    [Fact]
    public void An_open_cluster_has_an_area_behind_its_accounts_that_is_always_drawn()
    {
        var scene = Scene(isOpen: true);

        var area = Assert.Single(GraphAreas.Build(scene, [], [], TimeZoneInfo.Utc), GraphAreas.IsAccountCluster);

        Assert.Equal(AreaSort.Service, area.Sort);
        Assert.Equal(AreaLine.Solid, area.Line);
        Assert.Equal(2, area.Nodes.Count);
        Assert.Equal("Trolig tjänst · 2 adresser", area.Label);
    }

    [Fact]
    public void A_closed_cluster_has_no_area()
    {
        var scene = Scene(isOpen: false);

        Assert.DoesNotContain(GraphAreas.Build(scene, [], [], TimeZoneInfo.Utc), GraphAreas.IsAccountCluster);
    }

    [Fact]
    public void An_account_in_a_cluster_says_why_in_the_inspector()
    {
        var scene = Scene(isOpen: true);
        var node = scene.Nodes.First(view => view.Node.Accounts.Contains(EthSamples.S.ToChainAddress()));

        var sentences = InspectorContent.NodeSummary(scene, node, 1);

        Assert.Contains(sentences, sentence => sentence.StartsWith("Trolig tjänst · 2 adresser. deposit-sweep v1",
            StringComparison.Ordinal));
        Assert.NotNull(InspectorContent.AccountClusterOf(scene, node.Node));
    }

    [Fact]
    public void An_account_of_an_open_cluster_can_be_broken_out_and_one_of_a_closed_cannot()
    {
        var open = Scene(isOpen: true);
        var node = open.Nodes.First(view => view.Node.Accounts.Contains(EthSamples.S.ToChainAddress()));

        var cluster = InspectorForms.AccountClusterToSplit(open, node);

        Assert.NotNull(cluster);
        Assert.StartsWith("Bryter ut ", InspectorForms.AccountSplitNotice(cluster, node), StringComparison.Ordinal);
        var closed = Scene(isOpen: false);
        Assert.Null(InspectorForms.AccountClusterToSplit(closed, closed.Nodes.First(view => view.Node.Accounts.Count == 1
            && view.Node.Accounts[0] == EthSamples.V.ToChainAddress())));
    }

    private static GraphScene Scene(bool isOpen)
    {
        var members = new[] { EthSamples.M.ToChainAddress(), EthSamples.S.ToChainAddress() }
            .OrderBy(account => account.Value, StringComparer.Ordinal).ToList();
        var graph = EthSamples.Graph() with
        {
            AccountClusters =
            [
                new GraphAccountCluster(GraphAccountCluster.KeyOf(members[0]), Chain.Ethereum, members, members[1],
                    isOpen, ["deposit-sweep v1: S tömdes till M."]),
            ],
        };
        return GraphScene.Build(new GraphSnapshot(
            graph, new Dictionary<string, IReadOnlyList<Label>>(), TracingMethodKind.Fifo, [], null, null));
    }
}
