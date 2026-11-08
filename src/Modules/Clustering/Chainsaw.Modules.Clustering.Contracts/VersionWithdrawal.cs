namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// That a version of an analysis is drawn back: it proved to merge wrongly, so its conclusions stop counting in the
/// clusters and the findings, and the highest version of the same analysis that is not drawn back counts instead.
/// Nothing is removed (principles 1 and 8); its conclusions stay sealed, and the withdrawal is a sealed record of its
/// own.
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain <c>clustering.withdrawals</c>.</param>
/// <param name="Heuristic">The analysis and the version drawn back.</param>
/// <param name="Reason">The user's reason, or <see langword="null"/>; the reason is optional.</param>
/// <param name="JournalSequence">The journal entry that says it.</param>
/// <param name="RecordedUtc">When it was sealed.</param>
public sealed record VersionWithdrawal(
    long Sequence, HeuristicId Heuristic, string? Reason, long JournalSequence, DateTimeOffset RecordedUtc);
