using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// The merges the user makes: the analysis they count as in the clusters, <c>user v1</c>, so that a user's merge is
/// explained, broken out and undone like the heuristics' merges (sketch AN1b), and is always the user's judgement,
/// never a fact (principle 2).
/// </summary>
public static class UserMerges
{
    /// <summary>The name the user's merges carry in the clusters and in a split that breaks one.</summary>
    public const string Name = "user";

    /// <summary>The longest text a merge's grounds or reason may have.</summary>
    public const int MaximumTextLength = 1_000;

    /// <summary>The most addresses one merge may hold.</summary>
    public const int MaximumAddresses = 1_000;

    /// <summary>Gets the name and version the user's merges are explained with: <c>user v1</c>.</summary>
    public static HeuristicId Id { get; } = new(Name, 1);

    /// <summary>
    /// Writes why a merge holds, as the clusters explain it: that the user approved it, what the proposal said and,
    /// when given, the user's own reason.
    /// </summary>
    /// <param name="merge">The merge.</param>
    /// <returns>The reason in plain Swedish.</returns>
    public static string Explain(ClusterMerge merge)
    {
        ArgumentNullException.ThrowIfNull(merge);
        var text = string.Format(
            CultureInfo.InvariantCulture,
            "Användaren godkände förslaget från {0}: {1}",
            merge.Proposal,
            merge.Grounds);
        return merge.Reason is { } reason ? $"{text} Skäl: {reason}" : text;
    }
}

/// <summary>
/// The user's decision that some addresses have the same owner (<c>user v1</c>), sealed in the case with who, when and
/// an optional reason. The clusters respect it: a heuristic never takes it back, and only the user can, by breaking it
/// out (a split that names <see cref="UserMerges.Name"/>) or by undoing it.
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain <c>clustering.merges</c>.</param>
/// <param name="Addresses">The addresses, at least two and all different.</param>
/// <param name="Txid">The transaction the merge rests on, so that it can be checked against the chain.</param>
/// <param name="Proposal">The analysis whose proposal the user approved, with its version.</param>
/// <param name="Grounds">What the proposal said, in plain Swedish.</param>
/// <param name="Reason">The user's own reason, or <see langword="null"/>; it is optional.</param>
/// <param name="Origin">The sealed record the merge came from, for example an approved suggestion, or none.</param>
/// <param name="JournalSequence">The journal entry of the merge: who decided it and when.</param>
/// <param name="RecordedUtc">When it was decided.</param>
public sealed record ClusterMerge(
    long Sequence,
    IReadOnlyList<BitcoinAddress> Addresses,
    Txid Txid,
    HeuristicId Proposal,
    string Grounds,
    string? Reason,
    MergeOrigin? Origin,
    long JournalSequence,
    DateTimeOffset RecordedUtc);

/// <summary>What the user asks for when merging addresses.</summary>
/// <param name="Addresses">The addresses, at least two and all different.</param>
/// <param name="Txid">The transaction the merge rests on.</param>
/// <param name="Proposal">The analysis whose proposal the user approves, with its version.</param>
/// <param name="Grounds">What the proposal said; not empty, at most 1 000 characters.</param>
public sealed record MergeRequest(
    IReadOnlyList<BitcoinAddress> Addresses, Txid Txid, HeuristicId Proposal, string Grounds);

/// <summary>
/// The sealed record in another module that a merge came from, so that the module can find its merges again (for
/// example the decision that approved a suggestion in <c>checks.decisions</c>).
/// </summary>
/// <param name="Chain">The sealed chain's name.</param>
/// <param name="Sequence">The record's number in the chain.</param>
public sealed record MergeOrigin(string Chain, long Sequence);

/// <summary>
/// The user's undoing of a merge: a sealed record of its own in the chain <c>clustering.merge_undos</c>, so that the
/// merge stays readable but no longer counts.
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain.</param>
/// <param name="MergeSequence">The undone merge's <see cref="ClusterMerge.Sequence"/>.</param>
/// <param name="Reason">Why, or <see langword="null"/>; the reason is optional.</param>
/// <param name="JournalSequence">The journal entry: who undid it and when.</param>
/// <param name="RecordedUtc">When it was undone.</param>
public sealed record ClusterMergeUndo(
    long Sequence, long MergeSequence, string? Reason, long JournalSequence, DateTimeOffset RecordedUtc);

/// <summary>A merge in the case's history, with its undoing when it has been undone.</summary>
/// <param name="Merge">The merge.</param>
/// <param name="Undo">Its undoing, or <see langword="null"/> while it holds.</param>
public sealed record MergeHistoryEntry(ClusterMerge Merge, ClusterMergeUndo? Undo)
{
    /// <summary>Gets whether the merge is undone and no longer holds.</summary>
    public bool IsUndone => Undo is not null;
}
