using Chainsaw.App.Graph;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.App.Tests.GraphSamples;

namespace Chainsaw.App.Tests;

/// <summary>
/// Made-up checks on the made-up graph of <see cref="GraphSamples"/>: V is a mixer whose likely outputs are U and E,
/// a CoinJoin around K and E, and a wallet the clustering proposes for W and V.
/// </summary>
internal static class CheckSamples
{
    public static readonly HeuristicId MixerId = new("mixer-outputs", 1);

    public static readonly OperatorProfile Who = new("Utredare", "Analytiker", "Enheten");

    public static CheckCandidate Candidate(BitcoinAddress address, int tx, double score) =>
        new($"{Tx[tx].Value}:0", address, Tx[tx], 0, new Satoshi(1000), score, "Belopp och tid.");

    public static StoredCheck Mixer(params CheckDecision[] decisions) => new(
        1,
        MixerId,
        "Mixerutgångar",
        new CheckFinding(
            "deposit:0",
            CheckKind.Suggestion,
            AreaSort.Obfuscation,
            "Troliga utgångar ur mixern",
            "1,0000 in",
            "Mixerutgångar v1 letar … aldrig ett faktum.",
            [V, U, E],
            [],
            [new CheckBasis(Tx[0], Block(100)), new CheckBasis(Tx[1], Block(101)), new CheckBasis(Tx[2], Block(102))],
            [Candidate(U, 1, 0.8), Candidate(E, 2, 0.5)]),
        5,
        new DateTimeOffset(2026, 11, 7, 13, 2, 0, TimeSpan.Zero),
        decisions);

    public static readonly HeuristicId SplitId = new("split-amounts", 1);

    /// <summary>A second suggestion, without candidates: E split its money into many small amounts.</summary>
    public static StoredCheck Split() => new(
        6,
        SplitId,
        "Uppdelning",
        new CheckFinding(
            "split:0", CheckKind.Suggestion, AreaSort.Obfuscation, "Uppdelning i små belopp", "14 ×",
            "Uppdelning v1 … aldrig ett faktum.", [E], [], [new CheckBasis(Tx[3], Block(103))], []),
        5,
        new DateTimeOffset(2026, 11, 7, 14, 40, 0, TimeSpan.Zero),
        []);

    public static StoredCheck CoinJoin() => new(
        2,
        new HeuristicId("coinjoin-area", 1),
        "CoinJoin",
        new CheckFinding(
            Tx[2].Value, CheckKind.Check, AreaSort.Obfuscation, "CoinJoin, Wasabi", "5 × 0,1", "Klustringens …",
            [K1, E], [], [new CheckBasis(Tx[2], Block(102))], []),
        3,
        new DateTimeOffset(2026, 11, 7, 13, 2, 0, TimeSpan.Zero),
        []);

    public static StoredCheck Wallet() => new(
        4,
        new HeuristicId("wallet-proposals", 1),
        "Plånböcker",
        new CheckFinding(
            W.Value, CheckKind.Check, AreaSort.Wallet, "Samma plånbok troligt", "change-address v1", "Klustringen …",
            [W, V], [], [], []),
        3,
        new DateTimeOffset(2026, 11, 7, 13, 2, 0, TimeSpan.Zero),
        []);

    public static CheckDecision Decision(string candidate, CheckVerdict verdict) =>
        new(1, MixerId, "deposit:0", candidate, verdict, null, Who, DateTimeOffset.UnixEpoch, 9);

    public static OwnArea Own(string name, params BitcoinAddress[] members) => new(
        "own-1", name, AreaSort.Own, AreaTint.Green, true, "Samma tid.",
        [.. members.Select(CheckRules.MemberOf)], Who, DateTimeOffset.UnixEpoch, 1);
}

/// <summary>A Checks module in memory: runs give what the test says, and what is stored is remembered.</summary>
internal sealed class TestCheckSource : ICheckSource
{
    public List<StoredCheck> Checks { get; } = [CheckSamples.Mixer(), CheckSamples.CoinJoin(), CheckSamples.Wallet()];

    public List<OwnArea> Areas { get; } = [];

    public List<DecisionRequest> Decisions { get; } = [];

    public List<OwnAreaDraft> Added { get; } = [];

    public List<(string Id, string Name)> Renamed { get; } = [];

    public List<(string Id, string? Reason)> Removed { get; } = [];

    public Exception? RunFailure { get; set; }

    /// <summary>Holds the next decision until the test lets it go.</summary>
    public TaskCompletionSource? HoldDecision { get; set; }

    /// <summary>Holds the next run until the test lets it go.</summary>
    public TaskCompletionSource? HoldRun { get; set; }

    public bool RenameFindsNothing { get; set; }

    public int Runs { get; private set; }

    public async Task<CheckRun> RunAsync(ICase @case, TraceGraph graph, CancellationToken cancellationToken)
    {
        Runs++;
        var checks = Checks.ToList();
        if (HoldRun is { } hold)
        {
            HoldRun = null;
            await hold.Task;
        }

        return RunFailure is { } failure
            ? throw failure
            : new CheckRun(
                checks, 0, graph.Transactions.Count, new DateTimeOffset(2026, 11, 7, 13, 2, 0, TimeSpan.Zero), []);
    }

    public async Task<CheckDecision> DecideAsync(ICase @case, DecisionRequest request)
    {
        Decisions.Add(request);
        if (HoldDecision is { } hold)
        {
            HoldDecision = null;
            await hold.Task;
        }

        var decision = new CheckDecision(
            Decisions.Count, request.Analysis, request.FindingKey, request.CandidateKey, request.Verdict, request.Reason,
            CheckSamples.Who, DateTimeOffset.UnixEpoch, 10);
        var index = Checks.FindIndex(check => check.Finding.Key == request.FindingKey);
        Checks[index] = Checks[index] with { Decisions = [.. Checks[index].Decisions, decision] };
        return decision;
    }

    public Task<IReadOnlyList<OwnArea>> GetAreasAsync(ICase @case, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OwnArea>>([.. Areas]);

    public Task<OwnArea> AddAreaAsync(ICase @case, OwnAreaDraft draft)
    {
        Added.Add(draft);
        var area = new OwnArea(
            "own-" + Added.Count, draft.Name, draft.Sort, draft.Tint, draft.SameActor, draft.Note, draft.Members,
            CheckSamples.Who, DateTimeOffset.UnixEpoch, Added.Count);
        Areas.Add(area);
        return Task.FromResult(area);
    }

    public Task<OwnArea?> RenameAreaAsync(ICase @case, string id, string name)
    {
        Renamed.Add((id, name));
        if (RenameFindsNothing)
        {
            return Task.FromResult<OwnArea?>(null);
        }

        var index = Areas.FindIndex(area => area.Id == id);
        Areas[index] = Areas[index] with { Name = name };
        return Task.FromResult<OwnArea?>(Areas[index]);
    }

    public Task<bool> RemoveAreaAsync(ICase @case, string id, string? reason)
    {
        Removed.Add((id, reason));
        return Task.FromResult(Areas.RemoveAll(area => area.Id == id) > 0);
    }
}
