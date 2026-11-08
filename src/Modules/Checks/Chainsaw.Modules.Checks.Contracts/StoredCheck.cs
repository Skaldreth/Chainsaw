using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>
/// A finding as it is sealed in the case, with the analysis that found it and the decisions about it.
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain <c>checks.results</c>.</param>
/// <param name="Analysis">The analysis and its version.</param>
/// <param name="AnalysisName">The analysis's name as the user reads it.</param>
/// <param name="Finding">What it found.</param>
/// <param name="JournalSequence">The journal entry of the run that sealed it: who ran it and when.</param>
/// <param name="RecordedUtc">When it was sealed.</param>
/// <param name="Decisions">
/// The user's decisions about it in this version, oldest first; the latest per subject counts.
/// </param>
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
    /// <returns>
    /// The latest decision, or <see langword="null"/> when none is made or the latest was taken back
    /// (<see cref="CheckVerdict.Withdrawn"/>).
    /// </returns>
    public CheckDecision? DecisionFor(string? candidateKey) =>
        Decisions.LastOrDefault(decision => decision.CandidateKey == candidateKey) is { } latest
        && latest.Verdict != CheckVerdict.Withdrawn
            ? latest
            : null;

    /// <summary>
    /// Gets whether the user has settled the suggestion (sketch KF2b): approved one of its candidates, dismissed it
    /// as a whole, or decided every candidate; a suggestion without candidates is settled by a decision about it.
    /// Always <see langword="false"/> for a check, which waits for nothing.
    /// </summary>
    public bool IsSettled
    {
        get
        {
            if (Finding.Kind != CheckKind.Suggestion)
            {
                return false;
            }

            return DecisionFor(null) is not null
                || Finding.Candidates.Any(c => DecisionFor(c.Key)?.Verdict == CheckVerdict.Approved)
                || (Finding.Candidates.Count > 0 && Finding.Candidates.All(c => DecisionFor(c.Key) is not null));
        }
    }

    /// <summary>
    /// Gets how many candidates the suggestion still waits for while it is not settled (one for a suggestion without
    /// candidates); 0 for a check or a settled suggestion.
    /// </summary>
    public int Waiting
    {
        get
        {
            if (Finding.Kind != CheckKind.Suggestion || IsSettled)
            {
                return 0;
            }

            return Finding.Candidates.Count == 0
                ? 1
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
/// <param name="Verdict">Approved, dismissed or taken back.</param>
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
/// <param name="Verdict">Approved, dismissed or taken back.</param>
/// <param name="Reason">Why, or <see langword="null"/>.</param>
/// <param name="Undoes">
/// For <see cref="CheckVerdict.Withdrawn"/>: the sequence of the decision it takes back, which must be the one in
/// force,
/// so that an old <em>Ångra</em> never takes back a newer decision; <see langword="null"/> otherwise.
/// </param>
public sealed record DecisionRequest(
    HeuristicId Analysis,
    string FindingKey,
    string? CandidateKey,
    CheckVerdict Verdict,
    string? Reason = null,
    long? Undoes = null)
{
    /// <summary>Gives the request that takes a sealed decision back.</summary>
    /// <param name="decision">The decision in force.</param>
    /// <returns>The request.</returns>
    public static DecisionRequest TakeBack(CheckDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);
        return new DecisionRequest(
            decision.Analysis,
            decision.FindingKey,
            decision.CandidateKey,
            CheckVerdict.Withdrawn,
            null,
            decision.Sequence);
    }
}
