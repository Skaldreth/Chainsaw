using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// A source's proof that a transaction is in a block (<c>/tx/:txid/merkle-proof</c>): the hashes beside the path from
/// the transaction up through the block's tree of transactions, and where the transaction stands in the block. Chainsaw
/// computes the root from the txid and these hashes itself and compares it with the merkle root in the block's own
/// header, so that a source's word that the transaction is in that block is checked (granskningen etapp 15).
/// </summary>
/// <param name="Height">The block's height, as the source gave it.</param>
/// <param name="Branch">The hashes beside the path, from the bottom up, as Bitcoin shows hashes.</param>
/// <param name="Position">The transaction's place in the block, from 0.</param>
internal sealed record MerkleProof(long Height, IReadOnlyList<string> Branch, long Position)
{
    /// <summary>The most hashes beside a path: a block cannot hold 2^32 transactions.</summary>
    private const int MaximumBranch = 32;

    /// <summary>Reads a proof as Esplora gives it: <c>{"block_height", "merkle", "pos"}</c>.</summary>
    /// <param name="body">The response's bytes.</param>
    /// <returns>The proof.</returns>
    /// <exception cref="EsploraFormatException">It is not such a proof.</exception>
    public static MerkleProof Parse(ReadOnlySpan<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("block_height", out var height) || !height.TryGetInt64(out var blockHeight)
                || !root.TryGetProperty("pos", out var pos) || !pos.TryGetInt64(out var position)
                || !root.TryGetProperty("merkle", out var merkle) || merkle.ValueKind != JsonValueKind.Array
                || blockHeight < 0 || position < 0 || merkle.GetArrayLength() > MaximumBranch)
            {
                throw new EsploraFormatException("The response is not a merkle proof.");
            }

            var branch = new List<string>();
            foreach (var hash in merkle.EnumerateArray())
            {
                if (hash.ValueKind != JsonValueKind.String || !BlockHash.TryParse(hash.GetString(), out var parsed))
                {
                    throw new EsploraFormatException("A hash in the merkle proof is not 32 bytes of hex.");
                }

                branch.Add(parsed.Value);
            }

            return position >> branch.Count != 0
                ? throw new EsploraFormatException("The place in the block does not fit the proof.")
                : new MerkleProof(blockHeight, branch, position);
        }
        catch (JsonException exception)
        {
            throw new EsploraFormatException("The response is not a merkle proof.", exception);
        }
    }

    /// <summary>
    /// Computes the root the proof leads to from a transaction: at each level its hash is joined with the one beside
    /// it, on the left or the right by the place, and hashed twice with SHA-256, as Bitcoin builds the tree.
    /// </summary>
    /// <param name="txid">The transaction.</param>
    /// <returns>
    /// The root, as Bitcoin shows hashes, or <see langword="null"/> when the proof uses the duplicated last node on the
    /// right-hand side (CVE-2012-2459): a node's copy stands only to its own right, never as a place of its own.
    /// </returns>
    public string? RootFrom(Txid txid)
    {
        ArgumentNullException.ThrowIfNull(txid);
        var hash = Internal(txid.Value);
        var place = Position;
        foreach (var beside in Branch)
        {
            var other = Internal(beside);
            var onTheRight = (place & 1) == 1;
            if (onTheRight && other.AsSpan().SequenceEqual(hash))
            {
                return null;
            }

            hash = onTheRight ? Twice([.. other, .. hash]) : Twice([.. hash, .. other]);
            place >>= 1;
        }

        Array.Reverse(hash);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Says whether the proof's depth and place fit a block with this many transactions: the tree has
    /// <c>ceil(log2(n))</c> levels, and the place is one of the block's.
    /// </summary>
    /// <param name="transactions">How many transactions the block has.</param>
    /// <returns><see langword="true"/> when they fit.</returns>
    public bool Fits(int transactions)
    {
        var depth = 0;
        while ((1L << depth) < transactions)
        {
            depth++;
        }

        return transactions > 0 && Branch.Count == depth && Position < transactions;
    }

    /// <summary>Gives a hash in the byte order Bitcoin hashes it in (reversed from how it is shown).</summary>
    /// <param name="shown">The hash as shown.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Internal(string shown)
    {
        var bytes = Convert.FromHexString(shown);
        Array.Reverse(bytes);
        return bytes;
    }

    /// <summary>Hashes twice with SHA-256.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The hash.</returns>
    private static byte[] Twice(byte[] bytes) => SHA256.HashData(SHA256.HashData(bytes));
}
