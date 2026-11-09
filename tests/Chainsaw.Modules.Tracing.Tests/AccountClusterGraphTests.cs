using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// An account cluster in the graph (sketch KE1): closed, its accounts are one node, the cluster's, and the lines to
/// them meet it; open, every account is its own node again, and the graph keeps the cluster for its area.
/// </summary>
public sealed class AccountClusterGraphTests
{
    private static readonly EthereumAddress Scammer = Address(2);

    private static readonly EthereumAddress[] Members = [Address(3), Address(4), Address(9)];

    [Fact]
    public void A_closed_cluster_is_one_node_that_the_lines_meet()
    {
        var graph = Build(isOpen: false);

        var node = Assert.Single(graph.Nodes, node => node.Key == Cluster(false).Key);
        Assert.Equal(GraphNodeKind.Cluster, node.Kind);
        Assert.Equal(3, node.ClusterSize);
        Assert.Equal(Members.Select(member => member.ToChainAddress()), node.Accounts);
        Assert.DoesNotContain(graph.Nodes, other => other.Accounts.Count == 1
            && Members.Select(member => member.ToChainAddress()).Contains(other.Accounts[0]));
        Assert.Contains(graph.Edges, edge => edge.To == node.Key);
        Assert.Single(graph.AccountClusters);
    }

    [Fact]
    public void A_closed_cluster_keeps_its_collector_s_end_and_draws_no_line_to_itself()
    {
        var hint = new TrailEndHint(TrailEndReason.Exchange, "test", "Börs");

        var graph = Build(isOpen: false, ends: account => account == Members[2].ToChainAddress() ? [hint] : []);

        var node = Assert.Single(graph.Nodes, node => node.Key == Cluster(false).Key);
        Assert.Contains(hint, node.Ends);
        Assert.DoesNotContain(graph.Edges, edge => edge.From == node.Key && edge.To == node.Key);
    }

    [Fact]
    public void An_open_cluster_is_its_accounts_as_their_own_nodes()
    {
        var graph = Build(isOpen: true);

        Assert.DoesNotContain(graph.Nodes, node => node.Key == Cluster(true).Key);
        Assert.All(Members, member => Assert.Contains(graph.Nodes,
            node => node.Accounts.Count == 1 && node.Accounts[0] == member.ToChainAddress()));
        Assert.True(Assert.Single(graph.AccountClusters).IsOpen);
    }

    private static TraceGraph Build(bool isOpen, Func<ChainAddress, IReadOnlyList<TrailEndHint>>? ends = null)
    {
        List<EthereumTransfer> history =
        [
            Transfer(1, Scammer, Members[0]), Transfer(2, Members[0], Members[2]),
            Transfer(3, Scammer, Members[1]), Transfer(4, Members[1], Members[2]),
        ];
        var data = new GraphData(
            new GraphState(
                [TraceStartPoint.ForEthereumAddress(Scammer)], [], [],
                new Dictionary<string, IReadOnlyDictionary<GraphLayout, StoredPosition>>()),
            new Dictionary<Txid, GraphTransaction>(),
            new Dictionary<Txid, OutputSpendsAnswer>(),
            [])
        {
            Account = AccountData.Combine(
                new Dictionary<EthereumTxHash, EthereumTransaction>(), history, [], new HashSet<ChainAddress>(),
                new HashSet<ChainAddress>()),
        };
        return GraphBuilder.Build(data, new GraphKnowledge(address => [address], _ => [], _ => [])
        {
            AccountClusters = [Cluster(isOpen)],
            AccountEnds = ends ?? (_ => []),
        });
    }

    private static GraphAccountCluster Cluster(bool isOpen)
    {
        var members = Members.Select(member => member.ToChainAddress()).ToList();
        return new GraphAccountCluster(
            GraphAccountCluster.KeyOf(members[0]), Chain.Ethereum, members, members[2], isOpen,
            ["deposit-sweep v1: …"]);
    }

    private static EthereumAddress Address(int n) => EthereumAddress.Parse("0x" + new string((char)('0' + n), 40));

    private static EthereumTransfer Transfer(int n, EthereumAddress from, EthereumAddress to) => new(
        EthereumTxHash.Parse("0x" + n.ToString("x64")), EthereumTransferKind.Native, from, to,
        new AssetAmount(Asset.Ether, BigInteger.Pow(10, 18)), 0,
        new EthereumBlockReference(900 + n, "0x" + new string('a', 64), DateTimeOffset.UnixEpoch.AddHours(n)));
}
