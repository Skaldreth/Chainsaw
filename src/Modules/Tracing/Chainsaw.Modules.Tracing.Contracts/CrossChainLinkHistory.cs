namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// That a link to another chain was taken back (sketch AN1b): a sealed record of its own, so that the link stays
/// readable but no longer counts.
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain <c>tracing.cross_chain</c>.</param>
/// <param name="Reason">Why, or <see langword="null"/>; the reason is optional.</param>
/// <param name="JournalSequence">The journal entry: who took it back and when.</param>
/// <param name="RecordedUtc">When it was taken back.</param>
public sealed record CrossChainUnlink(long Sequence, string? Reason, long JournalSequence, DateTimeOffset RecordedUtc);

/// <summary>A link to another chain in the case's history, with its taking back when it has been taken back.</summary>
/// <param name="Link">The link.</param>
/// <param name="Unlink">Its taking back, or <see langword="null"/> while it is in force.</param>
public sealed record CrossChainLinkHistoryEntry(CrossChainLink Link, CrossChainUnlink? Unlink)
{
    /// <summary>Gets whether the link is taken back and no longer counts.</summary>
    public bool IsTakenBack => Unlink is not null;
}
