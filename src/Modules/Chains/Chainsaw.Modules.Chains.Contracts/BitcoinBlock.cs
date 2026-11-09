namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>A block's header and size, as the source reported it.</summary>
/// <param name="Hash">The block's hash.</param>
/// <param name="Height">The block's height.</param>
/// <param name="Time">The time in the header, in UTC.</param>
/// <param name="MedianTime">The median of the last eleven blocks' times, in UTC, when the source gives it.</param>
/// <param name="PreviousHash">The previous block's hash; <see langword="null"/> only for the first block.</param>
/// <param name="MerkleRoot">The root of the block's transactions, as 64 hex characters.</param>
/// <param name="TransactionCount">The number of transactions in the block.</param>
/// <param name="Size">The block's size in bytes.</param>
/// <param name="Weight">The block's weight in weight units.</param>
/// <param name="Version">The header's version field.</param>
public sealed record BitcoinBlock(
    BlockHash Hash,
    long Height,
    DateTimeOffset Time,
    DateTimeOffset? MedianTime,
    BlockHash? PreviousHash,
    string MerkleRoot,
    int TransactionCount,
    long Size,
    long Weight,
    long Version)
{
    /// <summary>
    /// Gets whether Chainsaw has checked this description against the block's own 80-byte header, sealed in the case:
    /// the header hashes to the block's hash, meets the target its own bits name, and has this version, previous
    /// block, merkle root and time.
    /// </summary>
    public bool HeaderVerified { get; init; }

    /// <summary>
    /// Gets the target in compact form as the source described it (<c>bits</c>), or <see langword="null"/> when it
    /// did not; checked against the header when given.
    /// </summary>
    public uint? Bits { get; init; }

    /// <summary>Gets the nonce as the source described it, or <see langword="null"/>; checked against the header.</summary>
    public uint? Nonce { get; init; }
}
