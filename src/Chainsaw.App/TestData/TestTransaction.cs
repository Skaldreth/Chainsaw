#if DEBUG
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Chainsaw.App.TestData;

/// <summary>A made-up input: the coin it spends, with what the coin is, or new coins for a coinbase.</summary>
/// <param name="PreviousTxid">The spent coin's transaction, or <see langword="null"/> for a coinbase.</param>
/// <param name="PreviousIndex">The spent coin's output.</param>
/// <param name="Spent">The spent output, or <see langword="null"/> for a coinbase.</param>
public sealed record TestInput(string? PreviousTxid, int PreviousIndex, TestOutput? Spent)
{
    /// <summary>Gets whether the input makes new coins.</summary>
    public bool IsCoinbase => PreviousTxid is null;
}

/// <summary>A made-up output.</summary>
/// <param name="To">Where it pays.</param>
/// <param name="Value">The amount in satoshi.</param>
public sealed record TestOutput(TestAddress To, long Value);

/// <summary>
/// A made-up transaction with its own bytes, written as the chain writes them (without a witness), so that its txid is
/// the double SHA-256 of the bytes and Chainsaw's check against the bytes holds as for a real one.
/// </summary>
public sealed class TestTransaction
{
    /// <summary>The version every made-up transaction has.</summary>
    public const int Version = 2;

    /// <summary>The sequence of a spending input (replaceable by fee, as wallets write today).</summary>
    public const uint Sequence = 0xFFFF_FFFD;

    /// <summary>The sequence and the index of a coinbase input.</summary>
    public const uint Final = 0xFFFF_FFFF;

    /// <summary>The weight per byte without a witness.</summary>
    private const int WitnessScale = 4;

    /// <summary>Creates the transaction and writes its bytes.</summary>
    /// <param name="label">The scenario's name for it.</param>
    /// <param name="height">The block it is in.</param>
    /// <param name="inputs">The inputs.</param>
    /// <param name="outputs">The outputs.</param>
    public TestTransaction(string label, long height, IReadOnlyList<TestInput> inputs, IReadOnlyList<TestOutput> outputs)
    {
        Label = label;
        Height = height;
        Inputs = inputs;
        Outputs = outputs;
        Bytes = Write();
        Txid = DisplayHash(Bytes);
    }

    /// <summary>Gets the scenario's name for it.</summary>
    public string Label { get; }

    /// <summary>Gets the block it is in.</summary>
    public long Height { get; }

    /// <summary>Gets the inputs.</summary>
    public IReadOnlyList<TestInput> Inputs { get; }

    /// <summary>Gets the outputs.</summary>
    public IReadOnlyList<TestOutput> Outputs { get; }

    /// <summary>Gets the bytes, as <c>/tx/:txid/hex</c> gives them.</summary>
    public byte[] Bytes { get; }

    /// <summary>Gets the txid.</summary>
    public string Txid { get; }

    /// <summary>Gets whether it makes new coins.</summary>
    public bool IsCoinbase => Inputs[0].IsCoinbase;

    /// <summary>Gets the size in bytes.</summary>
    public int Size => Bytes.Length;

    /// <summary>Gets the weight: four per byte, since no witness is written.</summary>
    public int Weight => Bytes.Length * WitnessScale;

    /// <summary>Gets the fee: what the inputs leave over, nothing for a coinbase.</summary>
    public long Fee => IsCoinbase ? 0 : Inputs.Sum(input => input.Spent!.Value) - Outputs.Sum(output => output.Value);

    /// <summary>Gives a hash as Bitcoin shows it: double SHA-256, the bytes reversed.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The hash in hex.</returns>
    public static string DisplayHash(ReadOnlySpan<byte> bytes)
    {
        var hash = SHA256.HashData(SHA256.HashData(bytes));
        Array.Reverse(hash);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Gives a hash in the byte order the chain writes it in: the shown hex reversed.</summary>
    /// <param name="display">The hash as shown.</param>
    /// <returns>The bytes.</returns>
    public static byte[] Internal(string display)
    {
        var bytes = Convert.FromHexString(display);
        Array.Reverse(bytes);
        return bytes;
    }

    /// <summary>Writes the transaction as the chain does, without the witness.</summary>
    /// <returns>The bytes.</returns>
    private byte[] Write()
    {
        using var stream = new MemoryStream();
        WriteInt32(stream, Version);
        WriteCount(stream, Inputs.Count);
        foreach (var input in Inputs)
        {
            WriteInput(stream, input);
        }

        WriteCount(stream, Outputs.Count);
        Span<byte> value = stackalloc byte[8];
        foreach (var output in Outputs)
        {
            BinaryPrimitives.WriteInt64LittleEndian(value, output.Value);
            stream.Write(value);
            WriteBytes(stream, Convert.FromHexString(output.To.Script));
        }

        WriteUInt32(stream, 0);
        return stream.ToArray();
    }

    /// <summary>
    /// Writes one input. A coinbase's script carries the height (as BIP 34 asks) and the label, so that every coinbase
    /// has a txid of its own; a spending input's script is left empty, since nothing checks signatures here.
    /// </summary>
    /// <param name="stream">Where to write.</param>
    /// <param name="input">The input.</param>
    private void WriteInput(Stream stream, TestInput input)
    {
        if (input.IsCoinbase)
        {
            stream.Write(new byte[32]);
            WriteUInt32(stream, Final);
            WriteBytes(stream, CoinbaseScript());
            WriteUInt32(stream, Final);
            return;
        }

        stream.Write(Internal(input.PreviousTxid!));
        WriteUInt32(stream, (uint)input.PreviousIndex);
        WriteBytes(stream, []);
        WriteUInt32(stream, Sequence);
    }

    /// <summary>Gives a coinbase's script: the height pushed as four bytes, then the label.</summary>
    /// <returns>The script.</returns>
    public byte[] CoinbaseScript()
    {
        var label = Encoding.UTF8.GetBytes(Label);
        var script = new byte[1 + 4 + 1 + label.Length];
        script[0] = 4;
        BinaryPrimitives.WriteInt32LittleEndian(script.AsSpan(1, 4), (int)Height);
        script[5] = (byte)label.Length;
        label.CopyTo(script, 6);
        return script;
    }

    /// <summary>Writes a count as the chain does (a compact size; the made-up data stays below 253).</summary>
    /// <param name="stream">Where to write.</param>
    /// <param name="count">The count.</param>
    /// <exception cref="ArgumentOutOfRangeException">The count needs more than one byte.</exception>
    private static void WriteCount(Stream stream, int count)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 252);
        stream.WriteByte((byte)count);
    }

    /// <summary>Writes bytes with their length first.</summary>
    /// <param name="stream">Where to write.</param>
    /// <param name="bytes">The bytes.</param>
    private static void WriteBytes(Stream stream, ReadOnlySpan<byte> bytes)
    {
        WriteCount(stream, bytes.Length);
        stream.Write(bytes);
    }

    /// <summary>Writes a signed 32-bit number, little-endian.</summary>
    /// <param name="stream">Where to write.</param>
    /// <param name="value">The number.</param>
    private static void WriteInt32(Stream stream, int value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }

    /// <summary>Writes an unsigned 32-bit number, little-endian.</summary>
    /// <param name="stream">Where to write.</param>
    /// <param name="value">The number.</param>
    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        stream.Write(bytes);
    }
}
#endif
