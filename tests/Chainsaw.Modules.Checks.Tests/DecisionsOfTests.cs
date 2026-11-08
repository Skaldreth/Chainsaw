using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using static Chainsaw.Modules.Checks.Tests.Synthetic;

namespace Chainsaw.Modules.Checks.Tests;

/// <summary>
/// The decisions in force that the analyses read (<c>CheckService.DecisionsOf</c>): the latest per candidate, in a
/// stable order, from several analyses.
/// </summary>
public sealed class DecisionsOfTests
{
    private static readonly OperatorProfile Who = new("Utredare", "Analytiker", "Enheten");

    private static long _sequence;

    private static CheckCandidate Candidate(string label) =>
        new($"{Txid(label).Value}:0", Wpkh(label), Txid(label), 0, new Satoshi(1_000), 0.9, "Skäl.");

    private static (ResultKey Key, SealedResult Result) Sealed(string analysis, string key, params string[] labels)
    {
        var finding = new CheckFinding(
            key, CheckKind.Suggestion, AreaSort.Obfuscation, "T", "S", "W", [Wpkh("anchor")], [], [],
            [.. labels.Select(Candidate)]);
        var content = FindingCodec.Encode(finding);
        return (new ResultKey(analysis, 1, key),
            new SealedResult(1, content, FindingCodec.Fingerprint(content), 1, DateTimeOffset.UnixEpoch));
    }

    private static CheckDecision Decision(ResultKey key, string? label, CheckVerdict verdict) => new(
        ++_sequence,
        new HeuristicId(key.Analysis, (int)key.Version),
        key.FindingKey,
        label is null ? null : $"{Txid(label).Value}:0",
        verdict,
        null,
        Who,
        DateTimeOffset.UnixEpoch,
        _sequence);

    [Fact]
    public void The_latest_decision_per_candidate_counts_and_both_lists_are_ordered()
    {
        var (parts, partsResult) = Sealed(MixerPartsCheck.Name, "deposit-b:0", "p1", "p2", "p3");
        var (outputs, outputsResult) = Sealed(MixerOutputsCheck.Name, "deposit-a:0", "o1", "o2");
        var (whole, wholeResult) = Sealed(MixerOutputsCheck.Name, "deposit-c:0", "w1");
        var results = new Dictionary<ResultKey, SealedResult>
        {
            [parts] = partsResult,
            [outputs] = outputsResult,
            [whole] = wholeResult,
        };
        var decisions = new Dictionary<ResultKey, List<CheckDecision>>
        {
            [parts] =
            [
                Decision(parts, "p2", CheckVerdict.Approved),
                Decision(parts, "p1", CheckVerdict.Approved),
                Decision(parts, "p3", CheckVerdict.Approved),
                Decision(parts, "p3", CheckVerdict.Withdrawn),
            ],
            [outputs] =
            [
                Decision(outputs, "o1", CheckVerdict.Approved),
                Decision(outputs, "o2", CheckVerdict.Dismissed),
            ],
            [whole] = [Decision(whole, null, CheckVerdict.Dismissed)],
        };

        var (approved, dismissed) = CheckService.DecisionsOf(results, decisions);

        Assert.Equal(
            [
                (MixerOutputsCheck.Name, Txid("o1")),
                .. new[] { "p1", "p2" }.Select(p => (MixerPartsCheck.Name, Txid(p)))
                    .OrderBy(item => item.Item2.Value, StringComparer.Ordinal),
            ],
            approved.Select(a => (a.Analysis.Name, a.Txid)));
        Assert.All(approved, a => Assert.Equal(0, a.Output));
        Assert.Equal(
            [("deposit-a:0", $"{Txid("o2").Value}:0"), ("deposit-c:0", (string?)null)],
            dismissed.Select(d => (d.FindingKey, d.CandidateKey)));
    }

    [Fact]
    public void Nothing_decided_gives_nothing()
    {
        var (approved, dismissed) = CheckService.DecisionsOf([], []);

        Assert.Empty(approved);
        Assert.Empty(dismissed);
    }
}
