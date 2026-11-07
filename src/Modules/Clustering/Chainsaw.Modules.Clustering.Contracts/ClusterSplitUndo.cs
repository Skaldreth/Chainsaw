namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// The user's undoing of a split (sketch AN1b): a sealed record of its own in the chain
/// <c>clustering.split_undos</c>, so that the split stays readable but no longer counts, and the heuristics may merge
/// the two addresses again.
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain.</param>
/// <param name="SplitSequence">The undone split's <see cref="ClusterSplit.Sequence"/>.</param>
/// <param name="Reason">Why, or <see langword="null"/>; the reason is optional.</param>
/// <param name="JournalSequence">The journal entry: who undid it and when.</param>
/// <param name="RecordedUtc">When it was undone.</param>
public sealed record ClusterSplitUndo(
    long Sequence, long SplitSequence, string? Reason, long JournalSequence, DateTimeOffset RecordedUtc);

/// <summary>A split in the case's history, with its undoing when it has been undone.</summary>
/// <param name="Split">The split.</param>
/// <param name="Undo">Its undoing, or <see langword="null"/> while it holds.</param>
public sealed record SplitHistoryEntry(ClusterSplit Split, ClusterSplitUndo? Undo)
{
    /// <summary>Gets whether the split is undone and no longer holds.</summary>
    public bool IsUndone => Undo is not null;
}

/// <summary>
/// What undoing splits would do, for the confirmation the user sees first (the user's decision 2026-11-07): which
/// addresses join the cluster again, and through which link.
/// </summary>
/// <param name="Splits">The splits that hold now and would be undone; one already undone is left out.</param>
/// <param name="Rejoined">
/// The addresses that would join the cluster again, each with the link (heuristic and transaction) that brings it in;
/// empty when nothing would be merged, for example while another split holds them apart or no heuristic has merged
/// them.
/// </param>
/// <param name="Heuristics">The heuristics that would merge them again, by name and version, in order.</param>
public sealed record UndoSplitPreview(
    IReadOnlyList<ClusterSplit> Splits,
    IReadOnlyList<ClusterMember> Rejoined,
    IReadOnlyList<HeuristicId> Heuristics)
{
    /// <summary>Gets whether any of the splits holds now and can be undone.</summary>
    public bool IsInForce => Splits.Count > 0;
}
