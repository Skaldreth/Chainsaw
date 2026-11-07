using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>
/// The facit for what a watch picks out of a transaction: money out without the node's own change, money in that is
/// not change, receivers 1:n, senders n:1, the asset and the minimum. Synthetic transactions only.
/// </summary>
public sealed class WatchMatchTests
{
    private static readonly BitcoinAddress Node = Synthetic.Wpkh("node");
    private static readonly BitcoinAddress Change = Synthetic.Wpkh("node-change");
    private static readonly BitcoinAddress Exchange = Synthetic.Wpkh("exchange");
    private static readonly BitcoinAddress Other = Synthetic.Wpkh("other");
    private static readonly BitcoinAddress Victim = Synthetic.Wpkh("victim");

    private readonly WatchCheck _check = new();

    private static ChainAddress On(BitcoinAddress address) => new(Chain.Bitcoin, address.Value);

    private static WatchFilter Filter(
        WatchDirection direction, ChainAddress[]? receivers = null, ChainAddress[]? senders = null,
        long? minimum = null) =>
        new(Chain.Bitcoin, direction, receivers ?? [], senders ?? [], null,
            minimum is { } sat ? AssetAmount.Of(new Satoshi(sat)) : null);

    private IReadOnlyList<WatchMatch> Match(
        WatchFilter filter, BitcoinTransaction transaction, params BitcoinAddress[] node)
    {
        var watched = (node.Length == 0 ? [Node] : node).Select(On).ToHashSet();
        return _check.Match(watched, filter, BitcoinCaseFeed.Of(transaction));
    }

    [Fact]
    public void Money_out_counts_what_leaves_the_node_and_not_its_change()
    {
        var tx = Synthetic.Tx("out").In(Node, 100_000).Out(Exchange, 60_000).Out(Other, 30_000)
            .Out(Node, 9_000).Build();

        var match = Assert.Single(Match(Filter(WatchDirection.Out), tx));

        Assert.Equal(WatchDirection.Out, match.Direction);
        Assert.Equal(90_000, (long)match.Amount.Units);
        Assert.Equal([On(Exchange), On(Other)], match.Counterparties.OrderBy(p => p.Value == On(Other).Value));
        Assert.Equal(900_010, match.Block.Height);
        Assert.Equal(Synthetic.Block(900_010).Hash.Value, match.Block.Hash);
    }

    [Fact]
    public void Change_to_a_cluster_address_is_not_money_out()
    {
        var tx = Synthetic.Tx("consolidate").In(Node, 100_000).Out(Change, 99_000).Build();

        Assert.Empty(Match(Filter(WatchDirection.Both), tx, Node, Change));
    }

    [Fact]
    public void Receivers_one_to_many_count_only_what_goes_to_them()
    {
        var tx = Synthetic.Tx("to-exchange").In(Node, 100_000).Out(Exchange, 60_000).Out(Other, 30_000).Build();
        var filter = Filter(WatchDirection.Out, receivers: [On(Exchange), On(Victim)]);

        var match = Assert.Single(Match(filter, tx));

        Assert.Equal(60_000, (long)match.Amount.Units);
        Assert.Equal([On(Exchange)], match.Counterparties);
    }

    [Fact]
    public void Money_out_to_no_chosen_receiver_is_no_hit()
    {
        var tx = Synthetic.Tx("elsewhere").In(Node, 100_000).Out(Other, 90_000).Build();

        Assert.Empty(Match(Filter(WatchDirection.Out, receivers: [On(Exchange)]), tx));
    }

    [Fact]
    public void Money_in_counts_what_the_node_gets_with_its_senders()
    {
        var tx = Synthetic.Tx("in").In(Victim, 50_000).In(Other, 50_000).Out(Node, 70_000).Out(Victim, 29_000)
            .Build();

        var match = Assert.Single(Match(Filter(WatchDirection.In), tx));

        Assert.Equal(WatchDirection.In, match.Direction);
        Assert.Equal(70_000, (long)match.Amount.Units);
        Assert.Equal(2, match.Counterparties.Count);
    }

    [Fact]
    public void Senders_many_to_one_need_one_of_them_among_the_inputs()
    {
        var fromVictim = Synthetic.Tx("from-victim").In(Victim, 50_000).Out(Node, 49_000).Build();
        var fromOther = Synthetic.Tx("from-other").In(Other, 50_000).Out(Node, 49_000).Build();
        var filter = Filter(WatchDirection.In, senders: [On(Victim), On(Exchange)]);

        Assert.Single(Match(filter, fromVictim));
        Assert.Empty(Match(filter, fromOther));
    }

    [Fact]
    public void The_direction_decides_what_is_looked_for()
    {
        var outgoing = Synthetic.Tx("o").In(Node, 10_000).Out(Other, 9_000).Build();
        var incoming = Synthetic.Tx("i").In(Other, 10_000).Out(Node, 9_000).Build();

        Assert.Empty(Match(Filter(WatchDirection.In), outgoing));
        Assert.Empty(Match(Filter(WatchDirection.Out), incoming));
        Assert.Single(Match(Filter(WatchDirection.Both), outgoing));
        Assert.Single(Match(Filter(WatchDirection.Both), incoming));
    }

    [Fact]
    public void The_minimum_is_tested_on_the_sum()
    {
        var tx = Synthetic.Tx("split").In(Node, 100_000).Out(Exchange, 30_000).Out(Other, 30_000).Build();

        Assert.Single(Match(Filter(WatchDirection.Out, minimum: 60_000), tx));
        Assert.Empty(Match(Filter(WatchDirection.Out, minimum: 60_001), tx));
    }

    [Fact]
    public void An_unconfirmed_transaction_is_no_hit_yet()
    {
        var tx = Synthetic.Tx("mempool", height: null).In(Node, 10_000).Out(Other, 9_000).Build();

        Assert.Empty(Match(Filter(WatchDirection.Both), tx));
    }

    [Fact]
    public void On_Ethereum_each_transfer_counts_and_the_asset_filters()
    {
        var node = Synthetic.Eth("eth-node");
        var dex = Synthetic.Eth("dex");
        var tx = new WatchTransaction(Chain.Ethereum, Synthetic.EthTx("swap").Value,
            new WatchBlock(21_000_050, Synthetic.EthBlockHash(21_000_050), DateTimeOffset.UnixEpoch),
            [
                Flow(node, dex, Synthetic.Tether(2_500)),
                Flow(dex, node, Synthetic.Ether(1.5m)),
            ]);
        var all = new WatchFilter(Chain.Ethereum, WatchDirection.Both, [], [], null, null);
        var onlyTether = all with { Asset = Synthetic.Usdt, Minimum = Synthetic.Tether(1_000) };
        var watched = new HashSet<ChainAddress> { Synthetic.On(node) };

        var both = _check.Match(watched, all, tx);
        var tether = Assert.Single(_check.Match(watched, onlyTether, tx));

        Assert.Equal(2, both.Count);
        Assert.Equal(WatchDirection.Out, tether.Direction);
        Assert.Equal(Synthetic.Tether(2_500), tether.Amount);
        Assert.Empty(_check.Match(watched, onlyTether with { Minimum = Synthetic.Tether(3_000) }, tx));
    }

    private static WatchFlow Flow(EthereumAddress from, EthereumAddress to, AssetAmount amount) =>
        new([new WatchLeg(Synthetic.On(from), amount)], [new WatchLeg(Synthetic.On(to), amount)]);
}
