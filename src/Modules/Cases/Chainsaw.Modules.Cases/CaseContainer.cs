using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The layout of a case file: an 8-byte magic, the format version (16 bits, big-endian), the header length (32 bits,
/// big-endian), the header as UTF-8 JSON, the database, encrypted by SQLite3 Multiple Ciphers, and last the seal over
/// everything before it (<see cref="CaseFileSeal"/>).
/// </summary>
/// <remarks>
/// SQLite3 Multiple Ciphers encrypts every byte of the database file and has no room for envelopes of its own, so the
/// envelopes live in front of the database and the database is worked on in a working copy (see <c>Case</c>).
/// </remarks>
internal static class CaseContainer
{
    /// <summary>
    /// The format version this build writes and the only one it reads. Version 1 had no seal over the file; it is
    /// refused rather than read without one, since a file that says it is version 1 could otherwise have had its
    /// seal removed (before 1.0 old cases need not open).
    /// </summary>
    public const int FormatVersion = 2;

    /// <summary>The largest header that is read; a header is a few hundred bytes per envelope.</summary>
    public const int MaximumHeaderLength = 1024 * 1024;

    /// <summary>The most envelopes a header may hold.</summary>
    public const int MaximumEnvelopes = 64;

    /// <summary>The header field with the case id.</summary>
    private const string CaseIdField = "caseId";

    /// <summary>The header field with the envelopes.</summary>
    private const string EnvelopesField = "envelopes";

    /// <summary>The magic at the start of every case file.</summary>
    private static ReadOnlySpan<byte> Magic => "CHSWCASE"u8;

    /// <summary>The length of the fixed part before the header: magic, version and length.</summary>
    private static int PrefixLength => Magic.Length + sizeof(ushort) + sizeof(uint);

    /// <summary>Reads the header and leaves the stream at the first byte of the database.</summary>
    /// <param name="stream">The case file, at its start.</param>
    /// <returns>The header.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Corrupt"/>, <see cref="CaseError.UnsupportedVersion"/> or
    /// <see cref="CaseError.OutdatedFormat"/>.
    /// </exception>
    public static CaseHeader ReadHeader(Stream stream)
    {
        Span<byte> prefix = stackalloc byte[PrefixLength];
        ReadExactly(stream, prefix);
        if (!prefix[..Magic.Length].SequenceEqual(Magic))
        {
            throw new CaseException(CaseError.Corrupt);
        }

        var version = BinaryPrimitives.ReadUInt16BigEndian(prefix[Magic.Length..]);
        if (version == 0)
        {
            throw new CaseException(CaseError.Corrupt);
        }

        if (version > FormatVersion)
        {
            throw new CaseException(CaseError.UnsupportedVersion);
        }

        if (version < FormatVersion)
        {
            throw new CaseException(CaseError.OutdatedFormat);
        }

        var length = BinaryPrimitives.ReadUInt32BigEndian(prefix[(Magic.Length + sizeof(ushort))..]);
        if (length is 0 or > MaximumHeaderLength)
        {
            throw new CaseException(CaseError.Corrupt);
        }

        var json = new byte[length];
        ReadExactly(stream, json);
        return Parse(json);
    }

    /// <summary>Writes the fixed part and the header; the caller writes the database after it.</summary>
    /// <param name="stream">The new file, at its start.</param>
    /// <param name="header">The header.</param>
    /// <exception cref="InvalidOperationException">The header has more envelopes than are read back.</exception>
    public static void WriteHeader(Stream stream, CaseHeader header)
    {
        if (header.Envelopes.Count is 0 or > MaximumEnvelopes)
        {
            throw new InvalidOperationException("A header must hold between one and the maximum of envelopes.");
        }

        var envelopes = new JsonArray();
        foreach (var envelope in header.Envelopes)
        {
            envelopes.Add(envelope.DeepClone());
        }

        var document = new JsonObject
        {
            [CaseIdField] = header.CaseId.ToString("D"),
            [EnvelopesField] = envelopes,
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(document);
        Span<byte> prefix = stackalloc byte[PrefixLength];
        Magic.CopyTo(prefix);
        BinaryPrimitives.WriteUInt16BigEndian(prefix[Magic.Length..], FormatVersion);
        BinaryPrimitives.WriteUInt32BigEndian(prefix[(Magic.Length + sizeof(ushort))..], (uint)json.Length);
        stream.Write(prefix);
        stream.Write(json);
    }

    /// <summary>Parses the header JSON.</summary>
    /// <param name="json">The UTF-8 JSON.</param>
    /// <returns>The header.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when the JSON does not hold.</exception>
    private static CaseHeader Parse(byte[] json)
    {
        try
        {
            var document = JsonNode.Parse(json) as JsonObject;
            var caseIdText = document?[CaseIdField]?.GetValue<string>();
            var envelopes = document?[EnvelopesField] as JsonArray;
            if (!Guid.TryParseExact(caseIdText, "D", out var caseId) || envelopes is null
                || envelopes.Count is 0 or > MaximumEnvelopes)
            {
                throw new CaseException(CaseError.Corrupt);
            }

            var parsed = new List<JsonObject>(envelopes.Count);
            foreach (var envelope in envelopes)
            {
                if (envelope is not JsonObject item
                    || item[KeyEnvelopes.KindField]?.GetValue<string>() is not { Length: > 0 })
                {
                    throw new CaseException(CaseError.Corrupt);
                }

                parsed.Add((JsonObject)item.DeepClone());
            }

            return new CaseHeader(caseId, parsed);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException
            or ArgumentException)
        {
            throw new CaseException(CaseError.Corrupt, exception);
        }
    }

    /// <summary>Reads exactly enough bytes to fill a buffer.</summary>
    /// <param name="stream">The stream.</param>
    /// <param name="buffer">The buffer to fill.</param>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when the file ends first.</exception>
    private static void ReadExactly(Stream stream, Span<byte> buffer)
    {
        try
        {
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException exception)
        {
            throw new CaseException(CaseError.Corrupt, exception);
        }
    }
}
