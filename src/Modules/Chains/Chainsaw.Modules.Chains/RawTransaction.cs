using System.Buffers.Binary;
using System.Security.Cryptography;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// A transaction read from its own bytes (the serialisation the network carries), with its txid computed by Chainsaw:
/// the double SHA-256 of the transaction without its witness, written in reverse byte order as Bitcoin shows it.
/// </summary>
/// <param name="Txid">The txid computed from the bytes.</param>
/// <param name="Version">The version field, as the signed 32-bit number it is in the bytes.</param>
/// <param name="Inputs">The inputs, in order.</param>
/// <param name="Outputs">The outputs, in order.</param>
/// <param name="LockTime">The lock time.</param>
internal sealed record RawTransaction(
    Txid Txid, int Version, IReadOnlyList<RawInput> Inputs, IReadOnlyList<RawOutput> Outputs, uint LockTime);

/// <summary>An input as the bytes have it: the output it spends and its sequence.</summary>
/// <param name="PreviousTxid">The spent output's transaction, as Bitcoin shows it (reversed hex).</param>
/// <param name="PreviousIndex">The spent output's index; <see cref="uint.MaxValue"/> for a coinbase.</param>
/// <param name="Sequence">The sequence field.</param>
internal sealed record RawInput(string PreviousTxid, uint PreviousIndex, uint Sequence);

/// <summary>An output as the bytes have it.</summary>
/// <param name="Value">The amount in satoshi.</param>
/// <param name="Script">The locking script as lower-case hex.</param>
internal sealed record RawOutput(long Value, string Script);

/// <summary>
/// Reads a transaction's bytes and checks the source's description of it against them, so that Chainsaw itself knows
/// that an answer is the transaction that was asked for (principle 1): the txid commits to everything but the witness,
/// so a description that matches the bytes in those parts is the transaction.
/// </summary>
internal static class RawTransactionReader
{
    /// <summary>The previous txid of a coinbase input: 32 zero bytes.</summary>
    private static readonly string CoinbaseTxid = new('0', 64);

    /// <summary>The bit of the flag after the marker that says a Litecoin transaction carries MWEB data.</summary>
    private const byte MwebFlag = 0x08;

    /// <summary>The most bytes a transaction may have (a block's weight limit divided by the witness factor).</summary>
    private const int MaximumBytes = 4_000_000;

    /// <summary>Reads a transaction's bytes, given as hex text (as from <c>/tx/:txid/hex</c>).</summary>
    /// <param name="body">The response's bytes.</param>
    /// <param name="expected">The txid asked for; the bytes must hash to it.</param>
    /// <returns>The transaction.</returns>
    /// <exception cref="EsploraFormatException">
    /// The text is not hex, the bytes are not a transaction, or they are another transaction than the one asked for.
    /// </exception>
    public static RawTransaction ParseHex(ReadOnlySpan<byte> body, Txid expected)
    {
        var raw = Read(HexBytes(body));
        return raw.Txid == expected
            ? raw
            : throw new EsploraFormatException("The bytes are another transaction than the one asked for.");
    }

    /// <summary>Reads a transaction's bytes and computes its txid.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The transaction.</returns>
    /// <exception cref="EsploraFormatException">The bytes are not one whole transaction.</exception>
    public static RawTransaction Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length > MaximumBytes)
        {
            throw new EsploraFormatException("The transaction is larger than a block.");
        }

        var reader = new ByteReader(bytes);
        var version = reader.Int32();
        var afterVersion = reader.Position;
        var hasWitness = reader.Remaining >= 2 && bytes[reader.Position] == 0 && bytes[reader.Position + 1] == 1;
        if (reader.Remaining >= 2 && bytes[reader.Position] == 0 && (bytes[reader.Position + 1] & MwebFlag) != 0)
        {
            // A flag beyond the witness's: on Litecoin the extension block's (MWEB) data, as in each block's HogEx.
            throw new EsploraFormatException(
                "The transaction's flag (0x08) says it carries data Chainsaw does not read; its bytes are not checked.");
        }

        if (hasWitness)
        {
            reader.Skip(2);
        }

        var bodyStart = reader.Position;
        var inputs = ReadInputs(ref reader);
        var outputs = ReadOutputs(ref reader);
        var bodyEnd = reader.Position;
        if (hasWitness)
        {
            SkipWitnesses(ref reader, inputs.Count);
        }

        var lockTimeStart = reader.Position;
        var lockTime = reader.UInt32();
        if (reader.Remaining != 0)
        {
            throw new EsploraFormatException("The bytes go on after the transaction.");
        }

        if (inputs.Count == 0 || outputs.Count == 0)
        {
            throw new EsploraFormatException("A transaction has at least one input and one output.");
        }

        // The txid is over the transaction without the marker, the flag and the witness.
        var stripped = new byte[afterVersion + (bodyEnd - bodyStart) + 4];
        bytes[..afterVersion].CopyTo(stripped);
        bytes[bodyStart..bodyEnd].CopyTo(stripped.AsSpan(afterVersion));
        bytes[lockTimeStart..].CopyTo(stripped.AsSpan(afterVersion + (bodyEnd - bodyStart)));
        return new RawTransaction(Txid.Parse(DisplayHash(stripped)), version, inputs, outputs, lockTime);
    }

    /// <summary>
    /// Compares the source's description of a transaction with its bytes, in every part the txid commits to: the
    /// version, each input's spent output and sequence, each output's amount and script, and the lock time. What the
    /// bytes do not hold (the spent outputs' amounts and addresses, the block) is not compared.
    /// </summary>
    /// <param name="transaction">The description.</param>
    /// <param name="raw">The bytes, read.</param>
    /// <returns><see langword="null"/> when they agree, otherwise what differs (in English, for the log).</returns>
    public static string? Compare(BitcoinTransaction transaction, RawTransaction raw)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(raw);
        if (transaction.Txid != raw.Txid)
        {
            return "txid";
        }

        if (transaction.Version != raw.Version && transaction.Version != (uint)raw.Version)
        {
            return "version";
        }

        if (transaction.LockTime != raw.LockTime)
        {
            return "locktime";
        }

        if (transaction.Inputs.Count != raw.Inputs.Count || transaction.Outputs.Count != raw.Outputs.Count)
        {
            return "counts";
        }

        for (var index = 0; index < raw.Inputs.Count; index++)
        {
            if (!SameInput(transaction.Inputs[index], raw.Inputs[index]))
            {
                return $"input {index}";
            }
        }

        for (var index = 0; index < raw.Outputs.Count; index++)
        {
            var output = transaction.Outputs[index];
            if (output.Amount.Value != raw.Outputs[index].Value
                || !string.Equals(output.Script, raw.Outputs[index].Script, StringComparison.Ordinal))
            {
                return $"output {index}";
            }
        }

        return null;
    }

    /// <summary>Computes a double SHA-256 and writes it as Bitcoin shows hashes: reversed, lower-case hex.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The 64 hex characters.</returns>
    public static string DisplayHash(ReadOnlySpan<byte> bytes)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(SHA256.HashData(bytes), hash);
        hash.Reverse();
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Reads hex text, allowing white space around it.</summary>
    /// <param name="body">The text's bytes.</param>
    /// <returns>The bytes it spells.</returns>
    /// <exception cref="EsploraFormatException">It is not hex.</exception>
    public static byte[] HexBytes(ReadOnlySpan<byte> body)
    {
        var text = System.Text.Encoding.ASCII.GetString(body).Trim();
        try
        {
            return Convert.FromHexString(text);
        }
        catch (FormatException exception)
        {
            throw new EsploraFormatException("The response is not hex.", exception);
        }
    }

    /// <summary>Checks one input of the description against the bytes.</summary>
    /// <param name="input">The described input.</param>
    /// <param name="raw">The input in the bytes.</param>
    /// <returns><see langword="true"/> when they agree.</returns>
    private static bool SameInput(BitcoinInput input, RawInput raw)
    {
        if (input.Sequence != raw.Sequence)
        {
            return false;
        }

        if (input.IsCoinbase)
        {
            return raw.PreviousTxid == CoinbaseTxid && raw.PreviousIndex == uint.MaxValue;
        }

        return input.PreviousTxid?.Value == raw.PreviousTxid && input.PreviousIndex == raw.PreviousIndex;
    }

    /// <summary>Reads the inputs.</summary>
    /// <param name="reader">The reader, at the input count.</param>
    /// <returns>The inputs.</returns>
    private static List<RawInput> ReadInputs(ref ByteReader reader)
    {
        // An input is at least 41 bytes, so a count beyond what is left cannot be true.
        var count = reader.Count(41);
        var inputs = new List<RawInput>(count);
        for (var index = 0; index < count; index++)
        {
            var previous = reader.Bytes(32).ToArray();
            Array.Reverse(previous);
            var previousIndex = reader.UInt32();
            reader.Skip(reader.Count(1));
            inputs.Add(new RawInput(Convert.ToHexStringLower(previous), previousIndex, reader.UInt32()));
        }

        return inputs;
    }

    /// <summary>Reads the outputs.</summary>
    /// <param name="reader">The reader, at the output count.</param>
    /// <returns>The outputs.</returns>
    private static List<RawOutput> ReadOutputs(ref ByteReader reader)
    {
        // An output is at least 9 bytes.
        var count = reader.Count(9);
        var outputs = new List<RawOutput>(count);
        for (var index = 0; index < count; index++)
        {
            var value = reader.Int64();
            if (value < 0)
            {
                throw new EsploraFormatException("An output's amount is negative.");
            }

            var script = reader.Bytes(reader.Count(1));
            outputs.Add(new RawOutput(value, Convert.ToHexStringLower(script)));
        }

        return outputs;
    }

    /// <summary>Skips the witness of each input.</summary>
    /// <param name="reader">The reader, after the outputs.</param>
    /// <param name="inputs">The number of inputs.</param>
    private static void SkipWitnesses(ref ByteReader reader, int inputs)
    {
        for (var input = 0; input < inputs; input++)
        {
            var items = reader.Count(1);
            for (var item = 0; item < items; item++)
            {
                reader.Skip(reader.Count(1));
            }
        }
    }

    /// <summary>Reads Bitcoin's little-endian fields and compact sizes, refusing to read past the end.</summary>
    private ref struct ByteReader
    {
        /// <summary>The bytes.</summary>
        private readonly ReadOnlySpan<byte> _bytes;

        /// <summary>Creates a reader at the start of the bytes.</summary>
        /// <param name="bytes">The bytes.</param>
        public ByteReader(ReadOnlySpan<byte> bytes) => _bytes = bytes;

        /// <summary>Gets where the next byte is read.</summary>
        public int Position { get; private set; }

        /// <summary>Gets how many bytes are left.</summary>
        public readonly int Remaining => _bytes.Length - Position;

        /// <summary>Reads some bytes.</summary>
        /// <param name="count">How many.</param>
        /// <returns>The bytes.</returns>
        public ReadOnlySpan<byte> Bytes(int count)
        {
            if (count < 0 || count > Remaining)
            {
                throw new EsploraFormatException("The transaction's bytes end too early.");
            }

            var read = _bytes.Slice(Position, count);
            Position += count;
            return read;
        }

        /// <summary>Skips some bytes.</summary>
        /// <param name="count">How many.</param>
        public void Skip(int count) => Bytes(count);

        /// <summary>Reads a signed 32-bit number.</summary>
        /// <returns>The number.</returns>
        public int Int32() => BinaryPrimitives.ReadInt32LittleEndian(Bytes(4));

        /// <summary>Reads an unsigned 32-bit number.</summary>
        /// <returns>The number.</returns>
        public uint UInt32() => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4));

        /// <summary>Reads a signed 64-bit number.</summary>
        /// <returns>The number.</returns>
        public long Int64() => BinaryPrimitives.ReadInt64LittleEndian(Bytes(8));

        /// <summary>
        /// Reads a compact size that counts things of at least a given size, refusing a count that the bytes left
        /// cannot hold (so that a forged count never allocates much).
        /// </summary>
        /// <param name="smallest">The fewest bytes each counted thing takes.</param>
        /// <returns>The count.</returns>
        public int Count(int smallest)
        {
            var first = Bytes(1)[0];
            ulong count = first switch
            {
                0xFD => BinaryPrimitives.ReadUInt16LittleEndian(Bytes(2)),
                0xFE => BinaryPrimitives.ReadUInt32LittleEndian(Bytes(4)),
                0xFF => BinaryPrimitives.ReadUInt64LittleEndian(Bytes(8)),
                _ => first,
            };
            if (count > (ulong)(Remaining / smallest))
            {
                throw new EsploraFormatException("A count is larger than the bytes can hold.");
            }

            return (int)count;
        }
    }
}
