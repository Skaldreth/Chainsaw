using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>
/// What the background saw, brought into the case when it is opened: raw responses sealed again, its hits sealed as
/// tips (never as hits) with a journal line, starts and progress, each entry once.
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
    public async Task A_check_from_the_background_is_sealed_with_its_responses_and_its_hits_only_as_tips()
    {
        var watch = await StartedWatch();
        _harness.SeenByTheCase(900_010);
        var entry = Hit(watch, 900_003);
        Queue.Add(entry);

        var result = Background.Import(_harness.Case);

        Assert.Equal((1, 2), (result.Entries, result.Responses));
        var hit = Assert.Single(result.Tips);
        Responses.AssertSame(entry.Matches[0], hit.Match);
        var sealedResponses = ResponseSequences();
        Assert.Equal(3, sealedResponses.Count);
        Assert.Equal([sealedResponses[2]], hit.Origins);
        Assert.Equal(hit.Sequence, Assert.Single(_harness.Service.GetTips(_harness.Case)).Sequence);
        Assert.Equal(WatchTipState.Pending, hit.State);
        Assert.Equal(entry.Id, hit.Entry);
        Assert.Empty(_harness.Service.GetHits(_harness.Case));
        Assert.Empty(_harness.Journal(MonitoringJournalKinds.Hit));
        var line = Assert.Single(_harness.Journal(MonitoringJournalKinds.Tip));
        Assert.Contains("väntar på kontroll", line.Summary, StringComparison.Ordinal);
        Assert.Equal(line.Sequence, hit.JournalSequence);
        Assert.Single(_harness.Journal(MonitoringJournalKinds.Imported));
        Assert.Equal(900_000, _harness.Service.GetChecked(_harness.Case)[watch.Id].Height);
        Assert.Empty(Queue.List(CaseId));
        Assert.True(_harness.Verify().IsIntact);
        var fetchedBy = _harness.Case.Read(reader =>
        {
            using var command = reader.CreateCommand(
                "SELECT fetched_by, exit_route FROM chains_raw_responses ORDER BY sequence DESC");
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
        Assert.Single(_harness.Service.GetTips(_harness.Case));
        Assert.Equal(2, ResponseSequences().Count);
        Assert.Empty(Queue.List(CaseId));
    }

    [Fact]
    public async Task A_hit_seen_twice_by_the_background_comes_in_as_one_tip()
    {
        var watch = await StartedWatch();
        Queue.Add(Hit(watch, 900_003));
        Queue.Add(Hit(watch, 900_003) with { Utc = Seen.AddMinutes(1) });

        var result = Background.Import(_harness.Case);

        Assert.Equal(2, result.Entries);
        Assert.Single(result.Tips);
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
        Assert.Empty(result.Tips);
        Assert.Empty(_harness.Service.GetTips(_harness.Case));
        Assert.Equal(4, ResponseSequences().Count);
    }

    [Fact]
    public async Task A_check_counted_from_another_start_counts_only_above_the_case_s_start()
    {
        var watch = await StartedWatch();
        Queue.Add(Hit(watch, 900_003) with { Start = 899_990 });
        Queue.Add(Hit(watch, 899_995) with { Start = 899_990, Id = Guid.NewGuid() });

        var result = Background.Import(_harness.Case);

        Assert.Equal(900_003, Assert.Single(result.Tips).Match.Block.Height);
    }

    [Fact]
    public async Task A_check_from_a_later_start_never_marks_the_blocks_between_as_read()
    {
        var watch = await StartedWatch();
        Queue.Add(Hit(watch, 900_003) with { Start = 900_002 });

        var result = Background.Import(_harness.Case);

        Assert.Single(result.Tips);
        Assert.Equal(900_000, _harness.Service.GetChecked(_harness.Case)[watch.Id].Height);
    }

    [Fact]
    public async Task A_change_of_only_the_pace_keeps_the_background_s_hits()
    {
        var watch = await StartedWatch();
        Queue.Add(Hit(watch, 900_003));
        _harness.Service.Change(_harness.Case, watch.Id, watch.Filter with { Pace = WatchPace.FifteenMinutes });

        Assert.Single(Background.Import(_harness.Case).Tips);
    }

    [Fact]
    public async Task The_plan_carries_the_tips_the_case_has_in_the_look_back()
    {
        var watch = await StartedWatch();
        Queue.Add(Hit(watch, 900_003));
        Background.Import(_harness.Case);

        var described = Assert.Single(Background.Describe(_harness.Case, Seen).Watches);

        var tip = _harness.Service.GetTips(_harness.Case).Single();
        Assert.Equal([WatchSchedule.Key(watch.Id, tip.Match)], described.Known);
    }

    [Fact]
    public async Task Money_at_or_below_the_start_is_not_a_hit()
    {
        var watch = await StartedWatch();
        Queue.Add(Hit(watch, 900_000));

        Assert.Empty(Background.Import(_harness.Case).Tips);
    }

    [Fact]
    public void A_start_the_background_set_is_sealed_when_the_watch_has_none()
    {
        var watch = _harness.WatchBitcoin(Synthetic.Wpkh("s"));
        _harness.SeenByTheCase(900_010);
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

    [Theory]
    [InlineData(Chain.Tron, "tron-mainnet", 80_000_010)]
    [InlineData(Chain.Bsc, "bsc-mainnet", 130_000_010)]
    [InlineData(Chain.Litecoin, "litecoin-mainnet", 3_000_010)]
    public void A_start_from_the_background_comes_in_on_tron_bsc_and_litecoin(Chain chain, string name, long height)
    {
        var anchor = chain switch
        {
            Chain.Tron => EthereumAddress.Parse(Chain.Tron, "41" + new string('a', 40)).ToChainAddress(),
            Chain.Bsc => EthereumAddress.Parse(Chain.Bsc, "0x" + new string('a', 40)).ToChainAddress(),
            _ => BitcoinAddress.FromWitness(
                0, Enumerable.Repeat((byte)0x33, 20).ToArray(), BitcoinNetwork.Litecoin).ToChainAddress(),
        };
        var watch = _harness.Service.Add(
            _harness.Case,
            new WatchSubject(WatchSubjectKind.Address, anchor, "Bevakad"),
            new WatchFilter(chain, WatchDirection.Both, [], [], null, null));

        // The case has seen the chain this far in its own sealed answers, so the background's start can be taken.
        _harness.Case.Write(transaction => _harness.Get<IResponseTransfer>().Seal(
            transaction, Responses.Make(1, "height", chain: name), new SealedResponseIndex(height, [])));
        Queue.Add(Started(watch, height));

        Assert.Equal(1, Background.Import(_harness.Case).Entries);
        Assert.Equal(height, Assert.Single(_harness.Service.GetWatches(_harness.Case)).StartHeight);
        Assert.Empty(Queue.List(CaseId));
    }

    [Fact]
    public void A_start_beyond_what_the_case_has_seen_waits_in_the_queue_until_the_case_has_seen_that_far()
    {
        // A forged start far ahead would hide every real hit, since only money above the start is one; taken lower
        // than the background set it, money from before the watch was made would be hits.
        var watch = _harness.WatchBitcoin(Synthetic.Wpkh("s"));
        _harness.SeenByTheCase(900_010);
        Queue.Add(Started(watch, 900_020));

        Assert.Equal(0, Background.Import(_harness.Case).Entries);
        Assert.Null(Assert.Single(_harness.Service.GetWatches(_harness.Case)).StartHeight);
        Assert.Single(Queue.List(CaseId));

        _harness.SeenByTheCase(900_025);
        Assert.Equal(1, Background.Import(_harness.Case).Entries);

        Assert.Equal(900_020, Assert.Single(_harness.Service.GetWatches(_harness.Case)).StartHeight);
        Assert.Equal(900_020, _harness.Service.GetChecked(_harness.Case)[watch.Id].Height);
        Assert.Empty(Queue.List(CaseId));
    }

    [Fact]
    public void A_case_that_saw_the_chain_long_before_the_watch_never_starts_it_back_there_nor_loses_its_hits()
    {
        // The case traced a year back and the watch was made, and closed, before Chainsaw asked the height: the
        // background started it and saw a hit. Neither the old height nor the open check's later start may stand.
        _harness.SeenByTheCase(800_000, Seen.AddYears(-1));
        _harness.Clock.UtcNow = Seen.AddMinutes(-30);
        var watch = _harness.WatchBitcoin(Synthetic.Wpkh("w"));
        Queue.Add(Started(watch, 900_000) with { Utc = Seen.AddMinutes(-10) });
        Queue.Add(Hit(watch, 900_003));

        Assert.Equal(0, Background.Import(_harness.Case).Entries);
        _harness.SeenByTheCase(900_010, Seen.AddHours(1));
        var result = Background.Import(_harness.Case);

        Assert.Equal(2, result.Entries);
        Assert.Single(result.Tips);
        Assert.Equal(900_000, Assert.Single(_harness.Service.GetWatches(_harness.Case)).StartHeight);
        Assert.Equal(900_000, _harness.Service.GetChecked(_harness.Case)[watch.Id].Height);
    }

    [Fact]
    public void A_start_is_never_taken_lower_than_the_case_had_seen_when_the_watch_was_made()
    {
        // The case's own lookup came before the watch was made, so the chain was at least that high then.
        _harness.Clock.UtcNow = Seen;
        _harness.SeenByTheCase(900_010);
        var watch = _harness.WatchBitcoin(Synthetic.Wpkh("s"));
        _harness.SeenByTheCase(900_050, Seen.AddHours(1));
        Queue.Add(Started(watch, 0));

        Background.Import(_harness.Case);

        // At least that, or what the later lookup bounds the chain to have been then.
        Assert.InRange(Assert.Single(_harness.Service.GetWatches(_harness.Case)).StartHeight!.Value, 900_010, 900_050);
    }

    [Fact]
    public void No_start_is_taken_from_the_background_while_the_case_has_seen_no_block()
    {
        var watch = _harness.WatchBitcoin(Synthetic.Wpkh("s"));
        Queue.Add(Started(watch, 9_999_999));
        Queue.Add(Hit(watch, 900_003));

        var result = Background.Import(_harness.Case);

        // Both wait in the queue: the start, and the check that would bring it.
        Assert.Empty(result.Tips);
        Assert.Equal(0, result.Entries);
        Assert.Equal(2, Queue.List(CaseId).Count);
        Assert.Null(Assert.Single(_harness.Service.GetWatches(_harness.Case)).StartHeight);
        Assert.Empty(_harness.Journal(MonitoringJournalKinds.Started));
        Assert.False(_harness.Service.GetChecked(_harness.Case).ContainsKey(watch.Id));
    }

    [Fact]
    public void A_check_whose_start_never_came_in_brings_its_start_and_its_hits()
    {
        // The start's entry was set aside, so the watch has no start when the background's check comes in.
        var watch = _harness.WatchBitcoin(Synthetic.Wpkh("w"));
        _harness.SeenByTheCase(900_010);
        Queue.Add(Hit(watch, 900_003));

        var result = Background.Import(_harness.Case);

        Assert.Single(result.Tips);
        Assert.Equal(900_000, Assert.Single(_harness.Service.GetWatches(_harness.Case)).StartHeight);
        Assert.False(_harness.Service.GetChecked(_harness.Case).ContainsKey(watch.Id));
        Assert.Single(_harness.Journal(MonitoringJournalKinds.Started));
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
    public async Task A_read_through_height_is_never_taken_from_the_background_however_far_the_case_has_seen()
    {
        var watch = await StartedWatch();
        _harness.SeenByTheCase(900_010);
        var forged = Hit(watch, 900_003);
        Queue.Add(forged with { Checked = forged.Checked! with { Height = 9_999_999, Tip = 9_999_999 } });

        var result = Background.Import(_harness.Case);

        Assert.Single(result.Tips);
        var checkedAt = _harness.Service.GetChecked(_harness.Case)[watch.Id];
        Assert.Equal((900_000, 900_000), (checkedAt.Height, checkedAt.Tip));
    }

    [Fact]
    public async Task A_check_from_the_background_never_moves_how_far_the_case_has_read()
    {
        // How far a check read, and when, are only the background's word: a forged entry that said it read to the
        // chain's tip, or in a far future, would let the open check skip or never come to the blocks it never read.
        var watch = await StartedWatch();
        var before = _harness.Service.GetChecked(_harness.Case)[watch.Id];
        _harness.SeenByTheCase(900_010);
        var forged = Hit(watch, 900_003);
        Queue.Add(forged with
        {
            Checked = forged.Checked! with { CheckedUtc = Seen.AddYears(100), Height = 900_010, Tip = 900_010 },
        });

        var result = Background.Import(_harness.Case);

        Assert.Single(result.Tips);
        Assert.Equal(before, _harness.Service.GetChecked(_harness.Case)[watch.Id]);
        Assert.True(MonitoringService.IsDue(watch, before, 900_011, _harness.Clock.UtcNow));
    }

    [Fact]
    public void A_start_from_the_background_never_notes_the_watch_as_checked_later_than_now()
    {
        // A start dated in a far future would make the watch never due, so that no check ever came.
        var watch = _harness.WatchBitcoin(Synthetic.Wpkh("s"));
        _harness.SeenByTheCase(900_010);
        Queue.Add(Started(watch, 900_010) with { Utc = Seen.AddYears(100) });

        Background.Import(_harness.Case);

        Assert.True(_harness.Service.GetChecked(_harness.Case)[watch.Id].CheckedUtc <= _harness.Clock.UtcNow);
    }

    [Fact]
    public void A_start_is_never_taken_far_below_the_chain_when_the_case_had_seen_no_block_as_the_watch_was_made()
    {
        // The case had no height when the watch was made (made offline, say), so only its later lookup bounds it: the
        // chain made at most so many blocks between, and a start far below would make old money hits.
        _harness.Clock.UtcNow = Seen.AddMinutes(-30);
        var forged = _harness.WatchBitcoin(Synthetic.Wpkh("f"));
        var honest = _harness.WatchBitcoin(Synthetic.Wpkh("h"));
        _harness.SeenByTheCase(900_050, Seen);
        _harness.Clock.UtcNow = Seen.AddMinutes(1);
        Queue.Add(Started(forged, 1));
        Queue.Add(Started(honest, 900_045));

        Background.Import(_harness.Case);

        var starts = _harness.Service.GetWatches(_harness.Case).ToDictionary(each => each.Id, each => each.StartHeight);
        Assert.InRange(starts[forged.Id]!.Value, 900_050 - 6 - 1 - 24, 900_050);
        Assert.Equal(900_045, starts[honest.Id]);
    }

    [Fact]
    public void A_start_waits_in_the_queue_while_its_watch_is_paused_before_it_had_one()
    {
        // Brought in while paused, the start would be dropped, and the open check would start the watch only when
        // it is resumed, losing what the background saw in between.
        var watch = _harness.WatchBitcoin(Synthetic.Wpkh("s"));
        Queue.Add(Started(watch, 900_020));
        Queue.Add(Hit(watch, 900_023) with { Start = 900_020 });
        _harness.Service.SetPaused(_harness.Case, watch.Id, true);
        _harness.SeenByTheCase(900_025);

        Assert.Equal(0, Background.Import(_harness.Case).Entries);
        Assert.Equal(2, Queue.List(CaseId).Count);

        _harness.Service.SetPaused(_harness.Case, watch.Id, false);
        var result = Background.Import(_harness.Case);

        Assert.Equal(900_020, Assert.Single(_harness.Service.GetWatches(_harness.Case)).StartHeight);
        Assert.Single(result.Tips);
        Assert.Empty(Queue.List(CaseId));
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

    [Theory]
    [InlineData("null-tx")]
    [InlineData("null-hash")]
    [InlineData("negative")]
    [InlineData("both")]
    [InlineData("asset")]
    [InlineData("status")]
    public async Task A_hostile_entry_is_set_aside_and_the_others_come_in(string defect)
    {
        var watch = await StartedWatch();
        var good = Hit(watch, 900_003);
        var match = good.Matches[0];
        var bad = (good with { Id = Guid.NewGuid(), Utc = Seen.AddMinutes(-5) }) switch
        {
            var entry when defect == "null-tx" => entry with { Matches = [match with { Transaction = null! }] },
            var entry when defect == "null-hash" =>
                entry with { Matches = [match with { Block = match.Block with { Hash = null! } }] },
            var entry when defect == "negative" =>
                entry with { Matches = [match with { Amount = AssetAmount.Of(new Satoshi(0)) - match.Amount }] },
            var entry when defect == "both" => entry with { Matches = [match with { Direction = WatchDirection.Both }] },
            var entry when defect == "asset" => entry with
            {
                Matches = [match with { Amount = new AssetAmount(Asset.Ether, match.Amount.Units) }],
            },
            var entry => entry with { Checked = good.Checked! with { Status = (WatchFeedStatus)9 } },
        };
        Queue.Add(good);
        Queue.Add(bad);

        var result = Background.Import(_harness.Case);

        Assert.Equal(1, result.Entries);
        Assert.Single(result.Tips);
        Assert.Equal([BackgroundWatches.UnreadableText], result.Problems);
        Assert.Empty(Queue.List(CaseId));
        Assert.True(_harness.Verify().IsIntact);
    }

    [Theory]
    [InlineData("Responses")]
    [InlineData("Origins")]
    public async Task An_entry_without_its_lists_cannot_be_read(string list)
    {
        var watch = await StartedWatch();
        var json = System.Text.Json.Nodes.JsonNode.Parse(BackgroundCodec.Encode(Hit(watch, 900_003)))!;
        json[list] = null;

        Assert.Throws<InvalidDataException>(
            () => BackgroundCodec.DecodeEntry(System.Text.Encoding.UTF8.GetBytes(json.ToJsonString())));
    }

    [Theory]
    [InlineData(WatchDirection.In, "only")]
    [InlineData(WatchDirection.Out, "other")]
    public async Task A_match_the_watch_s_filter_could_not_give_is_set_aside(WatchDirection direction, string party)
    {
        var watch = _harness.WatchBitcoin(Synthetic.Wpkh("w"), WatchDirection.Out, receivers: [Synthetic.Btc("only")]);
        await _harness.Service.CheckAsync(_harness.Case, Chain.Bitcoin, 900_000);
        watch = _harness.Service.GetWatches(_harness.Case).Single();
        var entry = Hit(watch, 900_003);
        Queue.Add(entry with
        {
            Matches = [entry.Matches[0] with { Direction = direction, Counterparties = [Synthetic.Btc(party)] }],
        });

        var result = Background.Import(_harness.Case);

        Assert.Equal(0, result.Entries);
        Assert.Empty(_harness.Service.GetTips(_harness.Case));
        Assert.Equal([BackgroundWatches.UnreadableText], result.Problems);
    }

    [Fact]
    public async Task An_entry_whose_file_cannot_be_set_aside_just_now_never_stops_the_others()
    {
        var watch = await StartedWatch();
        var forged = Hit(watch, 900_003) with { Id = Guid.NewGuid() };
        forged = forged with { Responses = [forged.Responses[0] with { Sha256 = new string('0', 64) }] };
        Queue.Add(Hit(watch, 900_003));
        var held = new HeldQueue((WatchQueue)Queue, forged);
        var transfer = _harness.Get<Chains.Contracts.IResponseTransfer>();
        var background = new BackgroundWatches(
            _harness.Get<MonitoringService>(),
            held,
            transfer,
            _harness.Get<Chains.Contracts.ICaseHeights>(),
            _harness.Clock);

        var result = background.Import(_harness.Case);

        Assert.Equal(1, result.Entries);
        Assert.Single(result.Tips);
        Assert.Equal([BackgroundWatches.UnreadableText], result.Problems);
        Assert.Empty(Queue.List(CaseId));
    }

    [Fact]
    public void A_token_match_whose_decimals_another_source_writes_otherwise_is_read()
    {
        var token = Asset.Token(Chain.Ethereum, Synthetic.Eth("t").Value, "TOK", 6);
        var filter = new WatchFilter(
            Chain.Ethereum, WatchDirection.Both, [], [], token, new AssetAmount(token, 1_000));
        var match = new WatchMatch(
            "0x" + new string('a', 64), new WatchBlock(900_003, "0xab", Seen), WatchDirection.In,
            new AssetAmount(Asset.Token(Chain.Ethereum, Synthetic.Eth("t").Value, "TOK", 18), 5_000), []);
        var entry = new QueuedCheck(
            Guid.NewGuid(), CaseId, "w", QueuedKind.Checked, filter, 900_000, 900_004,
            new WatchChecked(Seen, 900_004, WatchFeedStatus.Read, null), [match], [], [], Seen);

        var read = BackgroundCodec.DecodeEntry(BackgroundCodec.Encode(entry));

        Assert.Equal(5_000, Assert.Single(read.Matches).Amount.Units);
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

    private QueuedCheck Started(Watch watch, long start) => new(
        Guid.NewGuid(), CaseId, watch.Id, QueuedKind.Started, watch.Filter, start, start, null, [], [], [], Seen);

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

/// <summary>
/// The real queue with one more entry first, whose responses the case refuses and whose file is held so that it cannot
/// be set aside just now.
/// </summary>
/// <param name="inner">The real queue.</param>
/// <param name="held">The held entry.</param>
internal sealed class HeldQueue(WatchQueue inner, QueuedCheck held) : IWatchQueue
{
    public void Add(QueuedCheck entry) => inner.Add(entry);

    public IReadOnlyList<Guid> List(Guid caseId) => [held.Id, .. inner.List(caseId)];

    public QueuedCheck? Read(Guid caseId, Guid id) => id == held.Id ? held : inner.Read(caseId, id);

    public void Remove(Guid caseId, Guid id) => inner.Remove(caseId, id);

    public void SetAside(Guid caseId, Guid id) =>
        throw new IOException(id == held.Id ? "The file is held." : "Only the held entry is set aside here.");

    public bool HasHits() => inner.HasHits();
}
