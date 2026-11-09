using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains;

/// <summary>One sealed answer about a transaction: where it put it, and from which endpoint.</summary>
/// <param name="Sighting">Where the answer put the transaction, and when it came.</param>
/// <param name="EndpointId">The endpoint's id as sealed.</param>
/// <param name="IsGone">Whether the answer was that the source does not know the transaction.</param>
internal readonly record struct BlockAnswer(BlockSighting Sighting, string EndpointId, bool IsGone)
{
    /// <summary>Gets whether the answer had the transaction in a block.</summary>
    public bool InBlock => Sighting.Height is not null || Sighting.Hash is not null;
}

/// <summary>
/// The one rule by which every chain's sealed answers about a transaction are compared (GR9a), so that Bitcoin and
/// Ethereum say the same thing for the same answers. The first answer that had the transaction in a block is compared
/// with the newest: another block means that the chain was most likely reorganised. An answer that the transaction is
/// in no block, or not known, says the same only when it is confirmed (finding I1 of the second review): two such
/// answers in a row, after the last that had it in a block, from different endpoints or with at least one new block
/// between them, each from an endpoint that had come as far as the block. A public endpoint has several nodes behind
/// one address, and one of them can lag behind the second after a new block. A transaction that no answer ever had in a
/// block is compared by <see cref="CompareMempool"/> instead. Both answers stay in the case (principle 1); the result
/// is an assessment of what the sources answered (principle 2).
/// </summary>
internal static class BlockChangeRule
{
    /// <summary>Compares a transaction's sealed answers.</summary>
    /// <param name="answers">Every sealed answer about it, in any order.</param>
    /// <param name="store">The chain's sealed answers, for the heights the endpoints and the case had reached.</param>
    /// <param name="reader">The case's reader.</param>
    /// <returns>
    /// What became of it, the first answer in a block and the newest, or <see langword="null"/> when its block is the
    /// same or a change is not confirmed.
    /// </returns>
    public static (BlockChangeKind Kind, BlockSighting Before, BlockSighting Now)? Compare(
        IEnumerable<BlockAnswer> answers, RawResponseStore store, ICaseReader reader)
    {
        var ordered = answers.OrderBy(answer => answer.Sighting.Sequence).ToList();
        var firstInBlock = ordered.FindIndex(answer => answer.InBlock);
        if (firstInBlock < 0)
        {
            return null;
        }

        var before = ordered[firstInBlock].Sighting;
        var after = ordered.Where(answer => answer.Sighting.Sequence > before.Sequence).ToList();
        if (after.Count == 0)
        {
            return null;
        }

        var newest = after[^1];
        if (newest.InBlock)
        {
            return SameBlock(before, newest.Sighting) ? null : (BlockChangeKind.Moved, before, newest.Sighting);
        }

        var lastInBlock = after.FindLastIndex(answer => answer.InBlock);
        var negatives = after.Skip(lastInBlock + 1)
            .Where(answer => Reached(store, reader, answer, before))
            .ToList();
        if (negatives.Count < 2 || !Confirms(store, reader, negatives[^2], negatives[^1]))
        {
            return null;
        }

        var kind = newest.IsGone ? BlockChangeKind.Gone : BlockChangeKind.Unconfirmed;
        return (kind, before, newest.Sighting);
    }

    /// <summary>
    /// How long after the last answer that had a transaction unconfirmed the source must still not know it before it
    /// counts as gone from the mempool (<see cref="CompareMempool"/>): an hour is six of Bitcoin's blocks and some 300
    /// of Ethereum's, so that a transaction that was valid and paid its way would normally have been in one, while a
    /// public endpoint whose nodes have not all seen a new transaction yet is given time.
    /// </summary>
    public static readonly TimeSpan MempoolGrace = TimeSpan.FromHours(1);

    /// <summary>
    /// Compares the sealed answers about a transaction that no answer ever had in a block: it counts as gone from the
    /// mempool when, after the last answer that had it unconfirmed,
    /// <list type="bullet">
    /// <item>two answers in a row said the source does not know it, confirming each other as in
    /// <see cref="Compare"/> (another endpoint, or a new block between them), each from an endpoint that had come at
    /// least as far as the case knew when it last saw the transaction;</item>
    /// <item>the newest of them came at least <see cref="MempoolGrace"/> after that answer;</item>
    /// <item>and the case knew of at least one block more when it came, so that the chain went on without it.</item>
    /// </list>
    /// A transaction that a block ever held is left to <see cref="Compare"/>. The rule is an assessment of what the
    /// sources answered (principle 2); every answer stays in the case (principle 1).
    /// </summary>
    /// <param name="answers">Every sealed answer about it, in any order.</param>
    /// <param name="store">The chain's sealed answers, for the heights the endpoints and the case had reached.</param>
    /// <param name="reader">The case's reader.</param>
    /// <returns>
    /// The last answer that had it unconfirmed and the newest that says it is not known, or <see langword="null"/>
    /// when the rule does not hold.
    /// </returns>
    public static (BlockSighting Before, BlockSighting Now)? CompareMempool(
        IEnumerable<BlockAnswer> answers, RawResponseStore store, ICaseReader reader)
    {
        var ordered = answers.OrderBy(answer => answer.Sighting.Sequence).ToList();
        var seenAt = ordered.FindLastIndex(answer => !answer.IsGone);
        if (seenAt < 0 || ordered.Any(answer => answer.InBlock))
        {
            return null;
        }

        var seen = ordered[seenAt].Sighting;
        if (store.KnownHeight(reader, seen.Sequence) is not { } known)
        {
            return null;
        }

        var negatives = ordered.Skip(seenAt + 1)
            .Where(answer => store.EndpointHeight(reader, answer.EndpointId, answer.Sighting.Sequence) >= known)
            .ToList();
        if (negatives.Count < 2 || !Confirms(store, reader, negatives[^2], negatives[^1]))
        {
            return null;
        }

        var newest = negatives[^1].Sighting;
        return newest.Seen - seen.Seen >= MempoolGrace && store.KnownHeight(reader, newest.Sequence) > known
            ? (seen, newest)
            : null;
    }

    /// <summary>
    /// Says whether two answers put the transaction in the same block: the same hash, or, when an answer did not say
    /// the hash, the same height.
    /// </summary>
    /// <param name="before">The earlier answer.</param>
    /// <param name="now">The later answer.</param>
    /// <returns><see langword="true"/> when the block is the same as far as the answers tell.</returns>
    private static bool SameBlock(BlockSighting before, BlockSighting now) =>
        before.Hash is not null && now.Hash is not null
            ? string.Equals(before.Hash, now.Hash, HashComparison(before.Hash))
            : before.Height == now.Height;

    /// <summary>
    /// Gives how two block hashes are compared: hex (Bitcoin, Ethereum, TRON) without case, any other (Solana's Base58,
    /// whose case is part of it) exactly.
    /// </summary>
    /// <param name="hash">One of the hashes.</param>
    /// <returns>The comparison.</returns>
    internal static StringComparison HashComparison(string hash) =>
        hash.AsSpan().TrimStart("0x").ContainsAnyExcept("0123456789abcdefABCDEF")
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

    /// <summary>
    /// Says whether a later answer confirms an earlier one: it came from another endpoint, or the case knew of at least
    /// one more block when it came.
    /// </summary>
    /// <param name="store">The chain's sealed answers.</param>
    /// <param name="reader">The case's reader.</param>
    /// <param name="earlier">The earlier answer.</param>
    /// <param name="later">The later answer.</param>
    /// <returns><see langword="true"/> when it confirms it.</returns>
    private static bool Confirms(RawResponseStore store, ICaseReader reader, BlockAnswer earlier, BlockAnswer later) =>
        earlier.EndpointId != later.EndpointId
        || store.KnownHeight(reader, later.Sighting.Sequence) > store.KnownHeight(reader, earlier.Sighting.Sequence);

    /// <summary>Says whether an answer's endpoint had come as far as a block when it answered.</summary>
    /// <param name="store">The chain's sealed answers.</param>
    /// <param name="reader">The case's reader.</param>
    /// <param name="answer">The answer.</param>
    /// <param name="block">Where the case first saw the transaction.</param>
    /// <returns><see langword="true"/> when its sealed answers up to then show at least the block's height.</returns>
    private static bool Reached(RawResponseStore store, ICaseReader reader, BlockAnswer answer, BlockSighting block) =>
        block.Height is { } height
        && store.EndpointHeight(reader, answer.EndpointId, answer.Sighting.Sequence) >= height;
}
