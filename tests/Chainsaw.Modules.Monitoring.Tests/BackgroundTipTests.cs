using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>
/// The background's hits are tips, not facts, until Chainsaw's own lookup has checked them (decided 2026-11-08):
/// whoever can write the queue can also write the responses an entry carries.
/// </summary>
public sealed class BackgroundTipTests : IDisposable
{
    private static readonly DateTimeOffset Seen = new(2026, 11, 7, 4, 0, 0, TimeSpan.Zero);

    private static readonly BitcoinAddress Node = Synthetic.Wpkh("w");

    private static readonly BitcoinAddress Dest = Synthetic.Wpkh("dest");

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    private IBackgroundWatches Background => _harness.Get<IBackgroundWatches>();

    private IWatchQueue Queue => _harness.Get<IWatchQueue>();

    [Fact]
    public async Task A_forged_tip_whose_transaction_is_not_on_the_chain_is_never_a_hit_and_is_closed_as_such()
    {
        var watch = await StartedWatch();
        Queue.Add(Entry(watch, "forged"));
        Background.Import(_harness.Case);

        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_004);

        Assert.Empty(run.Hits);
        Assert.Empty(_harness.Service.GetHits(_harness.Case));
        Assert.Equal(WatchTipState.Rejected, Assert.Single(_harness.Service.GetTips(_harness.Case)).State);
        var line = Assert.Single(_harness.Journal(MonitoringJournalKinds.TipRejected));
        Assert.Contains("stämde inte med kedjan", line.Summary, StringComparison.Ordinal);
        Assert.Empty(_harness.Journal(MonitoringJournalKinds.Hit));
        Assert.Equal(
            [(Synthetic.Txid("forged").Value, LookupMode.InStep)], _harness.Bitcoin.TransactionsAsked);
        Assert.True(_harness.Verify().IsIntact);

        // A closed tip is never looked up again.
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_004);
        Assert.Single(_harness.Bitcoin.TransactionsAsked);
    }

    [Fact]
    public async Task A_tip_whose_transaction_does_not_give_the_hit_is_not_sealed()
    {
        var watch = await StartedWatch();
        // The transaction exists, but the watched address is not in it.
        _harness.Bitcoin.Holds(
            Synthetic.Wpkh("other"),
            Synthetic.Tx("hit", 900_003).In(Synthetic.Wpkh("other"), 30_000).Out(Dest, 25_000).Build());
        Queue.Add(Entry(watch, "hit"));
        Background.Import(_harness.Case);

        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_004);

        Assert.Empty(_harness.Service.GetHits(_harness.Case));
        Assert.Equal(WatchTipState.Rejected, Assert.Single(_harness.Service.GetTips(_harness.Case)).State);
        Assert.Single(_harness.Journal(MonitoringJournalKinds.TipRejected));
    }

    [Fact]
    public async Task A_genuine_tip_is_sealed_from_Chainsaw_s_own_answer_with_the_tip_as_where_it_came_from()
    {
        var watch = await StartedWatch();
        var genuine = Genuine();
        Queue.Add(Entry(watch, "hit"));
        var tip = Assert.Single(Background.Import(_harness.Case).Tips);

        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_004);

        var hit = Assert.Single(run.Hits);
        Assert.Equal(genuine.Block!.Hash.Value, hit.Match.Block.Hash);
        Assert.NotEqual(tip.Match.Block.Hash, hit.Match.Block.Hash);
        Assert.Equal([22L], hit.Origins);
        Assert.Equal(hit.Sequence, Assert.Single(_harness.Service.GetHits(_harness.Case)).Sequence);
        var closed = Assert.Single(_harness.Service.GetTips(_harness.Case));
        Assert.Equal((WatchTipState.Confirmed, hit.Sequence), (closed.State, closed.Hit));
        var line = Assert.Single(_harness.Journal(MonitoringJournalKinds.Hit));
        Assert.Equal(line.Sequence, hit.JournalSequence);
        Assert.Contains("\"tip\":" + tip.Sequence, line.Details, StringComparison.Ordinal);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Theory]
    [InlineData(WatchGate.NoExit, LookupStatus.Fetched)]
    [InlineData(WatchGate.Locked, LookupStatus.Fetched)]
    [InlineData(WatchGate.Open, LookupStatus.Offline)]
    [InlineData(WatchGate.Open, LookupStatus.Failed)]
    public async Task A_tip_that_cannot_be_checked_now_waits_and_is_sealed_when_it_can(WatchGate gate, LookupStatus status)
    {
        var watch = await StartedWatch();
        Genuine();
        _harness.SeenByTheCase(900_004);
        Queue.Add(Entry(watch, "hit"));
        Background.Import(_harness.Case);
        _harness.Gate.State = gate;
        _harness.Bitcoin.TransactionStatus = status;

        var waiting = await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_004);

        Assert.Empty(waiting.Hits);
        Assert.Equal(WatchTipState.Pending, Assert.Single(_harness.Service.GetTips(_harness.Case)).State);
        Assert.Empty(_harness.Service.GetHits(_harness.Case));
        Assert.Empty(_harness.Journal(MonitoringJournalKinds.TipRejected));

        _harness.Gate.State = WatchGate.Open;
        _harness.Bitcoin.TransactionStatus = LookupStatus.Fetched;
        var back = await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_004);

        Assert.Single(back.Hits);
        Assert.Equal(WatchTipState.Confirmed, Assert.Single(_harness.Service.GetTips(_harness.Case)).State);
    }

    [Fact]
    public async Task A_waiting_tip_stays_in_the_case_when_it_is_closed_and_opened_again()
    {
        var watch = await StartedWatch();
        Genuine();
        Queue.Add(Entry(watch, "hit"));
        Background.Import(_harness.Case);

        _harness.Reopen();

        var tip = Assert.Single(_harness.Service.GetTips(_harness.Case));
        Assert.Equal(WatchTipState.Pending, tip.State);
        Assert.True(_harness.Verify().IsIntact);
        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_004);
        Assert.Single(run.Hits);
    }

    [Fact]
    public async Task A_tip_becomes_one_hit_however_often_it_is_seen_and_never_twice()
    {
        var watch = await StartedWatch();
        Genuine();
        Queue.Add(Entry(watch, "hit"));
        Background.Import(_harness.Case);
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_004);

        // The background sees it again, and the open check reads it again in its look-back.
        Queue.Add(Entry(watch, "hit"));
        var again = Background.Import(_harness.Case);
        _harness.Clock.UtcNow = Seen.AddHours(1);
        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_006);

        Assert.Empty(again.Tips);
        Assert.Empty(run.Hits);
        Assert.Single(_harness.Service.GetHits(_harness.Case));
        Assert.Single(_harness.Service.GetTips(_harness.Case));
        Assert.Single(_harness.Journal(MonitoringJournalKinds.Hit));
    }

    [Fact]
    public async Task A_tip_whose_hit_the_open_check_sealed_first_points_at_it_and_seals_nothing_more()
    {
        var watch = await StartedWatch();
        Genuine();
        Queue.Add(Entry(watch, "hit"));
        Background.Import(_harness.Case);
        var tip = _harness.Service.GetTips(_harness.Case).Single();

        // The open check seals the hit while the tip waits for the exit.
        _harness.Bitcoin.TransactionStatus = LookupStatus.Offline;
        _harness.Clock.UtcNow = Seen.AddHours(1);
        var open = await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_006);
        _harness.Bitcoin.TransactionStatus = LookupStatus.Fetched;
        var run = await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_006);

        var hit = Assert.Single(open.Hits);
        Assert.Empty(run.Hits);
        Assert.Single(_harness.Service.GetHits(_harness.Case));
        var closed = Assert.Single(_harness.Service.GetTips(_harness.Case));
        Assert.Equal((tip.Sequence, WatchTipState.Confirmed, hit.Sequence), (closed.Sequence, closed.State, closed.Hit));
    }

    [Fact]
    public async Task A_tip_of_a_watch_taken_back_is_closed_unchecked_and_one_of_a_paused_watch_waits()
    {
        var watch = await StartedWatch();
        Genuine();
        Queue.Add(Entry(watch, "hit"));
        Background.Import(_harness.Case);

        _harness.Service.SetPaused(_harness.Case, watch.Id, true);
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_004);
        Assert.Equal(WatchTipState.Pending, _harness.Service.GetTips(_harness.Case).Single().State);

        _harness.Service.Remove(_harness.Case, watch.Id, null);
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_004);

        Assert.Equal(WatchTipState.Dropped, _harness.Service.GetTips(_harness.Case).Single().State);
        Assert.Single(_harness.Journal(MonitoringJournalKinds.TipDropped));
        Assert.Empty(_harness.Service.GetHits(_harness.Case));
        Assert.Empty(_harness.Bitcoin.TransactionsAsked);
    }

    [Fact]
    public async Task A_tip_of_a_watch_taken_back_is_closed_at_once_since_no_check_of_its_chain_may_come_again()
    {
        var watch = await StartedWatch();
        Genuine();
        Queue.Add(Entry(watch, "hit"));
        Background.Import(_harness.Case);

        _harness.Service.Remove(_harness.Case, watch.Id, null);

        Assert.Equal(WatchTipState.Dropped, _harness.Service.GetTips(_harness.Case).Single().State);
        Assert.Single(_harness.Journal(MonitoringJournalKinds.TipDropped));
    }

    /// <summary>Puts the genuine transaction on the watched address's page: 25 000 sat out to the destination.</summary>
    private BitcoinTransaction Genuine()
    {
        var transaction = Synthetic.Tx("hit", 900_003).In(Node, 30_000).Out(Dest, 25_000).Build();
        _harness.Bitcoin.Holds(Node, transaction);
        return transaction;
    }

    private async Task<Watch> StartedWatch()
    {
        var watch = _harness.WatchBitcoin(Node);
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_000);
        _harness.Clock.UtcNow = Seen.AddMinutes(-30);
        return _harness.Service.GetWatches(_harness.Case).Single(each => each.Id == watch.Id);
    }

    /// <summary>An entry from the background that says 25 000 sat went out to the destination in a transaction.</summary>
    private QueuedCheck Entry(Watch watch, string tx) => new(
        Guid.NewGuid(),
        _harness.Case.Info.Id,
        watch.Id,
        QueuedKind.Checked,
        watch.Filter,
        900_000,
        900_004,
        new WatchChecked(Seen, 900_004, WatchFeedStatus.Read, null),
        [new WatchMatch(
            Synthetic.Txid(tx).Value, new WatchBlock(900_003, "0000ab", Seen), WatchDirection.Out,
            AssetAmount.Of(new Satoshi(25_000)), [Synthetic.Btc("dest")])],
        [12],
        [Responses.Make(12, "{\"txs\":[]}")],
        Seen);
}
