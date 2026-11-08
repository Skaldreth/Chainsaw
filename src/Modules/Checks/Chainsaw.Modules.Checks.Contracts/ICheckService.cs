using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>
/// The checks and suggestions (CLAUDE.md, <em>Förvirring analyseras</em>, sketch KF1a): runs every analysis over the
/// graph the case holds, seals what is new with what it rests on, seals the user's decisions about suggestions, and
/// keeps the user's own areas (sketch KF1b). Everything is a judgement, never a fact (principle 2).
/// </summary>
/// <remarks>The methods are synchronous and read or write the case; call them off the UI thread (principle 6).</remarks>
public interface ICheckService
{
    /// <summary>Gets the analyses that run on Bitcoin, in the order they run.</summary>
    IReadOnlyList<HeuristicId> Analyses { get; }

    /// <summary>
    /// Runs every analysis over the graph and seals each finding that is new or changed since it was last sealed, with
    /// one line in the journal for the run; a run that finds nothing new writes nothing. A dismissed suggestion keeps
    /// its decision as long as the analysis's version is the same.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="graph">The graph, read from the case.</param>
    /// <returns>Every finding of the run, with its seal and decisions.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.NoOperatorProfile"/> when something new would be sealed, or <see cref="CaseError.Closed"/>.
    /// </exception>
    CheckRun Run(ICase @case, TraceGraph graph);

    /// <summary>
    /// Seals the user's decision about a suggestion or one of its candidates, with a line in the journal. The latest
    /// decision about the same subject counts; the earlier ones stay sealed. Approving a wallet
    /// (<see cref="CheckAnalysisNames.Wallets"/>) also merges its addresses in the clustering as the user's judgement,
    /// and taking that approval back undoes the merge, in the same transaction.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="request">The decision.</param>
    /// <returns>The sealed decision.</returns>
    /// <exception cref="ArgumentException">
    /// The suggestion or candidate is not sealed in the case, it is a check, the reason is too long, the suggestion is
    /// settled already or the candidate has a decision in force (an approve or a dismiss), a take-back does not
    /// name the decision in force, or the clustering refuses an approved wallet's merge.
    /// </exception>
    CheckDecision Decide(ICase @case, DecisionRequest request);

    /// <summary>
    /// Seals several decisions in one transaction, in order, each checked as <see cref="Decide"/> checks it against
    /// what the ones before it sealed; when one is refused, none is sealed.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="requests">The decisions.</param>
    /// <returns>The sealed decisions, in order.</returns>
    /// <exception cref="ArgumentException">One of the decisions is refused, as <see cref="Decide"/> says.</exception>
    IReadOnlyList<CheckDecision> DecideAll(ICase @case, IReadOnlyList<DecisionRequest> requests);

    /// <summary>
    /// Undoes the user's merges from the node's history (sketch AN1b) in one transaction: a merge that approving a
    /// wallet made is undone by taking that approval back (sealed, with the reason), so that the suggestion waits
    /// again; any other merge is undone in the clustering alone.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="mergeSequences">The merges' sequences in the clustering.</param>
    /// <param name="reason">Why, or <see langword="null"/>; the reason is optional.</param>
    /// <returns>
    /// How many merges were undone: those named that were in force, and the other merges of the same approval, which
    /// is taken back as a whole.
    /// </returns>
    /// <exception cref="ArgumentException">The reason is too long.</exception>
    int TakeBackMerges(ICase @case, IReadOnlyCollection<long> mergeSequences, string? reason);

    /// <summary>Reads the own areas in force, oldest first.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The areas.</returns>
    IReadOnlyList<OwnArea> GetAreas(ICase @case);

    /// <summary>Seals a new own area, with who and when and a line in the journal.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="draft">The area.</param>
    /// <returns>The sealed area.</returns>
    /// <exception cref="ArgumentException">
    /// The name is empty or too long, the note too long, or the members wrong.
    /// </exception>
    OwnArea AddArea(ICase @case, OwnAreaDraft draft);

    /// <summary>Renames an own area, sealed with a line in the journal; the old name stays sealed.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The area's id.</param>
    /// <param name="name">The new name.</param>
    /// <returns>The renamed area, or <see langword="null"/> when there is no such area in force.</returns>
    OwnArea? RenameArea(ICase @case, string id, string name);

    /// <summary>
    /// Removes an own area, sealed with a line in the journal; the area stays sealed in the case, marked as removed.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The area's id.</param>
    /// <param name="reason">Why, or <see langword="null"/>; the reason is optional.</param>
    /// <returns><see langword="false"/> when there is no such area in force.</returns>
    bool RemoveArea(ICase @case, string id, string? reason);
}

/// <summary>The journal codes the Checks module writes.</summary>
public static class CheckJournalKinds
{
    /// <summary>The analyses ran and sealed new or changed findings.</summary>
    public const string Ran = "checks.ran";

    /// <summary>The user approved or dismissed a suggestion.</summary>
    public const string Decided = "checks.decided";

    /// <summary>The user drew an own area.</summary>
    public const string AreaAdded = "checks.area-added";

    /// <summary>The user renamed an own area.</summary>
    public const string AreaRenamed = "checks.area-renamed";

    /// <summary>The user removed an own area.</summary>
    public const string AreaRemoved = "checks.area-removed";
}
