using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Tracing.Tests.Synthetic;

namespace Chainsaw.Modules.Tracing.Tests;

/// <summary>
/// Litecoin in the graph's UTXO part: a start point is expanded through Litecoin's own source, its transactions and
/// coins carry their chain, amounts are in LTC, the clustering merges common inputs, the state is read back on its
/// chain, and MWEB ends the trail. Against a synthetic chain; Bitcoin's source is never asked.
/// </summary>
public sealed class LitecoinGraphTests : IDisposable
{
    private readonly CaseHarness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task A_litecoin_address_is_expanded_through_litecoins_source_and_counts_in_ltc()
    {
        var (payment, onward) = Story();

        var result = await _harness.Expand(LitecoinPkh("victim-payee"));

        Assert.Equal(ExpansionOutcome.Expanded, result.Outcome);
        Assert.Equal(0, _harness.Source.Lookups);
        Assert.Contains(payment.Txid, result.Graph.Transactions.Keys);
        Assert.All(result.Graph.Transactions.Keys, txid => Assert.Equal(Chain.Litecoin, txid.Chain));
        var payee = result.Graph.Nodes.Single(node => node.GraphAddresses.Contains(LitecoinPkh("victim-payee")));
        Assert.Equal(Chain.Litecoin, payee.Chain);
        Assert.All(
            result.Graph.Edges.SelectMany(edge => edge.Transfers),
            transfer => Assert.Equal(Asset.Litecoin, transfer.Amount.Asset));
        Assert.DoesNotContain(onward.Txid.On(Chain.Bitcoin), result.Graph.Transactions.Keys);
    }

    [Fact]
    public async Task Fifo_traces_litecoin_coins_in_ltc()
    {
        var (payment, onward) = Story();
        await _harness.Expand(LitecoinPkh("victim-payee"));
        var graph = (await _harness.Service.ExpandAsync(
            _harness.Case, ExpansionRequest.ForTransaction(onward.Txid))).Graph;

        var result = _harness.Service.Trace(
            graph, new TraceRequest(TracingMethodKind.Fifo, [new TraceSeed(new Outpoint(payment.Txid, 0))]));

        Assert.Equal(new AssetAmount(Asset.Litecoin, 1_000_000), result.Traced);
        Assert.Equal(new AssetAmount(Asset.Litecoin, 1_000), result.EndedBy(TrailEndReason.MinerFee));
        Assert.All(result.Coins, coin => Assert.Equal(Chain.Litecoin, coin.Coin.Txid.Chain));
    }

    [Fact]
    public async Task A_trace_over_two_chains_is_refused()
    {
        var (payment, _) = Story();
        var bitcoin = Tx("bitcoin-payment").In(Wpkh("btc-payer"), 50_000).Out(Wpkh("btc-payee"), 49_000).Build();
        _harness.Source.Add(bitcoin);
        await _harness.Expand(LitecoinPkh("victim-payee"));
        var graph = (await _harness.Expand(Wpkh("btc-payee"))).Graph;

        Assert.Throws<ArgumentException>(() => _harness.Service.Trace(graph, new TraceRequest(
            TracingMethodKind.Fifo,
            [new TraceSeed(new Outpoint(payment.Txid, 0)), new TraceSeed(new Outpoint(bitcoin.Txid, 0))])));
    }

    [Fact]
    public async Task Common_inputs_on_litecoin_are_merged_into_one_wallet()
    {
        var spend = Tx(Chain.Litecoin, "ltc-consolidation")
            .In(LitecoinPkh("wallet-a"), 300_000).In(LitecoinPkh("wallet-b"), 200_000)
            .Out(LitecoinPkh("shop"), 499_000).Build();
        _harness.Litecoin.Add(spend);

        var result = await _harness.Expand(LitecoinPkh("wallet-a"));

        var wallet = Assert.Single(result.Graph.Nodes, node => node.Kind == GraphNodeKind.Cluster);
        Assert.Equal(Chain.Litecoin, wallet.Chain);
        Assert.Contains(LitecoinPkh("wallet-b"), wallet.GraphAddresses);
    }

    [Fact]
    public async Task Equal_outputs_on_litecoin_are_only_proposed_since_coinjoins_are_known_on_bitcoin_alone()
    {
        var join = Tx(Chain.Litecoin, "ltc-join")
            .In(LitecoinPkh("stranger-a"), 300_000).In(LitecoinPkh("stranger-b"), 300_000)
            .Out(LitecoinPkh("mix-a"), 299_000).Out(LitecoinPkh("mix-b"), 299_000).Build();
        _harness.Litecoin.Add(join);

        var result = await _harness.Expand(LitecoinPkh("stranger-a"));

        Assert.DoesNotContain(result.Graph.Nodes, node => node.Kind == GraphNodeKind.Cluster);
    }

    [Fact]
    public async Task A_litecoin_start_point_and_its_expansion_are_read_back_on_their_chain()
    {
        var (payment, _) = Story();
        var start = TraceStartPoint.ForAddress(LitecoinPkh("victim-payee"));
        Assert.True(_harness.Service.AddStartPoint(_harness.Case, start));
        Assert.True(_harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForTransaction(payment.Txid)));
        await _harness.Expand(LitecoinPkh("victim-payee"));

        _harness.Reopen();
        var graph = await _harness.Load();

        Assert.Contains(graph.Nodes, node => node.IsStartPoint && node.Chain == Chain.Litecoin);
        Assert.Contains(payment.Txid, graph.Transactions.Keys);
        Assert.False(_harness.Service.AddStartPoint(_harness.Case, TraceStartPoint.ForTransaction(payment.Txid)));
        Assert.True(_harness.Service.AddStartPoint(
            _harness.Case, TraceStartPoint.ForTransaction(payment.Txid.On(Chain.Bitcoin))));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task A_litecoin_coin_links_to_another_chain_and_is_read_back()
    {
        var (payment, _) = Story();
        await _harness.Expand(LitecoinPkh("victim-payee"));
        var to = new ChainTransactionRef(Chain.Bitcoin, Txid("on-bitcoin").Value);

        var link = _harness.Service.LinkCrossChain(_harness.Case, new Outpoint(payment.Txid, 0), to, "brygga");
        _harness.Reopen();

        var history = Assert.Single(_harness.Service.GetCrossChainHistory(_harness.Case));
        Assert.Equal(link.From, history.Link.From);
        Assert.Equal(Chain.Litecoin, history.Link.FromChain);
        Assert.Throws<ArgumentException>(() => _harness.Service.LinkCrossChain(
            _harness.Case,
            new Outpoint(payment.Txid, 1),
            new ChainTransactionRef(Chain.Litecoin, Txid("same-chain").Value),
            "samma kedja"));
    }

    [Fact]
    public async Task A_peg_in_to_mweb_ends_the_trail_on_litecoin()
    {
        var pegIn = BitcoinAddress.FromWitness(9, Enumerable.Repeat((byte)0x66, 32).ToArray(), BitcoinNetwork.Litecoin);
        var hide = Tx(Chain.Litecoin, "ltc-peg-in")
            .In(LitecoinPkh("hider"), 500_000).Out(pegIn, 499_000).Build();
        _harness.Litecoin.Add(hide);

        var result = await _harness.Expand(LitecoinPkh("hider"));

        var pool = result.Graph.Nodes.Single(node => node.GraphAddresses.Contains(pegIn));
        var end = Assert.Single(pool.Ends);
        Assert.Equal(TrailEndReason.PrivacyCoin, end.Reason);
        Assert.Equal(MwebTrailEnds.Source, end.Source);
    }

    /// <summary>
    /// A victim's payment on Litecoin and its onward spend: 0.01 LTC to the payee, who sends 0.00999 on with a fee of
    /// 0.00001.
    /// </summary>
    /// <returns>The payment and the onward spend.</returns>
    private (BitcoinTransaction Payment, BitcoinTransaction Onward) Story()
    {
        var payment = Tx(Chain.Litecoin, "ltc-payment", 2_900_000)
            .In(LitecoinPkh("victim"), 1_500_000).Out(LitecoinPkh("victim-payee"), 1_000_000)
            .Out(LitecoinPkh("victim-change"), 499_000).Build();
        var onward = Tx(Chain.Litecoin, "ltc-onward", 2_900_010)
            .Spend(payment, 0).Out(LitecoinPkh("next-hop"), 999_000).Build();
        _harness.Litecoin.Add(payment, onward);
        return (payment, onward);
    }
}
