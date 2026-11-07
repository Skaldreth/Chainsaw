using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.EthSynthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// The graph on Ethereum (stage 23b): start points, expansion through the Ethereum source, nodes as chain addresses,
/// edges per asset, swaps, and tracing per asset with the account methods.
/// </summary>
public sealed class EthereumGraphTests : IDisposable
{
    private static readonly EthereumAddress Victim = Address("victim");
    private static readonly EthereumAddress Scammer = Address("scammer");
    private static readonly EthereumAddress Mule = Address("mule");
    private static readonly EthereumAddress Exchange = Address("exchange");
    private static readonly EthereumAddress Pool = Address("pool");

    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    /// <summary>The victim pays USDT and ether to the scammer, who sends USDT on to a mule and swaps some.</summary>
    private void Chain()
    {
        _harness.Ethereum.Add(
            Tx("pay-usdt", 100, Victim, 0.001m, Token(Victim, Scammer, Usdt(1_000))),
            Tx("pay-eth", 101, Victim, 0.001m, Eth(Victim, Scammer, 2m)),
            Tx("to-mule", 110, Scammer, 0.001m, Token(Scammer, Mule, Usdt(600))),
            Tx("swap", 120, Scammer, 0.002m, Token(Scammer, Pool, Usdt(400)),
                (EthereumTransferKind.Internal, Pool, Scammer, Ether(0.1m))),
            Tx("cash-out", 130, Mule, 0.001m, Token(Mule, Exchange, Usdt(600))));
    }

    [Fact]
    public async Task An_ethereum_address_is_a_start_point_with_its_own_node()
    {
        Assert.True(_harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer)));

        var graph = await _harness.Load();

        var node = Assert.Single(graph.Nodes);
        Assert.True(node.IsStartPoint);
        Assert.Equal(SharedKernel.Chain.Ethereum, node.Chain);
        Assert.Equal([Scammer.ToChainAddress()], node.Addresses);
        Assert.Empty(node.GraphAddresses);
        Assert.Same(node, graph.NodeOf(Scammer.ToChainAddress()));
        Assert.Equal(NodeExpansion.NotExpanded, node.Expansion);
    }

    [Fact]
    public async Task Expanding_an_address_brings_in_its_three_histories_with_edges_per_asset()
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));

        var result = await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));

        Assert.Equal(ExpansionOutcome.Expanded, result.Outcome);
        Assert.Equal(4, result.NewEthereumTransactions.Count);
        var graph = result.Graph;
        var scammer = graph.NodeOf(Scammer.ToChainAddress())!;
        Assert.Equal(NodeExpansion.Expanded, scammer.Expansion);
        var usdtIn = Assert.Single(
            graph.Edges, edge => edge.To == scammer.Key && edge.Amount.Asset == KnownTokens.EthereumUsdt);
        Assert.Equal(Usdt(1_000), usdtIn.Amount);
        var ethIn = Assert.Single(graph.Edges, edge => edge.To == scammer.Key && edge.Amount.Asset == Asset.Ether
            && edge.From == graph.NodeOf(Victim.ToChainAddress())!.Key);
        Assert.Equal(Ether(2), ethIn.Amount);
        Assert.NotEqual(usdtIn.Key, ethIn.Key);
        Assert.All(graph.Edges.SelectMany(edge => edge.Transfers), transfer =>
        {
            Assert.NotNull(transfer.Block);
            Assert.StartsWith("0x", transfer.Block!.Hash.Value, StringComparison.Ordinal);
            Assert.Equal(SharedKernel.Chain.Ethereum, transfer.Transaction.Chain);
        });
        Assert.Contains(Scammer.ToChainAddress(), graph.Account.ReadAccounts);
    }

    [Fact]
    public async Task The_graph_is_read_again_from_the_case_without_asking_the_source()
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        var expanded = await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));
        var asked = _harness.Ethereum.Lookups;
        _harness.Ethereum.Online = false;

        var graph = await _harness.Load();

        Assert.Equal(asked, _harness.Ethereum.Lookups);
        Assert.Equal(expanded.Graph.Edges.Select(edge => edge.Key), graph.Edges.Select(edge => edge.Key));
        Assert.Empty(graph.Problems);
    }

    [Fact]
    public async Task An_expansion_holds_back_at_the_limit_and_says_so()
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));

        var result = await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumAddress(Scammer, 2));

        Assert.Equal(ExpansionOutcome.Partly, result.Outcome);
        Assert.Equal(2, result.NewEthereumTransactions.Count);
        Assert.Contains(Hash("swap"), result.NewEthereumTransactions);
        Assert.NotNull(result.HeldBack);
        Assert.Contains("hölls tillbaka", result.Problem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Offline_an_expansion_says_so_and_changes_nothing()
    {
        Chain();
        _harness.Ethereum.Online = false;

        var result = await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));

        Assert.Equal(ExpansionOutcome.NotExpanded, result.Outcome);
        Assert.Contains("Offline", result.Problem, StringComparison.Ordinal);
        Assert.Empty(result.Graph.Nodes);
    }

    [Fact]
    public async Task A_start_transaction_starts_at_its_signer()
    {
        Chain();
        var start = TraceStartPoint.ForEthereumTransaction(Hash("pay-usdt"));
        _harness.Service.AddStartPoint(_harness.Case, start);
        var before = await _harness.Load();
        Assert.Contains(before.Problems, problem => problem.Contains(Hash("pay-usdt").Value, StringComparison.Ordinal));

        var result = await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumTransaction(Hash("pay-usdt")));

        var victim = result.Graph.NodeOf(Victim.ToChainAddress())!;
        Assert.True(victim.IsStartPoint);
        Assert.Equal(0, victim.Hop);
        Assert.Equal(1, result.Graph.NodeOf(Scammer.ToChainAddress())!.Hop);
        Assert.Contains(Hash("pay-usdt"), result.Graph.Account.Transactions.Keys);
    }

    [Fact]
    public async Task A_swap_shows_only_where_the_signer_sent_one_asset_and_got_another()
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));

        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumAddress(Scammer))).Graph;

        var swap = Assert.Single(graph.Account.Swaps);
        Assert.Equal(Hash("swap"), swap.Transaction);
        Assert.Equal(Scammer.ToChainAddress(), swap.Account);
        Assert.Equal(Usdt(400), swap.Sold);
        Assert.Equal(Ether(0.1m), swap.Bought);
    }

    [Theory]
    [InlineData(TracingMethodKind.Fifo)]
    [InlineData(TracingMethodKind.Haircut)]
    public async Task Tracing_per_asset_follows_the_money_and_every_unit_ends_somewhere(TracingMethodKind method)
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));
        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumAddress(Mule))).Graph;
        var seeds = graph.Account.Transfers.Where(transfer => transfer.To == Scammer && transfer.From == Victim)
            .Select(AccountSeed.Of).ToList();

        var result = await _harness.Service.TraceAccountsAsync(graph, new AccountTraceRequest(method, seeds));

        Assert.Equal(2, result.Assets.Count);
        foreach (var asset in result.Assets)
        {
            var ended = asset.Ends.Aggregate(AssetAmount.Zero(asset.Traced.Asset), (sum, end) => sum + end.Amount.Amount);
            Assert.Equal(asset.Traced, ended);
        }

        var usdt = result.Assets.Single(asset => asset.Traced.Asset == KnownTokens.EthereumUsdt);
        var exchange = graph.NodeOf(Exchange.ToChainAddress())!;
        var end = Assert.Single(usdt.Ends, end => end.NodeKey == exchange.Key);
        Assert.Equal(TrailEndReason.NotExpanded, end.Reason);
        Assert.Equal(Usdt(600), end.Amount.Amount);
        Assert.Contains("räknat genom", end.Amount.Explain(), StringComparison.Ordinal);
        Assert.Equal(method, end.Amount.Method.Kind);
        var eth = result.Assets.Single(asset => asset.Traced.Asset == Asset.Ether);
        Assert.Contains(eth.Ends, end => end.Reason == TrailEndReason.MinerFee || end.Reason == TrailEndReason.Unspent);
    }

    [Fact]
    public async Task An_exchange_label_ends_the_trail_there()
    {
        Chain();
        _harness.Labels.Accounts[Mule.ToChainAddress()] = TrailEndReason.Exchange;
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));
        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumAddress(Mule))).Graph;
        var seeds = graph.Account.Transfers.Where(transfer => transfer.To == Scammer
            && transfer.Amount.Asset == KnownTokens.EthereumUsdt).Select(AccountSeed.Of).ToList();

        var result = await _harness.Service.TraceAccountsAsync(
            graph, new AccountTraceRequest(TracingMethodKind.Fifo, seeds));

        var mule = graph.NodeOf(Mule.ToChainAddress())!;
        Assert.Contains(mule.Ends, hint => hint.Reason == TrailEndReason.Exchange);
        var end = Assert.Single(result.Ends, end => end.NodeKey == mule.Key);
        Assert.Equal(TrailEndReason.Exchange, end.Reason);
        Assert.Equal(Usdt(600), end.Amount.Amount);
    }

    [Fact]
    public async Task Seeds_in_each_others_flow_are_refused_rather_than_counted_twice()
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForEthereumAddress(Scammer))).Graph;
        var seeds = graph.Account.Transfers.Where(transfer => transfer.Amount.Asset == KnownTokens.EthereumUsdt
            && (transfer.To == Scammer || transfer.To == Mule)).Select(AccountSeed.Of).ToList();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.Service.TraceAccountsAsync(
            graph, new AccountTraceRequest(TracingMethodKind.Fifo, seeds)));
    }

    [Fact]
    public async Task A_seed_that_is_not_in_the_graph_is_refused()
    {
        var graph = await _harness.Load();

        await Assert.ThrowsAsync<ArgumentException>(() => _harness.Service.TraceAccountsAsync(graph,
            new AccountTraceRequest(TracingMethodKind.Fifo,
                [new AccountSeed(Hash("nowhere"), EthereumTransferKind.Native, 0)])));
    }

    [Fact]
    public async Task Ethereum_start_points_and_expansions_survive_reopening()
    {
        Chain();
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumAddress(Scammer));
        _harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForEthereumTransaction(Hash("pay-usdt")));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumAddress(Scammer));
        await _harness.Service.ExpandAsync(_harness.Case, ExpansionRequest.ForEthereumTransaction(Hash("pay-usdt")));

        _harness.Reopen();
        var graph = await _harness.Load();

        Assert.Equal(2, graph.StartPoints.Count);
        Assert.Contains(graph.StartPoints, start => start.EthereumAddress == Scammer);
        Assert.Contains(graph.Expansions, expansion => expansion.EthereumTransaction == Hash("pay-usdt"));
        Assert.Equal(NodeExpansion.Expanded, graph.NodeOf(Scammer.ToChainAddress())!.Expansion);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task The_account_methods_are_offered_for_ethereum()
    {
        Assert.Equal([TracingMethodKind.Fifo, TracingMethodKind.Haircut],
            _harness.Service.AccountMethods.Select(id => id.Kind));
        Assert.Equal(
            _harness.Service.Methods.Select(id => id.Kind), _harness.Service.AccountMethods.Select(id => id.Kind));
        await Task.CompletedTask;
    }
}
