using System.Globalization;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Tests;

/// <summary>The files the open Chainsaw and the background share: plan, queue, holds, status and progress.</summary>
public sealed class BackgroundFilesTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "chainsaw-background-" + Guid.NewGuid().ToString("N"));

    private readonly BackgroundFolder _folder;

    public BackgroundFilesTests() => _folder = new BackgroundFolder(_directory, new FakeProtector());

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public void The_plan_is_written_protected_and_read_back_whole()
    {
        var file = new BackgroundPlanFile(_folder);
        var watch = new BackgroundWatch(
            "w1",
            [Synthetic.Btc("a"), Synthetic.Btc("b")],
            new WatchFilter(Chain.Bitcoin, WatchDirection.Out, [Synthetic.Btc("r")], [], null,
                AssetAmount.Of(new Satoshi(50_000))) { Pace = WatchPace.FiveMinutes },
            900_000,
            new WatchChecked(Time(5), 900_004, WatchFeedStatus.Partial, "lucka") { Tip = 900_006 });
        var token = new BackgroundWatch(
            "w2", [Synthetic.On(Synthetic.Eth("e"))],
            new WatchFilter(Chain.Ethereum, WatchDirection.In, [], [], Synthetic.Usdt, Synthetic.Tether(10)),
            null, null);
        var caseId = Guid.NewGuid();

        file.Update(plan => new BackgroundPlan(true, ExitRoute.Windows, WatchAlerts.Windows | WatchAlerts.Sound,
            [new BackgroundCase(caseId, [watch, token], Time(6))]));
        var read = file.Read();

        Assert.True(read.Enabled);
        Assert.Equal(ExitRoute.Windows, read.Route);
        Assert.Equal(WatchAlerts.Windows | WatchAlerts.Sound, read.Alerts);
        var @case = Assert.Single(read.Cases);
        Assert.Equal(caseId, @case.CaseId);
        Assert.Equal(Time(6), @case.WrittenUtc);
        var first = @case.Watches[0];
        Assert.Equal(watch.Addresses, first.Addresses);
        Assert.Equal(WatchCodec.Encode(watch.Filter), WatchCodec.Encode(first.Filter));
        Assert.Equal(900_000, first.StartHeight);
        Assert.Equal(watch.Checked, first.Checked);
        Assert.Equal(900_006, first.Checked!.Tip);
        Assert.Equal(WatchCodec.Encode(token.Filter), WatchCodec.Encode(@case.Watches[1].Filter));
        Assert.NotNull(file.WrittenAt());
        var bytes = File.ReadAllBytes(Path.Combine(_directory, "plan.bin"));
        Assert.DoesNotContain(watch.Addresses[0].Value, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
    }

    [Fact]
    public void A_missing_or_unreadable_plan_watches_nothing()
    {
        var file = new BackgroundPlanFile(_folder);
        Assert.Same(BackgroundPlan.Empty, file.Read());
        Assert.Null(file.WrittenAt());

        Directory.CreateDirectory(_directory);
        File.WriteAllBytes(Path.Combine(_directory, "plan.bin"), [1, 2, 3]);

        Assert.False(file.Read().HasWork);
    }

    [Fact]
    public void A_plan_protected_for_another_purpose_is_not_read()
    {
        var progress = new BackgroundProgressFile(_folder);
        progress.Write([new BackgroundProgress(Guid.NewGuid(), "w", 1, null, [])]);
        File.Copy(Path.Combine(_directory, "progress.bin"), Path.Combine(_directory, "plan.bin"));

        Assert.Same(BackgroundPlan.Empty, new BackgroundPlanFile(_folder).Read());
    }

    [Fact]
    public void A_case_without_active_watches_leaves_the_plan()
    {
        var id = Guid.NewGuid();
        var other = new BackgroundCase(Guid.NewGuid(), [Watch("x")], Time(1));
        var plan = new BackgroundPlan(true, ExitRoute.Windows, WatchAlerts.None,
            [new BackgroundCase(id, [Watch("a")], Time(1)), other]);

        var changed = plan.With(new BackgroundCase(id, [Watch("b")], Time(2)));
        var emptied = plan.With(new BackgroundCase(id, [], Time(2)));

        Assert.Equal(["x", "b"], changed.Cases.SelectMany(@case => @case.Watches).Select(watch => watch.WatchId));
        Assert.Equal([other.CaseId], emptied.Cases.Select(@case => @case.CaseId));
        Assert.False((plan with { Enabled = false }).HasWork);
        Assert.False(BackgroundPlan.Empty.HasWork);
    }

    [Fact]
    public void Queued_entries_are_listed_oldest_first_read_back_and_removed()
    {
        var queue = new WatchQueue(_folder);
        var caseId = Guid.NewGuid();
        var later = Entry(caseId, Time(9), hit: true);
        var earlier = Entry(caseId, Time(2), hit: false);
        queue.Add(later);
        queue.Add(earlier);
        queue.Add(Entry(Guid.NewGuid(), Time(1), hit: false));

        Assert.Equal([earlier.Id, later.Id], queue.List(caseId));
        var read = queue.Read(caseId, later.Id)!;
        Assert.Equal(later.WatchId, read.WatchId);
        Responses.AssertSame(later.Matches.Single(), read.Matches.Single());
        Assert.Equal(later.Checked, read.Checked);
        Assert.Equal(later.Responses.Single().Body, read.Responses.Single().Body);
        Assert.Equal(later.Responses.Single().Sha256, read.Responses.Single().Sha256);
        Assert.Equal(later.Origins, read.Origins);
        Assert.True(queue.HasHits());

        queue.Remove(caseId, later.Id);

        Assert.False(queue.HasHits());
        Assert.Equal([earlier.Id], queue.List(caseId));
        Assert.Null(queue.Read(caseId, later.Id));
    }

    [Fact]
    public void A_queued_entry_that_cannot_be_read_is_set_aside_and_never_read_again()
    {
        var queue = new WatchQueue(_folder);
        var caseId = Guid.NewGuid();
        var entry = Entry(caseId, Time(2), hit: true);
        queue.Add(entry);
        var path = Directory.GetFiles(Path.Combine(_directory, "queue", caseId.ToString("N"))).Single();
        var bytes = File.ReadAllBytes(path);
        bytes[5] ^= 0xFF;
        File.WriteAllBytes(path, bytes);

        Assert.Throws<InvalidDataException>(() => queue.Read(caseId, entry.Id));

        Assert.Empty(queue.List(caseId));
        Assert.False(queue.HasHits());
        Assert.True(File.Exists(path + ".broken"));
    }

    [Fact]
    public void A_queued_response_whose_body_does_not_match_its_hash_is_refused()
    {
        var queue = new WatchQueue(_folder);
        var caseId = Guid.NewGuid();
        var entry = Entry(caseId, Time(2), hit: false);
        var response = entry.Responses.Single() with { Body = "{\"changed\":true}"u8.ToArray() };
        queue.Add(entry with { Responses = [response] });

        Assert.Throws<InvalidDataException>(() => queue.Read(caseId, entry.Id));
        Assert.Empty(queue.List(caseId));
    }

    [Fact]
    public void A_held_case_is_held_until_its_hold_is_let_go()
    {
        var holds = new CaseHolds(_folder);
        var id = Guid.NewGuid();
        Assert.False(holds.IsHeld(id));

        var hold = holds.Hold(id);

        Assert.NotNull(hold);
        Assert.True(holds.IsHeld(id));
        Assert.Null(holds.Hold(id));
        Assert.False(holds.IsHeld(Guid.NewGuid()));
        hold!.Dispose();
        Assert.False(holds.IsHeld(id));
    }

    [Fact]
    public void The_status_and_the_progress_are_read_back()
    {
        var status = new BackgroundStatusFile(_folder);
        Assert.Null(status.Read());
        status.Write(new BackgroundStatus(BackgroundState.Running, "mempool.space", Time(3), 42));
        Assert.Equal(new BackgroundStatus(BackgroundState.Running, "mempool.space", Time(3), 42), status.Read());
        Assert.Contains("Running", File.ReadAllText(Path.Combine(_directory, "status.json")), StringComparison.Ordinal);

        var progress = new BackgroundProgressFile(_folder);
        Assert.Empty(progress.Read());
        var id = Guid.NewGuid();
        var checkedAt = new WatchChecked(Time(4), 900_002, WatchFeedStatus.Read, null) { Tip = 900_003 };
        progress.Write([new BackgroundProgress(id, "w", 900_000, checkedAt, ["w|tx|Out|BTC"])]);
        var read = Assert.Single(progress.Read());
        Assert.Equal(
            (id, "w", 900_000L, checkedAt), (read.CaseId, read.WatchId, read.StartHeight!.Value, read.Checked));
        Assert.Equal(["w|tx|Out|BTC"], read.Alerted);
    }

    private static DateTimeOffset Time(int minutes) => new DateTimeOffset(2026, 11, 7, 3, 0, 0, TimeSpan.Zero)
        .AddMinutes(minutes);

    private static BackgroundWatch Watch(string id) => new(
        id, [Synthetic.Btc(id)], new WatchFilter(Chain.Bitcoin, WatchDirection.Both, [], [], null, null), 1, null);

    internal static QueuedCheck Entry(Guid caseId, DateTimeOffset utc, bool hit)
    {
        var response = Responses.Make(7, "{\"page\":1}");
        IReadOnlyList<WatchMatch> matches = hit
            ? [new WatchMatch(
                Synthetic.Txid("t").Value, new WatchBlock(900_003, "00ab", utc), WatchDirection.Out,
                AssetAmount.Of(new Satoshi(1_000)), [Synthetic.Btc("r")])]
            : [];
        return new QueuedCheck(
            Guid.NewGuid(), caseId, "w1", QueuedKind.Checked,
            new WatchFilter(Chain.Bitcoin, WatchDirection.Both, [], [], null, null), 900_000, 900_004,
            new WatchChecked(utc, 900_004, WatchFeedStatus.Read, null), matches, hit ? [7] : [], [response], utc)
        {
            HasNewHits = hit,
        };
    }
}

/// <summary>Raw responses as the background would have sealed them, with a matching SHA-256.</summary>
internal static class Responses
{
    public static void AssertSame(WatchMatch expected, WatchMatch actual)
    {
        Assert.Equal(expected with { Counterparties = [] }, actual with { Counterparties = [] });
        Assert.Equal(expected.Counterparties, actual.Counterparties);
    }

    public static SealedResponse Make(long sequence, string body, DateTimeOffset? received = null)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var at = (received ?? new DateTimeOffset(2026, 11, 7, 3, 0, 0, TimeSpan.Zero))
            .ToString("O", CultureInfo.InvariantCulture);
        return new SealedResponse(
            sequence, "bitcoin-mainnet", "esplora", Guid.Parse("0b9e1e4a-5c39-4a8e-9a55-3f1f6a1c0009"),
            "esplora.test", "https://esplora.test/api/address/x/txs", "address-transactions", "x", 200,
            "application/json", at, at, at, "UTC", null, "windows", JsonSerializer.Serialize(BackgroundNames.Fetcher),
            Convert.ToHexStringLower(SHA256.HashData(bytes)), bytes);
    }
}

/// <summary>When a watch is due and how far a check leaves it, shared with the background.</summary>
public sealed class WatchScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_watch_is_due_at_a_new_block_once_its_pace_allows()
    {
        var last = new WatchChecked(Now.AddMinutes(-2), 100, WatchFeedStatus.Read, null);

        Assert.True(WatchSchedule.IsDue(WatchPace.EveryBlock, null, 100, Now));
        Assert.False(WatchSchedule.IsDue(WatchPace.EveryBlock, last, 100, Now));
        Assert.True(WatchSchedule.IsDue(WatchPace.EveryBlock, last, 101, Now));
        Assert.False(WatchSchedule.IsDue(WatchPace.FiveMinutes, last, 101, Now));
        Assert.True(WatchSchedule.IsDue(WatchPace.FiveMinutes, last, 101, Now.AddMinutes(3)));
        Assert.True(WatchSchedule.IsDue(
            WatchPace.EveryBlock, last with { Status = WatchFeedStatus.Failed }, 100, Now));
    }

    [Fact]
    public void A_check_moves_on_only_as_far_as_it_read()
    {
        static WatchCheckResult Result(WatchFeedStatus status, long? highest, long? above = null) =>
            new([], status, [], highest, []) { CompleteAbove = above };

        var read = WatchSchedule.After(100, Result(WatchFeedStatus.Read, 108), [], 110, Now);
        var partial = WatchSchedule.After(100, Result(WatchFeedStatus.Partial, 108, 105), ["lucka"], 110, Now);
        var unknown = WatchSchedule.After(100, Result(WatchFeedStatus.Partial, 108), [], 110, Now);
        var failed = WatchSchedule.After(100, Result(WatchFeedStatus.Failed, null), ["fel"], 110, Now);

        Assert.Equal((110L, 110L), (read.Height, read.Tip));
        Assert.Equal((105L, 110L, "lucka"), (partial.Height, partial.Tip, partial.Problem));
        Assert.Equal(100, unknown.Height);
        Assert.Equal((100L, WatchFeedStatus.Failed), (failed.Height, failed.Status));
        Assert.Equal(100, WatchSchedule.DownTo(100, null));
        Assert.Equal(104, WatchSchedule.DownTo(100, read with { Height = 110 }));
        Assert.Equal(100, WatchSchedule.DownTo(100, read with { Height = 103 }));
    }

    [Fact]
    public void A_token_hit_is_the_same_hit_whatever_symbol_the_source_gave_it()
    {
        static WatchMatch Match(string symbol, int decimals) => new(
            "0x" + new string('a', 64), new WatchBlock(100, "0xab", Now), WatchDirection.In,
            new AssetAmount(Asset.Token(Chain.Ethereum, Synthetic.Eth("t").Value, symbol, decimals), 5), []);

        // A source that had not read the token's name yet calls it "token"; the next read names it.
        Assert.Equal(WatchSchedule.Key("w", Match("token", 6)), WatchSchedule.Key("w", Match("PEPE", 6)));
        Assert.Equal(WatchSchedule.Key("w", Match("PEPE", 6)), WatchSchedule.Key("w", Match("PEPE", 18)));
        Assert.Equal(
            "w|tx|Out|BTC",
            WatchSchedule.Key("w", new WatchMatch(
                "tx", new WatchBlock(100, "ab", Now), WatchDirection.Out, new AssetAmount(Asset.Bitcoin, 5), [])));
    }
}
