using Chainsaw.SharedKernel;

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
/// <param name="TransactionId">The transaction's id as its chain writes it: a txid, or <c>0x</c> and a hash.</param>
/// <param name="Kind">What became of it.</param>
/// <param name="Before">The first answer that had it in a block.</param>
/// <param name="Now">The newest answer, which differs from it.</param>
public sealed record BlockChange(string TransactionId, BlockChangeKind Kind, BlockSighting Before, BlockSighting Now)
{
    /// <summary>Creates the change of a Bitcoin transaction.</summary>
    /// <param name="txid">The transaction.</param>
    /// <param name="kind">What became of it.</param>
    /// <param name="before">The first answer that had it in a block.</param>
    /// <param name="now">The newest answer, which differs from it.</param>
    public BlockChange(Txid txid, BlockChangeKind kind, BlockSighting before, BlockSighting now)
        : this(txid?.Value ?? throw new ArgumentNullException(nameof(txid)), kind, before, now)
    {
    }

    /// <summary>Gets the chain the transaction is on: Bitcoin unless said otherwise.</summary>
    public Chain Chain { get; init; } = Chain.Bitcoin;

    /// <summary>
    /// Gets, for <see cref="BlockChangeKind.Gone"/>, whether the answers that the source does not know the
    /// transaction come from endpoints on at least two hosts, or from the only endpoint switched on for the chain; one
    /// endpoint's word alone, while others could be asked, is weaker (an endpoint can lie or be broken). Always
    /// <see langword="true"/> for the other kinds and for a source that cannot tell.
    /// </summary>
    public bool IsCorroborated { get; init; } = true;

    /// <summary>
    /// Gets, for <see cref="BlockChangeKind.Gone"/>, the hosts whose sealed answers said the source does not know the
    /// transaction since the case last saw it in a block, in lower case; empty for the other kinds.
    /// </summary>
    public IReadOnlyList<string> GoneHosts { get; init; } = [];

    /// <summary>
    /// Gets whether every sealed answer on the chain in the case came from one host, so that a lone word is all there
    /// can be: an end that rests on it says it rests on a single source.
    /// </summary>
    public bool IsSingleSource { get; init; }
}
