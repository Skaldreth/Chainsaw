using System.Buffers.Binary;
using System.Numerics;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// A block's 80-byte header, read from its own bytes, with its hash computed by Chainsaw (the double SHA-256 of the
/// header, reversed as Bitcoin shows it).
/// </summary>
/// <param name="Hash">The hash computed from the bytes.</param>
/// <param name="Version">The version field.</param>
/// <param name="PreviousHash">The previous block's hash.</param>
/// <param name="MerkleRoot">The root of the block's transactions, as Bitcoin shows it.</param>
/// <param name="Time">The time in the header.</param>
/// <param name="Bits">The target, in the header's compact form.</param>
internal sealed record BlockHeader(
    BlockHash Hash, int Version, BlockHash PreviousHash, string MerkleRoot, DateTimeOffset Time, uint Bits);

/// <summary>
/// Reads a block header and checks the source's description of the block against it: the header's hash is the
/// block's hash, it meets the target its own bits name (so it carries the work it claims), and the description's
/// version, previous block, merkle root and time are the header's.
/// </summary>
internal static class BlockHeaderReader
{
    /// <summary>The size of a header.</summary>
    private const int HeaderSize = 80;

    /// <summary>The largest exponent of a compact target that still fits 256 bits.</summary>
    private const int MaximumExponent = 32;

    /// <summary>Reads a header given as hex text (as from <c>/block/:hash/header</c>).</summary>
    /// <param name="body">The response's bytes.</param>
    /// <param name="expected">The block asked for; the header must hash to it.</param>
    /// <returns>The header.</returns>
    /// <exception cref="EsploraFormatException">
    /// It is not an 80-byte header, it is another block, or it does not meet its own target.
    /// </exception>
    public static BlockHeader ParseHex(ReadOnlySpan<byte> body, BlockHash expected)
    {
        var bytes = RawTransactionReader.HexBytes(body);
        if (bytes.Length != HeaderSize)
        {
            throw new EsploraFormatException("A block header is 80 bytes.");
        }

        var hash = BlockHash.Parse(RawTransactionReader.DisplayHash(bytes));
        if (hash != expected)
        {
            throw new EsploraFormatException("The header is another block than the one asked for.");
        }

        var bits = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(72, 4));
        if (!MeetsTarget(hash, bits))
        {
            throw new EsploraFormatException("The header does not meet the target its bits name.");
        }

        return new BlockHeader(
            hash,
            BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0, 4)),
            BlockHash.Parse(Reversed(bytes.AsSpan(4, 32))),
            Reversed(bytes.AsSpan(36, 32)),
            DateTimeOffset.FromUnixTimeSeconds(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(68, 4))),
            bits);
    }

    /// <summary>
    /// Compares the source's description of a block with its header. The first block has no previous block; its
    /// header has 32 zero bytes there.
    /// </summary>
    /// <param name="block">The description.</param>
    /// <param name="header">The header.</param>
    /// <returns><see langword="null"/> when they agree, otherwise what differs (in English, for the log).</returns>
    public static string? Compare(BitcoinBlock block, BlockHeader header)
    {
        ArgumentNullException.ThrowIfNull(block);
        ArgumentNullException.ThrowIfNull(header);
        var previous = block.PreviousHash?.Value ?? new string('0', 64);
        if (block.Hash != header.Hash || previous != header.PreviousHash.Value)
        {
            return "hash";
        }

        if (block.Version != header.Version && block.Version != (uint)header.Version)
        {
            return "version";
        }

        if (!string.Equals(block.MerkleRoot, header.MerkleRoot, StringComparison.Ordinal))
        {
            return "merkle root";
        }

        return block.Time == header.Time ? null : "time";
    }

    /// <summary>
    /// Checks that a hash, read as a number, is at most the target that the compact bits name. Only the header's own
    /// claim is checked; whether the bits are the network's difficulty at that height is not.
    /// </summary>
    /// <param name="hash">The block's hash.</param>
    /// <param name="bits">The compact target.</param>
    /// <returns><see langword="true"/> when the hash meets the target.</returns>
    public static bool MeetsTarget(BlockHash hash, uint bits)
    {
        ArgumentNullException.ThrowIfNull(hash);
        var exponent = (int)(bits >> 24);
        var mantissa = bits & 0x007F_FFFF;
        // A negative target, a zero one, or one beyond 256 bits (Bitcoin Core's overflow rule) is never met.
        if ((bits & 0x0080_0000) != 0 || mantissa == 0 || exponent > MaximumExponent)
        {
            return false;
        }

        var target = exponent <= 3
            ? new BigInteger(mantissa >> (8 * (3 - exponent)))
            : new BigInteger(mantissa) << (8 * (exponent - 3));
        var value = new BigInteger(Convert.FromHexString(hash.Value), isUnsigned: true, isBigEndian: true);
        return value <= target;
    }

    /// <summary>Writes 32 bytes reversed, as Bitcoin shows hashes.</summary>
    /// <param name="bytes">The bytes in the header's order.</param>
    /// <returns>Lower-case hex.</returns>
    private static string Reversed(ReadOnlySpan<byte> bytes)
    {
        var copy = bytes.ToArray();
        Array.Reverse(copy);
        return Convert.ToHexStringLower(copy);
    }
}
