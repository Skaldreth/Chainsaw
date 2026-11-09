namespace Chainsaw.Modules.Cases;

/// <summary>How far a chain had come when this computer last saved the case.</summary>
/// <param name="Chain">The chain's name.</param>
/// <param name="Sequence">The sequence number of the chain's last record then.</param>
/// <param name="Hash">That record's hash.</param>
internal sealed record ChainAnchor(string Chain, long Sequence, byte[] Hash)
{
    /// <summary>
    /// Gets the records of the chain that were added on another computer: those an opening found beyond the anchor,
    /// as ranges of sequence numbers (both ends included), oldest first. They are kept with the anchor, since the
    /// anchor moves past them at the next save.
    /// </summary>
    public IReadOnlyList<SequenceRange> Elsewhere { get; init; } = [];
}

/// <summary>A run of records in a chain, by sequence number.</summary>
/// <param name="From">The first record's sequence number.</param>
/// <param name="To">The last record's sequence number, at least <paramref name="From"/>.</param>
internal readonly record struct SequenceRange(long From, long To)
{
    /// <summary>Gives whether a record lies in the run.</summary>
    /// <param name="sequence">The record's sequence number.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Contains(long sequence) => sequence >= From && sequence <= To;
}
