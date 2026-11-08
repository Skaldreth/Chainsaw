namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>What became of a transaction that has left the block the case first saw it in.</summary>
public enum BlockChangeKind
{
    /// <summary>It is in another block now: the chain was reorganised, and the transaction was confirmed again.</summary>
    Moved,

    /// <summary>It is unconfirmed again: the block it was in is no longer part of the chain, and none holds it yet.</summary>
    Unconfirmed,

    /// <summary>
    /// The source no longer knows it: another transaction spends the same money in the new chain, so this one is
    /// gone. Which transaction replaced it is not known from the answers alone.
    /// </summary>
    Gone,
}

/// <summary>Where a transaction stood in one sealed answer, and when the answer came.</summary>
/// <param name="Height">The block's height, or <see langword="null"/> while unconfirmed or when it is gone.</param>
/// <param name="Hash">The block's hash, or <see langword="null"/> while unconfirmed or when it is gone.</param>
/// <param name="Seen">When the answer came, in local time with its offset.</param>
/// <param name="Sequence">The sealed answer's number in the case, so that it can be found again (principle 1).</param>
public sealed record BlockSighting(long? Height, string? Hash, DateTimeOffset Seen, long Sequence)
{
    /// <summary>Gets the name of the endpoint the answer came from, or an empty text when it is not known.</summary>
    public string Endpoint { get; init; } = string.Empty;
}

/// <summary>
/// A transaction that a newer answer shows in another block than the case first saw it in, or not in a block at all:
/// most likely a reorganisation of the chain. It is read from the sealed answers only; both answers stay in the case
/// unchanged (principle 1), and this only says that they differ. That the block changed is an assessment of what the
/// source answered (principle 2): an answer that it is not in a block, or not known, counts only from an endpoint that
/// had come at least as far as the block the case first saw it in, since one that lags behind would say the same.
/// </summary>
/// <param name="Txid">The transaction.</param>
/// <param name="Kind">What became of it.</param>
/// <param name="Before">The first answer that had it in a block.</param>
/// <param name="Now">The newest answer, which differs from it.</param>
public sealed record BlockChange(Txid Txid, BlockChangeKind Kind, BlockSighting Before, BlockSighting Now);
