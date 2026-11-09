namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// That a version of an analysis is drawn back: it proved to merge wrongly, so its conclusions stop counting in the
/// clusters and the findings, it no longer runs on the case's transactions, and the highest registered version of the
/// same analysis that is not drawn back runs and counts instead. Nothing is removed (principles 1 and 8); its
/// conclusions stay sealed, and the withdrawal is a sealed record of its own that can be taken back (sketch AN1b).
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain <c>clustering.withdrawals</c>.</param>
/// <param name="Heuristic">The analysis and the version drawn back.</param>
/// <param name="Reason">The user's reason, or <see langword="null"/>; the reason is optional.</param>
/// <param name="JournalSequence">The journal entry that says it.</param>
/// <param name="RecordedUtc">When it was sealed.</param>
public sealed record VersionWithdrawal(
    long Sequence, HeuristicId Heuristic, string? Reason, long JournalSequence, DateTimeOffset RecordedUtc)
{
    /// <summary>Gets the taking back of the withdrawal, or <see langword="null"/> while it is in force.</summary>
    public VersionWithdrawalUndo? Undo { get; init; }

    /// <summary>Gets whether the withdrawal is in force (not taken back).</summary>
    public bool IsInForce => Undo is null;
}

/// <summary>
/// That a withdrawal of a version was taken back, so that the version runs and counts again; the withdrawal stays
/// sealed as it was.
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain <c>clustering.withdrawal_undos</c>.</param>
/// <param name="WithdrawalSequence">The withdrawal's <see cref="VersionWithdrawal.Sequence"/>.</param>
/// <param name="Reason">The user's reason, or <see langword="null"/>.</param>
/// <param name="JournalSequence">The journal entry that says it.</param>
/// <param name="RecordedUtc">When it was sealed.</param>
public sealed record VersionWithdrawalUndo(
    long Sequence, long WithdrawalSequence, string? Reason, long JournalSequence, DateTimeOffset RecordedUtc);
