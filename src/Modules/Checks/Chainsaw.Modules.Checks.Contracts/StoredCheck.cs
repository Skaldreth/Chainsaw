using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>A finding as it is sealed in the case, with the analysis that found it and the decisions about it.</summary>
/// <param name="Sequence">The record's number in the sealed chain <c>checks.results</c>.</param>
/// <param name="Analysis">The analysis and its version.</param>
/// <param name="AnalysisName">The analysis's name as the user reads it.</param>
/// <param name="Finding">What it found.</param>
/// <param name="JournalSequence">The journal entry of the run that sealed it: who ran it and when.</param>
/// <param name="RecordedUtc">When it was sealed.</param>
/// <param name="Decisions">The user's decisions about it in this version, oldest first; the latest per subject counts.</param>
public sealed record StoredCheck(
    long Sequence,
    HeuristicId Analysis,
    string AnalysisName,
    CheckFinding Finding,
    long JournalSequence,
    DateTimeOffset RecordedUtc,
    IReadOnlyList<CheckDecision> Decisions)
{
    /// <summary>Gives the decision in force about the finding as a whole, or about one of its candidates.</summary>
    /// <param name="candidateKey">The candidate's key, or <see langword="null"/> for the finding as a whole.</param>
    /// <returns>The latest decision, or <see langword="null"/> when none is made.</returns>
    public CheckDecision? DecisionFor(string? candidateKey) =>
        Decisions.LastOrDefault(decision => decision.CandidateKey == candidateKey);

    /// <summary>
    /// Gets how many decisions the suggestion still waits for: one per candidate that has none, or one for a
    /// suggestion without candidates; 0 for a check.
    /// </summary>
    public int Waiting
    {
        get
        {
            if (Finding.Kind != CheckKind.Suggestion)
            {
                return 0;
            }

            return Finding.Candidates.Count == 0
                ? (DecisionFor(null) is null ? 1 : 0)
                : Finding.Candidates.Count(candidate => DecisionFor(candidate.Key) is null);
        }
    }
}

/// <summary>
/// The user's decision about a suggestion or one of its candidates, sealed with who, when and an optional reason.
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain <c>checks.decisions</c>.</param>
/// <param name="Analysis">The analysis and version the decision holds for.</param>
/// <param name="FindingKey">The finding's key.</param>
/// <param name="CandidateKey">The candidate's key, or <see langword="null"/> for the suggestion as a whole.</param>
/// <param name="Verdict">Approved or dismissed.</param>
/// <param name="Reason">Why, or <see langword="null"/>; the reason is optional.</param>
/// <param name="DecidedBy">Who decided.</param>
/// <param name="DecidedUtc">When.</param>
/// <param name="JournalSequence">The journal entry that records it.</param>
public sealed record CheckDecision(
    long Sequence,
    HeuristicId Analysis,
    string FindingKey,
    string? CandidateKey,
    CheckVerdict Verdict,
    string? Reason,
    OperatorProfile DecidedBy,
    DateTimeOffset DecidedUtc,
    long JournalSequence);

/// <summary>What a run of the analyses gave.</summary>
/// <param name="Checks">Every finding of the run, with its seal and decisions, in the analyses' order.</param>
/// <param name="Sealed">How many findings were new or changed and were sealed by this run.</param>
/// <param name="Transactions">How many transactions the analyses ran on.</param>
/// <param name="RanUtc">When the run was made.</param>
/// <param name="Problems">Analyses that failed, in Swedish; empty when none did.</param>
public sealed record CheckRun(
    IReadOnlyList<StoredCheck> Checks,
    int Sealed,
    int Transactions,
    DateTimeOffset RanUtc,
    IReadOnlyList<string> Problems);

/// <summary>A decision to seal.</summary>
/// <param name="Analysis">The analysis and version of the suggestion.</param>
/// <param name="FindingKey">The finding's key.</param>
/// <param name="CandidateKey">The candidate's key, or <see langword="null"/> for the suggestion as a whole.</param>
/// <param name="Verdict">Approved or dismissed.</param>
/// <param name="Reason">Why, or <see langword="null"/>.</param>
public sealed record DecisionRequest(
    HeuristicId Analysis, string FindingKey, string? CandidateKey, CheckVerdict Verdict, string? Reason = null);
