using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;

namespace Chainsaw.Watcher.Tests;

/// <summary>The round's case in memory: the chains' tables, sealing and nothing of a real case.</summary>
public sealed class ScratchCaseTests : IDisposable
{
    private static readonly SealedChain Raw = new("t.raw", "t_raw", ["kind", "body"]);

    private readonly ScratchCase _case = new([new Tables()]);

    public void Dispose() => _case.Dispose();

    [Fact]
    public void Records_are_sealed_in_order_and_told_from_a_mark()
    {
        _case.Write(transaction => transaction.AppendSealed(Raw.Name, ["a", new byte[] { 1 }]));
        var mark = _case.Mark();

        var second = _case.Write(transaction => transaction.AppendSealed(Raw.Name, ["b", new byte[] { 2 }]));

        Assert.Equal(2, second.Sequence);
        Assert.Equal([2L], _case.SealedSince(Raw.Name, mark));
        Assert.Equal([1L, 2L], _case.SealedSince(Raw.Name, 0));
        Assert.Empty(_case.SealedSince("other", 0));
        Assert.Equal("b", _case.Read(reader =>
        {
            using var command = reader.CreateCommand("SELECT kind FROM t_raw WHERE sequence = 2");
            return (string)command.ExecuteScalar()!;
        }));
    }

    [Fact]
    public void A_write_that_fails_seals_nothing()
    {
        var mark = _case.Mark();

        Assert.Throws<InvalidOperationException>(() => _case.Write<int>(transaction =>
        {
            transaction.AppendSealed(Raw.Name, ["a", new byte[] { 1 }]);
            throw new InvalidOperationException();
        }));

        Assert.Empty(_case.SealedSince(Raw.Name, mark));
        Assert.Equal(1, _case.Write(transaction => transaction.AppendSealed(Raw.Name, ["c", null]).Sequence));
    }

    [Fact]
    public void It_keeps_no_journal_and_refuses_an_unknown_chain_and_work_once_closed()
    {
        Assert.Throws<NotSupportedException>(() => _case.AppendJournal(new JournalDraft("x", "y")));
        Assert.Throws<ArgumentException>(() => _case.Write(transaction => transaction.AppendSealed("nej", ["a"])));
        Assert.Empty(_case.ReadJournal());

        _case.Close();

        Assert.True(_case.IsClosed);
        Assert.Equal(CaseError.Closed,
            Assert.Throws<CaseException>(() => _case.Read(_ => 0)).Error);
    }

    [Fact]
    public void The_chains_tables_are_the_real_ones()
    {
        using var world = new World();
        using var scratch = world.Background<Func<ScratchCase>>()();

        Assert.Equal(0, scratch.Read(reader =>
        {
            using var command = reader.CreateCommand("SELECT count(*) FROM chains_raw_responses");
            return (long)command.ExecuteScalar()!;
        }));
    }

    private sealed class Tables : ICaseSchemaContributor
    {
        public string Module => "t";

        public IReadOnlyList<CaseSchemaStep> Steps { get; } =
        [
            new(1, "t", "CREATE TABLE t_raw (sequence INTEGER PRIMARY KEY, kind TEXT, body BLOB, hash BLOB NOT NULL);"),
        ];

        public IReadOnlyList<SealedChain> SealedChains { get; } = [Raw];
    }
}

/// <summary>The ways of telling: only those chosen, and one failing never stops the others.</summary>
public sealed class AlertsTests
{
    [Fact]
    public void Only_the_chosen_ways_tell_and_a_failing_one_leaves_the_others()
    {
        var windows = new Channel(WatchAlerts.Windows, fails: true);
        var sound = new Channel(WatchAlerts.Sound);
        var screen = new Channel(WatchAlerts.Screen);
        var alerts = new Alerts([windows, sound, screen]);

        alerts.Raise(WatchAlerts.Windows | WatchAlerts.Sound);

        Assert.Equal((1, 1, 0), (windows.Shown, sound.Shown, screen.Shown));
        alerts.Raise(WatchAlerts.None);
        Assert.Equal(1, sound.Shown);
    }

    [Fact]
    public void Every_way_says_only_that_a_watch_has_hit()
    {
        Assert.Equal("En bevakning har slagit till.", BackgroundNames.AlertTitle);
        Assert.DoesNotContain("{", BackgroundNames.AlertDetail, StringComparison.Ordinal);
        Assert.NotEmpty(CoinSound.Wave);
        Assert.Equal("RIFF"u8.ToArray(), CoinSound.Wave[..4]);
        Assert.Equal("WAVE"u8.ToArray(), CoinSound.Wave[8..12]);
    }

    private sealed class Channel(WatchAlerts way, bool fails = false) : IAlertChannel
    {
        public int Shown { get; private set; }

        public WatchAlerts Way => way;

        public void Show()
        {
            Shown++;
            if (fails)
            {
                throw new InvalidOperationException("No taskbar.");
            }
        }
    }
}

/// <summary>The runs: until nothing is left, and anew when the endpoints change.</summary>
public sealed class WatcherRunTests : IDisposable
{
    private readonly World _world = new();

    public void Dispose() => _world.Dispose();

    [Fact]
    public async Task A_run_ends_when_there_is_nothing_to_watch()
    {
        var run = new WatcherRun(_world.Background<WatcherLoop>(), null, (_, _) => Task.CompletedTask);

        Assert.Equal(RunEnd.Stopped, await run.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task A_run_starts_anew_when_the_endpoints_change_and_stops_when_asked()
    {
        await _world.WatchAsync();
        _world.WritePlan(Modules.Network.Contracts.ExitRoute.Vpn);
        var file = Path.Combine(_world.AppData, "sources.json");
        await _world.UseTestEndpointAsync();
        var rounds = 0;
        var run = new WatcherRun(_world.Background<WatcherLoop>(), file, (_, _) =>
        {
            if (++rounds == 2)
            {
                File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(1));
            }

            return Task.CompletedTask;
        });

        Assert.Equal(RunEnd.SourcesChanged, await run.RunAsync(CancellationToken.None));
        Assert.Equal(2, rounds);

        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();
        Assert.Equal(RunEnd.Stopped, await run.RunAsync(stop.Token));
    }
}
