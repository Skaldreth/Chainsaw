namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// Keccak-256 as Ethereum uses it (the original Keccak padding, not the SHA-3 standard's, which .NET has). It names
/// an address's checksum (EIP-55) and an event's signature, such as ERC-20's <c>Transfer</c>.
/// </summary>
internal static class Keccak256
{
    /// <summary>How many bytes of input one permutation absorbs: 1600 bits less twice the 256-bit output.</summary>
    private const int Rate = 136;

    /// <summary>The round constants of Keccak-f[1600].</summary>
    private static readonly ulong[] RoundConstants =
    [
        0x0000000000000001, 0x0000000000008082, 0x800000000000808A, 0x8000000080008000, 0x000000000000808B,
        0x0000000080000001, 0x8000000080008081, 0x8000000000008009, 0x000000000000008A, 0x0000000000000088,
        0x0000000080008009, 0x000000008000000A, 0x000000008000808B, 0x800000000000008B, 0x8000000000008089,
        0x8000000000008003, 0x8000000000008002, 0x8000000000000080, 0x000000000000800A, 0x800000008000000A,
        0x8000000080008081, 0x8000000000008080, 0x0000000080000001, 0x8000000080008008,
    ];

    /// <summary>How far each lane is rotated, by lane index (x + 5y).</summary>
    private static readonly int[] Rotations =
    [
        0, 1, 62, 28, 27, 36, 44, 6, 55, 20, 3, 10, 43, 25, 39, 41, 45, 15, 21, 8, 18, 2, 61, 56, 14,
    ];

    /// <summary>Hashes some bytes.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The 32-byte hash.</returns>
    public static byte[] Hash(ReadOnlySpan<byte> data)
    {
        var state = new ulong[25];
        var block = new byte[Rate];
        var offset = 0;
        while (data.Length - offset >= Rate)
        {
            Absorb(state, data.Slice(offset, Rate));
            offset += Rate;
        }

        // The last block: the rest, Keccak's padding 0x01 … 0x80 (both in one byte when only one is left).
        var rest = data[offset..];
        rest.CopyTo(block);
        block[rest.Length] ^= 0x01;
        block[Rate - 1] ^= 0x80;
        Absorb(state, block);

        var hash = new byte[32];
        for (var i = 0; i < 4; i++)
        {
            BitConverter.TryWriteBytes(hash.AsSpan(i * 8, 8), state[i]);
            if (!BitConverter.IsLittleEndian)
            {
                hash.AsSpan(i * 8, 8).Reverse();
            }
        }

        return hash;
    }

    /// <summary>Hashes a text's UTF-8 bytes.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The 32-byte hash.</returns>
    public static byte[] Hash(string text) => Hash(System.Text.Encoding.UTF8.GetBytes(text));

    /// <summary>XORs one block into the state, lane by lane in little-endian order, and permutes it.</summary>
    /// <param name="state">The 25 lanes.</param>
    /// <param name="block">One block of <see cref="Rate"/> bytes.</param>
    private static void Absorb(ulong[] state, ReadOnlySpan<byte> block)
    {
        for (var i = 0; i < Rate / 8; i++)
        {
            state[i] ^= System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(block.Slice(i * 8, 8));
        }

        Permute(state);
    }

    /// <summary>Keccak-f[1600]: 24 rounds of theta, rho, pi, chi and iota.</summary>
    /// <param name="a">The 25 lanes, indexed x + 5y.</param>
    private static void Permute(ulong[] a)
    {
        Span<ulong> c = stackalloc ulong[5];
        Span<ulong> b = stackalloc ulong[25];
        foreach (var constant in RoundConstants)
        {
            for (var x = 0; x < 5; x++)
            {
                c[x] = a[x] ^ a[x + 5] ^ a[x + 10] ^ a[x + 15] ^ a[x + 20];
            }

            for (var x = 0; x < 5; x++)
            {
                var d = c[(x + 4) % 5] ^ ulong.RotateLeft(c[(x + 1) % 5], 1);
                for (var y = 0; y < 25; y += 5)
                {
                    a[x + y] ^= d;
                }
            }

            for (var x = 0; x < 5; x++)
            {
                for (var y = 0; y < 5; y++)
                {
                    var from = x + (5 * y);
                    b[y + (5 * (((2 * x) + (3 * y)) % 5))] = ulong.RotateLeft(a[from], Rotations[from]);
                }
            }

            for (var y = 0; y < 25; y += 5)
            {
                for (var x = 0; x < 5; x++)
                {
                    a[x + y] = b[x + y] ^ (~b[((x + 1) % 5) + y] & b[((x + 2) % 5) + y]);
                }
            }

            a[0] ^= constant;
        }
    }
}
