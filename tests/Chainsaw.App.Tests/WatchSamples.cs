using Chainsaw.App.ViewModels;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Tests;

/// <summary>
/// The Monitoring module in memory: watches, records and hits kept in lists as the module would seal them, the gate
/// the test sets, and every call recorded. Nothing is sealed and nothing goes on the network.
/// </summary>
internal sealed class TestWatchSource : IWatchSource
{
    public static readonly OperatorProfile Who = new("Testa Testsson", "Utredare", "Enheten");

    public static readonly DateTimeOffset At = new(2026, 11, 7, 10, 0, 0, TimeSpan.Zero);

    public List<Watch> Watches { get; } = [];

    public List<WatchRecord> Records { get; } = [];

    public List<WatchHit> Hits { get; } = [];

    public Dictionary<string, WatchChecked> Checked { get; } = [];

    public Dictionary<string, long> Seen { get; } = [];

    public WatchGate GateState { get; set; } = WatchGate.Open;

    public long? Height { get; set; } = 900_000;

    public List<(Chain Chain, long Height)> Checks { get; } = [];

    public int HeightsAsked { get; private set; }

    public List<(string Id, string? Reason)> Removed { get; } = [];

    public List<WatchFilter> Added { get; } = [];

    /// <summary>The hits the next check finds, sealed as they come.</summary>
    public List<WatchMatch> NextHits { get; } = [];

    /// <summary>Whether a check fails as if the case had been closed.</summary>
    public bool CaseClosed { get; set; }

    public Watch Add(
        WatchSubject subject, WatchFilter filter, long? start = 899_000, WatchState state = WatchState.Active)
    {
        var watch = new Watch(
            Guid.NewGuid().ToString("N"), subject, filter, state, start, Who, At, At);
        Watches.Add(watch);
        Records.Add(new WatchRecord(Records.Count + 1, WatchAction.Added, watch, null, Who, At));
        return watch;
    }

    public WatchHit Hit(Watch watch, WatchMatch match)
    {
        var hit = new WatchHit(Hits.Count + 1, watch.Id, watch.Chain, match, [7], 3, At);
        Hits.Add(hit);
        return hit;
    }

    public Task<WatchSnapshot> ReadAsync(ICase @case, CancellationToken cancellationToken) =>
        Task.FromResult(new WatchSnapshot(
            [.. Watches.Where(watch => watch.State != WatchState.Removed)], [.. Records], [.. Hits],
            new Dictionary<string, WatchChecked>(Checked), new Dictionary<string, long>(Seen)));

    public Task<Watch> AddAsync(ICase @case, WatchSubject subject, WatchFilter filter)
    {
        Added.Add(filter);
        return Task.FromResult(Add(subject, filter, start: null));
    }

    public Task<Watch?> ChangeAsync(ICase @case, string id, WatchFilter filter) =>
        Task.FromResult(Replace(id, watch => watch with { Filter = filter }, WatchAction.Changed));

    public Task<Watch?> SetPausedAsync(ICase @case, string id, bool paused) => Task.FromResult(Replace(
        id, watch => watch with { State = paused ? WatchState.Paused : WatchState.Active },
        paused ? WatchAction.Paused : WatchAction.Resumed));

    public Task<bool> RemoveAsync(ICase @case, string id, string? reason)
    {
        Removed.Add((id, reason));
        return Task.FromResult(Replace(id, watch => watch with { State = WatchState.Removed }, WatchAction.Removed)
            is not null);
    }

    public Task MarkSeenAsync(ICase @case, string id, long through)
    {
        Seen[id] = Math.Max(Seen.GetValueOrDefault(id), through);
        return Task.CompletedTask;
    }

    public WatchGate Gate(Chain chain) => GateState;

    public Task<long?> GetHeightAsync(ICase @case, Chain chain, CancellationToken cancellationToken)
    {
        HeightsAsked++;
        return Task.FromResult(Height);
    }

    public Task<WatchRun> CheckAsync(ICase @case, Chain chain, long height, CancellationToken cancellationToken)
    {
        if (CaseClosed)
        {
            throw new CaseException(CaseError.Closed);
        }

        Checks.Add((chain, height));
        for (var index = 0; index < Watches.Count; index++)
        {
            if (Watches[index].StartHeight is null)
            {
                Watches[index] = Watches[index] with { StartHeight = height };
            }
        }

        var hits = Watches.Count == 0 ? [] : NextHits.Select(match => Hit(Watches[0], match)).ToList();
        NextHits.Clear();
        return Task.FromResult(new WatchRun(chain, WatchGate.Open, height,
            [new WatchOutcome(Watches.FirstOrDefault()?.Id ?? "-", WatchFeedStatus.Read, hits, [])], At));
    }

    private Watch? Replace(string id, Func<Watch, Watch> change, WatchAction action)
    {
        var index = Watches.FindIndex(watch => watch.Id == id && watch.State != WatchState.Removed);
        if (index < 0)
        {
            return null;
        }

        Watches[index] = change(Watches[index]);
        Records.Add(new WatchRecord(Records.Count + 1, action, Watches[index], null, Who, At));
        return Watches[index];
    }
}

/// <summary>Made-up watches on the sample graph's addresses.</summary>
internal static class WatchSamples
{
    public static ChainAddress On(Chainsaw.Modules.Chains.Contracts.BitcoinAddress address) =>
        new(Chain.Bitcoin, address.Value);

    public static WatchSubject Subject(Chainsaw.Modules.Chains.Contracts.BitcoinAddress address, string name = "V") =>
        new(WatchSubjectKind.Address, On(address), name);

    public static WatchFilter Filter(
        WatchDirection direction = WatchDirection.Both,
        IReadOnlyList<ChainAddress>? receivers = null,
        IReadOnlyList<ChainAddress>? senders = null) =>
        new(Chain.Bitcoin, direction, receivers ?? [], senders ?? [], null, null);

    public static WatchMatch Out(ChainAddress to, decimal bitcoin, long height = 900_010) => new(
        "aa" + height, new WatchBlock(height, new string('b', 64), TestWatchSource.At), WatchDirection.Out,
        GraphSamples.Btc(bitcoin), [to]);

    /// <summary>A board over the source, with a case open.</summary>
    public static (CaseSession Session, WatchBoard Board) Board(TestWatchSource source, bool open = true)
    {
        var session = new CaseSession();
        if (open)
        {
            session.Open(new TestCase("2026-0026"));
        }

        return (session, new WatchBoard(session, source, new InlineUi()));
    }
}
