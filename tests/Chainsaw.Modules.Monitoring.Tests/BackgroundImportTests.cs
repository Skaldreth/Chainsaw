using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>
/// What the background saw, brought into the case when it is opened: raw responses sealed again, hits sealed with a
/// journal line, starts and progress, each entry once.
/// </summary>
public sealed class BackgroundImportTests : IDisposable
{
    private static readonly DateTimeOffset Seen = new(2026, 11, 7, 4, 0, 0, TimeSpan.Zero);

    private readonly Harness _harness = new();

    public void Dispose() => _harness.Dispose();

    private IBackgroundWatches Background => _harness.Get<IBackgroundWatches>();

    private IWatchQueue Queue => _harness.Get<IWatchQueue>();

    private Guid CaseId => _harness.Case.Info.Id;

    [Fact]
    public async Task The_plan_knows_only_the_active_watches_their_addresses_filter_and_progress()
    {
        var active = _harness.WatchBitcoin(Synthetic.Wpkh("a"), WatchDirection.Out);
        var paused = _harness.WatchBitcoin(Synthetic.Wpkh("p"));
        _harness.Service.SetPaused(_harness.Case, paused.Id, true);
        var removed = _harness.WatchBitcoin(Synthetic.Wpkh("r"));
        _harness.Service.Remove(_harness.Case, removed.Id, null);
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_000);

        var described = Background.Describe(_harness.Case, Seen);

        Assert.Equal(CaseId, described.CaseId);
        var watch = Assert.Single(described.Watches);
        Assert.Equal(active.Id, watch.WatchId);
        Assert.Equal([Synthetic.Btc("a")], watch.Addresses);
        Assert.Equal(WatchDirection.Out, watch.Filter.Direction);
        Assert.Equal(900_000, watch.StartHeight);
        Assert.Equal(900_000, watch.Checked!.Height);
        Assert.Equal(Seen, described.WrittenUtc);
    }

    [Fact]
    public async Task A_check_from_the_background_is_sealed_with_its_responses_hits_and_journal()
    {
        var watch = await StartedWatch();
        var entry = Hit(watch, 900_003);
        Queue.Add(entry);

        var result = Background.Import(_harness.Case);

        Assert.Equal((1, 2), (result.Entries, result.Responses));
        var hit = Assert.Single(result.Hits);
        Responses.AssertSame(entry.Matches[0], hit.Match);
        var sealedResponses = ResponseSequences();
        Assert.Equal(2, sealedResponses.Count);
        Assert.Equal([sealedResponses[1]], hit.Origins);
        Assert.Equal(hit.Sequence, Assert.Single(_harness.Service.GetHits(_harness.Case)).Sequence);
        var line = Assert.Single(_harness.Journal(MonitoringJournalKinds.Hit));
        Assert.Contains("i bakgrunden", line.Summary, StringComparison.Ordinal);
        Assert.Equal(line.Sequence, hit.JournalSequence);
        Assert.Single(_harness.Journal(MonitoringJournalKinds.Imported));
        Assert.Equal(900_004, _harness.Service.GetChecked(_harness.Case)[watch.Id].Height);
        Assert.Empty(Queue.List(CaseId));
        Assert.True(_harness.Verify().IsIntact);
        var fetchedBy = _harness.Case.Read(reader =>
        {
            using var command = reader.CreateCommand("SELECT fetched_by, exit_route FROM chains_raw_responses");
            using var row = command.ExecuteReader();
            row.Read();
            return (row.GetString(0), row.GetString(1));
        });
        Assert.Contains(BackgroundNames.Fetcher.Name, fetchedBy.Item1, StringComparison.Ordinal);
        Assert.Equal("windows", fetchedBy.Item2);
    }

    [Fact]
    public async Task An_entry_brought_in_twice_is_brought_in_once()
    {
        var watch = await StartedWatch();
        var entry = Hit(watch, 900_003);
        Queue.Add(entry);
        Background.Import(_harness.Case);

        // A crash after the case was saved but before the entry was removed leaves it in the queue.
        Queue.Add(entry);
        var again = Background.Import(_harness.Case);

        Assert.Equal(0, again.Entries);
        Assert.Single(_harness.Service.GetHits(_harness.Case));
        Assert.Equal(2, ResponseSequences().Count);
        Assert.Empty(Queue.List(CaseId));
    }

    [Fact]
    public async Task A_hit_seen_twice_by_the_background_or_already_in_the_case_is_sealed_once()
    {
        var watch = await StartedWatch();
        Queue.Add(Hit(watch, 900_003));
        Queue.Add(Hit(watch, 900_003) with { Utc = Seen.AddMinutes(1) });

        var result = Background.Import(_harness.Case);

        Assert.Equal(2, result.Entries);
        Assert.Single(result.Hits);
        Assert.Equal(4, ResponseSequences().Count);
    }

    [Fact]
    public async Task A_watch_changed_or_taken_back_since_gets_no_hits_but_the_responses_are_kept()
    {
        var watch = await StartedWatch();
        var changed = _harness.WatchBitcoin(Synthetic.Wpkh("c"));
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_000);
        Queue.Add(Hit(watch, 900_003));
        Queue.Add(Hit(changed, 900_003));
        _harness.Service.Change(_harness.Case, changed.Id, changed.Filter with { Direction = WatchDirection.In });
        _harness.Service.Remove(_harness.Case, watch.Id, "klar");

        var result = Background.Import(_harness.Case);

        Assert.Equal(2, result.Entries);
        Assert.Empty(result.Hits);
        Assert.Empty(_harness.Service.GetHits(_harness.Case));
        Assert.Equal(4, ResponseSequences().Count);
    }

    [Fact]
    public async Task Money_at_or_below_the_start_is_not_a_hit()
    {
        var watch = await StartedWatch();
        Queue.Add(Hit(watch, 900_000));

        Assert.Empty(Background.Import(_harness.Case).Hits);
    }

    [Fact]
    public void A_start_the_background_set_is_sealed_when_the_watch_has_none()
    {
        var watch = _harness.WatchBitcoin(Synthetic.Wpkh("s"));
        Queue.Add(new QueuedCheck(
            Guid.NewGuid(), CaseId, watch.Id, QueuedKind.Started, watch.Filter, 900_010, 900_010, null, [], [],
            [Responses.Make(1, "900010")], Seen));

        Background.Import(_harness.Case);

        Assert.Equal(900_010, Assert.Single(_harness.Service.GetWatches(_harness.Case)).StartHeight);
        var line = Assert.Single(_harness.Journal(MonitoringJournalKinds.Started));
        Assert.Contains("i bakgrunden", line.Summary, StringComparison.Ordinal);
        Assert.Equal(900_010, _harness.Service.GetChecked(_harness.Case)[watch.Id].Height);
        Assert.True(_harness.Verify().IsIntact);
    }

    [Fact]
    public async Task A_start_the_case_already_has_is_kept()
    {
        var watch = await StartedWatch();
        Queue.Add(new QueuedCheck(
            Guid.NewGuid(), CaseId, watch.Id, QueuedKind.Started, watch.Filter, 900_010, 900_010, null, [], [], [],
            Seen));

        Background.Import(_harness.Case);

        Assert.Equal(900_000, Assert.Single(_harness.Service.GetWatches(_harness.Case)).StartHeight);
        Assert.Single(_harness.Journal(MonitoringJournalKinds.Started));
    }

    [Fact]
    public async Task An_older_check_from_the_background_never_moves_the_watch_back()
    {
        var watch = await StartedWatch();
        _harness.Clock.UtcNow = Seen.AddHours(1);
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_020);
        Queue.Add(Hit(watch, 900_003));

        Background.Import(_harness.Case);

        Assert.Equal(900_020, _harness.Service.GetChecked(_harness.Case)[watch.Id].Height);
    }

    [Fact]
    public async Task An_entry_that_cannot_be_read_is_set_aside_and_said_while_the_others_come_in()
    {
        var watch = await StartedWatch();
        var good = Hit(watch, 900_003);
        Queue.Add(good);
        var bad = Hit(watch, 900_003) with { Id = Guid.NewGuid(), Utc = Seen.AddMinutes(-5) };
        Queue.Add(bad);
        var path = Directory.GetFiles(Path.Combine(_harness.BackgroundDirectory, "queue", CaseId.ToString("N")))
            .Single(file => file.Contains(bad.Id.ToString("N"), StringComparison.Ordinal));
        File.WriteAllBytes(path, [9, 9, 9]);

        var result = Background.Import(_harness.Case);

        Assert.Equal(1, result.Entries);
        Assert.Equal([BackgroundWatches.UnreadableText], result.Problems);
        Assert.Empty(Queue.List(CaseId));
        Assert.True(File.Exists(path + ".broken"));
    }

    [Fact]
    public void Another_case_s_entries_stay_in_the_queue()
    {
        var other = Guid.NewGuid();
        Queue.Add(BackgroundFilesTests.Entry(other, Seen, hit: true));

        Assert.Equal(0, Background.Import(_harness.Case).Entries);
        Assert.Single(Queue.List(other));
    }

    private async Task<Watch> StartedWatch()
    {
        var watch = _harness.WatchBitcoin(Synthetic.Wpkh("w"));
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_000);
        _harness.Clock.UtcNow = Seen.AddMinutes(-30);
        return _harness.Service.GetWatches(_harness.Case).Single(each => each.Id == watch.Id);
    }

    private QueuedCheck Hit(Watch watch, long height) => new(
        Guid.NewGuid(),
        CaseId,
        watch.Id,
        QueuedKind.Checked,
        watch.Filter,
        900_000,
        900_004,
        new WatchChecked(Seen, 900_004, WatchFeedStatus.Read, null),
        [new WatchMatch(
            Synthetic.Txid("hit").Value, new WatchBlock(height, "0000ab", Seen), WatchDirection.Out,
            AssetAmount.Of(new Satoshi(25_000)), [Synthetic.Btc("dest")])],
        [12],
        [Responses.Make(11, "900004"), Responses.Make(12, "{\"txs\":[]}")],
        Seen);

    private List<long> ResponseSequences() => _harness.Case.Read(reader =>
    {
        using var command = reader.CreateCommand("SELECT sequence FROM chains_raw_responses ORDER BY sequence");
        using var rows = command.ExecuteReader();
        var all = new List<long>();
        while (rows.Read())
        {
            all.Add(rows.GetInt64(0));
        }

        return all;
    });
}
