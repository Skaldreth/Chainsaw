using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>
/// The watches in the case: sealed records with who and when, the start, hits sealed once with their block, the gate
/// that keeps every lookup back, and lookups only in step with the chain.
/// </summary>
public sealed class MonitoringServiceTests : IDisposable
{
    private static readonly BitcoinAddress Node = Synthetic.Wpkh("node");
    private static readonly BitcoinAddress Exchange = Synthetic.Wpkh("exchange");

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    [Fact]
    public void A_watch_is_sealed_with_who_and_when_and_a_line_in_the_journal()
    {
        var watch = _harness.WatchBitcoin(Node, WatchDirection.Out, receivers: [Btc(Exchange)]);

        Assert.Equal(WatchState.Active, watch.State);
        Assert.Null(watch.StartHeight);
        Assert.Equal("Testa Testsson", watch.CreatedBy.Name);
        Assert.Equal(watch.Id, _harness.Service.GetWatches(_harness.Case).Single().Id);
        Assert.Contains("ut · till 1 mottagare", _harness.Journal(MonitoringJournalKinds.Added).Single().Summary,
            StringComparison.Ordinal);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void Change_pause_resume_and_remove_are_new_records_and_the_old_stay()
    {
        var watch = _harness.WatchBitcoin(Node);
        var service = _harness.Service;

        service.Change(_harness.Case, watch.Id, watch.Filter with { Direction = WatchDirection.In });
        Assert.Equal(WatchState.Paused, service.SetPaused(_harness.Case, watch.Id, true)!.State);
        Assert.Equal(WatchState.Active, service.SetPaused(_harness.Case, watch.Id, false)!.State);
        Assert.True(service.Remove(_harness.Case, watch.Id, "  Fel adress  "));

        Assert.Empty(service.GetWatches(_harness.Case));
        var records = service.GetRecords(_harness.Case);
        Assert.Equal(
            [WatchAction.Added, WatchAction.Changed, WatchAction.Paused, WatchAction.Resumed, WatchAction.Removed],
            records.Select(record => record.Action));
        Assert.Equal(WatchDirection.In, records[^1].Watch.Filter.Direction);
        Assert.Equal("Fel adress", records[^1].Reason);
        Assert.False(service.Remove(_harness.Case, watch.Id, null));
        Assert.Null(service.SetPaused(_harness.Case, watch.Id, true));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public void A_wrong_filter_or_node_is_refused()
    {
        var subject = new WatchSubject(WatchSubjectKind.Address, Btc(Node), "Nod");
        var service = _harness.Service;

        Assert.Throws<ArgumentException>(() => service.Add(_harness.Case, subject,
            new WatchFilter(Chain.Bitcoin, WatchDirection.In, [Btc(Exchange)], [], null, null)));
        Assert.Throws<ArgumentException>(() => service.Add(_harness.Case, subject,
            new WatchFilter(Chain.Bitcoin, WatchDirection.Out, [new ChainAddress(Chain.Bitcoin, "nope")], [], null,
                null)));
        Assert.Throws<ArgumentException>(() => service.Add(_harness.Case, subject,
            new WatchFilter(Chain.Bitcoin, WatchDirection.Out, [], [], Synthetic.Usdt, null)));
        Assert.Throws<ArgumentException>(() => service.Add(_harness.Case, subject with { Name = " " },
            new WatchFilter(Chain.Bitcoin, WatchDirection.Out, [], [], null, null)));
        Assert.Throws<ArgumentException>(() => service.Add(_harness.Case,
            new WatchSubject(WatchSubjectKind.Cluster, Synthetic.On(Synthetic.Eth("e")), "Kluster"),
            new WatchFilter(Chain.Ethereum, WatchDirection.Out, [], [], null, null)));
        Assert.Throws<ArgumentException>(() => service.Add(_harness.Case,
            new WatchSubject(WatchSubjectKind.Address, Synthetic.On(Synthetic.Eth("e")), "E"),
            new WatchFilter(Chain.Ethereum, WatchDirection.Out, [], [], null, Synthetic.Tether(5))));
        Assert.Empty(service.GetWatches(_harness.Case));
    }

    [Fact]
    public async Task The_first_check_sets_the_start_and_only_later_money_is_a_hit()
    {
        var watch = _harness.WatchBitcoin(Node);
        var before = Synthetic.Tx("before", 900_000).In(Node, 10_000).Out(Exchange, 9_000).Build();
        _harness.Bitcoin.Holds(Node, before);

        var first = await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_000);

        Assert.Empty(first.Outcomes);
        Assert.Equal(900_000, _harness.Service.GetWatches(_harness.Case).Single().StartHeight);
        Assert.Empty(_harness.Bitcoin.Asked);

        var after = Synthetic.Tx("after", 900_002).In(Node, 20_000).Out(Exchange, 19_000).Build();
        _harness.Bitcoin.Holds(Node, after, before);
        var second = await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_002);

        var hit = Assert.Single(second.Hits);
        Assert.Equal(watch.Id, hit.WatchId);
        Assert.Equal(after.Txid.Value, hit.Match.Transaction);
        Assert.Equal(after.Block!.Hash.Value, hit.Match.Block.Hash);
        Assert.Equal([7L], hit.Origins);
        Assert.Equal(LookupMode.InStep, Assert.Single(_harness.Bitcoin.Asked).Mode);
        Assert.Single(_harness.Journal(MonitoringJournalKinds.Hit));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task A_hit_is_sealed_once_however_often_it_is_seen()
    {
        _harness.WatchBitcoin(Node);
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_000);
        _harness.Bitcoin.Holds(Node, Synthetic.Tx("hit", 900_001).In(Node, 20_000).Out(Exchange, 19_000).Build());

        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_001);
        var again = await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_002);
        _harness.Reopen();

        Assert.Empty(again.Hits);
        Assert.Single(_harness.Service.GetHits(_harness.Case));
        Assert.Single(_harness.Journal(MonitoringJournalKinds.Hit));
        var checkedAt = _harness.Service.GetChecked(_harness.Case).Values.Single();
        Assert.Equal(900_002, checkedAt.Height);
    }

    [Theory]
    [InlineData(WatchGate.Locked)]
    [InlineData(WatchGate.NoExit)]
    [InlineData(WatchGate.ChainOff)]
    public async Task Nothing_is_asked_when_the_gate_is_closed(WatchGate closed)
    {
        _harness.WatchBitcoin(Node);
        _harness.Gate.State = closed;

        var height = await _harness.Service.GetHeightAsync(_harness.Case, Chain.Bitcoin);
        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_000);

        Assert.Null(height);
        Assert.Equal(closed, run.Gate);
        Assert.Equal(0, _harness.Bitcoin.TipAsked);
        Assert.Empty(_harness.Bitcoin.Asked);
        Assert.Null(_harness.Service.GetWatches(_harness.Case).Single().StartHeight);
    }

    [Fact]
    public async Task A_paused_watch_is_not_checked()
    {
        var watch = _harness.WatchBitcoin(Node);
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_000);
        _harness.Service.SetPaused(_harness.Case, watch.Id, true);

        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_005);

        Assert.Empty(run.Outcomes);
        Assert.Empty(_harness.Bitcoin.Asked);
    }

    [Fact]
    public async Task A_failed_lookup_keeps_the_height_so_the_next_check_reads_back_to_it()
    {
        _harness.WatchBitcoin(Node);
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_000);
        _harness.Bitcoin.Status = LookupStatus.Failed;

        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_020);

        Assert.Equal(WatchFeedStatus.Failed, run.Outcomes.Single().Status);
        var checkedAt = _harness.Service.GetChecked(_harness.Case).Values.Single();
        Assert.Equal(900_000, checkedAt.Height);
        Assert.Equal(WatchFeedStatus.Failed, checkedAt.Status);
        Assert.NotNull(checkedAt.Problem);
    }

    [Fact]
    public async Task A_cluster_is_read_as_the_clustering_puts_it_at_each_check()
    {
        var spender = Synthetic.Wpkh("together");
        var merge = Synthetic.Tx("merge", 899_000).In(Node, 5_000).In(spender, 5_000).Out(Exchange, 9_000).Build();
        _harness.Clustering.Analyse(_harness.Case, [merge]);
        _harness.Service.Add(
            _harness.Case,
            new WatchSubject(WatchSubjectKind.Cluster, Btc(Node), "Kluster 7"),
            new WatchFilter(Chain.Bitcoin, WatchDirection.Both, [], [], null, null));
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_000);

        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_001);

        Assert.Equal([Node.Value, spender.Value], _harness.Bitcoin.Asked.Select(asked => asked.Address).Order()
            .ToList().OrderBy(value => value == spender.Value));
    }

    [Fact]
    public async Task On_Ethereum_the_watched_token_is_read_in_step_and_a_missing_block_hash_is_looked_up()
    {
        var node = Synthetic.Eth("eth-node");
        var sender = Synthetic.Eth("sender");
        _harness.Service.Add(
            _harness.Case,
            new WatchSubject(WatchSubjectKind.Address, Synthetic.On(node), "0x…node"),
            new WatchFilter(Chain.Ethereum, WatchDirection.In, [], [Synthetic.On(sender)], Synthetic.Usdt,
                Synthetic.Tether(1_000)));
        await _harness.Service.CheckAsync(_harness.Case, Chain.Ethereum, 21_000_000);
        _harness.Ethereum.Holds(node, EthereumHistory.Tokens,
            Synthetic.Transfer("in", EthereumTransferKind.Token, sender, node, Synthetic.Tether(2_500), 21_000_040,
                withHash: false));
        _harness.Ethereum.BlockHashes[21_000_040] = Synthetic.EthBlockHash(21_000_040);

        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Ethereum, 21_000_050);

        var hit = Assert.Single(run.Hits);
        Assert.Equal(Synthetic.Tether(2_500), hit.Match.Amount);
        Assert.Equal(Synthetic.EthBlockHash(21_000_040), hit.Match.Block.Hash);
        Assert.Equal([EthereumHistory.Tokens], _harness.Ethereum.Asked.Select(asked => asked.History));
        Assert.All(_harness.Ethereum.Asked, asked => Assert.Equal(LookupMode.InStep, asked.Mode));
        Assert.Equal(LookupMode.InStep, Assert.Single(_harness.Ethereum.BlocksAsked).Mode);
        _harness.Reopen();
        Assert.Equal(Synthetic.Tether(2_500), _harness.Service.GetHits(_harness.Case).Single().Match.Amount);
    }

    [Fact]
    public async Task A_hit_without_a_block_hash_waits_for_a_check_that_has_it()
    {
        var node = Synthetic.Eth("eth-node");
        _harness.Service.Add(
            _harness.Case,
            new WatchSubject(WatchSubjectKind.Address, Synthetic.On(node), "0x…node"),
            new WatchFilter(Chain.Ethereum, WatchDirection.Out, [], [], null, null));
        await _harness.Service.CheckAsync(_harness.Case, Chain.Ethereum, 21_000_000);
        _harness.Ethereum.Holds(node, EthereumHistory.Internal,
            Synthetic.Transfer("int", EthereumTransferKind.Internal, node, Synthetic.Eth("x"), Synthetic.Ether(1),
                21_000_010, withHash: false));

        var first = await _harness.Service.CheckAsync(_harness.Case, Chain.Ethereum, 21_000_011);
        _harness.Ethereum.BlockHashes[21_000_010] = Synthetic.EthBlockHash(21_000_010);
        var second = await _harness.Service.CheckAsync(_harness.Case, Chain.Ethereum, 21_000_012);

        Assert.Empty(first.Hits);
        Assert.Contains("21000010", first.Outcomes.Single().Problems.Single(), StringComparison.Ordinal);
        Assert.Single(second.Hits);
    }

    [Fact]
    public void Seen_hits_are_noted_and_never_move_back()
    {
        _harness.Service.MarkSeen(_harness.Case, "w", 5);
        _harness.Service.MarkSeen(_harness.Case, "w", 3);

        Assert.Equal(5, _harness.Service.GetSeen(_harness.Case)["w"]);
    }

    [Fact]
    public async Task The_height_is_asked_only_when_fetched_now()
    {
        Assert.Equal(900_000, await _harness.Service.GetHeightAsync(_harness.Case, Chain.Bitcoin));
        _harness.Bitcoin.Tip = null;
        Assert.Null(await _harness.Service.GetHeightAsync(_harness.Case, Chain.Bitcoin));
        Assert.Null(await _harness.Service.GetHeightAsync(_harness.Case, Chain.Tron));
    }

    [Fact]
    public void Without_an_operator_profile_nothing_is_sealed()
    {
        _harness.Profiles.Profile = null;

        var refused = Assert.Throws<CaseException>(() => _harness.WatchBitcoin(Node));

        Assert.Equal(CaseError.NoOperatorProfile, refused.Error);
        Assert.Empty(_harness.Service.GetWatches(_harness.Case));
    }

    private static ChainAddress Btc(BitcoinAddress address) => new(Chain.Bitcoin, address.Value);
}
