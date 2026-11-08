namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>Why the clustering refuses a merge of the user's own.</summary>
public enum MergeRefusal
{
    /// <summary>The merge would join two addresses the user has split; the split is undone first.</summary>
    AcrossSplit,

    /// <summary>
    /// The merge would, with the clusters as they stand, stop a merge that holds now, so that addresses would leave a
    /// cluster without the user breaking them out.
    /// </summary>
    BreaksMerge,
}

/// <summary>
/// The clustering refuses a merge of the user's own (<c>user v1</c>), so that a merge never joins what the user split
/// and never silently breaks up what holds; nothing is stored.
/// </summary>
public sealed class MergeRefusedException : ArgumentException
{
    /// <summary>Creates the exception.</summary>
    public MergeRefusedException()
        : this(MergeRefusal.AcrossSplit)
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">The message.</param>
    public MergeRefusedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and its cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public MergeRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception for a reason.</summary>
    /// <param name="refusal">Why the merge is refused.</param>
    public MergeRefusedException(MergeRefusal refusal)
        : base(MessageOf(refusal)) => Refusal = refusal;

    /// <summary>Gets why the merge is refused.</summary>
    public MergeRefusal Refusal { get; }

    /// <summary>Gives the message of a reason.</summary>
    /// <param name="refusal">Why.</param>
    /// <returns>The message, in English, for logs; the shell writes its own Swedish text.</returns>
    private static string MessageOf(MergeRefusal refusal) => refusal == MergeRefusal.BreaksMerge
        ? "The merge would break up a merge that holds now; undo the split it collides with first."
        : "The addresses are split by the user; undo the split before merging them.";
}

/// <summary>
/// What undoing the user's merges would do, for the confirmation the user sees first (sketch AN1b): which addresses a
/// heuristic would merge again once the user's merges no longer come first.
/// </summary>
/// <param name="Merges">The merges that hold now and would be undone; one already undone is left out.</param>
/// <param name="Rejoined">
/// The addresses that a heuristic's link would merge again, each with that link; empty when nothing would.
/// </param>
/// <param name="Heuristics">The heuristics that would merge them again, by name and version, in order.</param>
public sealed record UndoMergePreview(
    IReadOnlyList<ClusterMerge> Merges,
    IReadOnlyList<ClusterMember> Rejoined,
    IReadOnlyList<HeuristicId> Heuristics)
{
    /// <summary>Gets whether any of the merges holds now and can be undone.</summary>
    public bool IsInForce => Merges.Count > 0;
}
