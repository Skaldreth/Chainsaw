using System.Security.Cryptography;
using System.Text;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The seal over a whole case file: HMAC-SHA256, under a key derived from the file key and the case id, over every
/// byte before it (the prefix, the header with all its envelopes, also unknown ones, and the database), written as a
/// trailer at the end of the file.
/// </summary>
/// <remarks>
/// SQLite3 Multiple Ciphers authenticates each page of the database, but not which version of a page it is, and the
/// header is not encrypted at all. Without the seal, someone without the file key could swap in an older page of the
/// same case, or change an envelope that this build does not know, and the file would still open. With it, any change
/// to the file that is not made by someone holding the file key makes the file refuse to open.
/// </remarks>
internal static class CaseFileSeal
{
    /// <summary>The size of the tag.</summary>
    public const int TagSize = 32;

    /// <summary>The HKDF info for the key of the seal, kept apart from the database key.</summary>
    private const string SealKeyInfo = "chainsaw/case/file-seal/v1";

    /// <summary>Gets the length of the trailer: the marker and the tag.</summary>
    public static int TrailerLength => Marker.Length + TagSize;

    /// <summary>The marker in front of the tag, so that a file cut short is told from a wrong tag.</summary>
    private static ReadOnlySpan<byte> Marker => "CHSWSEAL"u8;

    /// <summary>Starts the seal of a case file.</summary>
    /// <param name="fileKey">The file key.</param>
    /// <param name="caseId">The case id from the header.</param>
    /// <returns>The HMAC to add every byte before the trailer to; the caller disposes it.</returns>
    public static IncrementalHash Begin(ReadOnlySpan<byte> fileKey, Guid caseId)
    {
        var key = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            HKDF.DeriveKey(
                HashAlgorithmName.SHA256, fileKey, key, caseId.ToByteArray(), Encoding.ASCII.GetBytes(SealKeyInfo));
            return IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    /// <summary>Writes the trailer with the tag over everything added to the seal.</summary>
    /// <param name="output">The case file, after the database.</param>
    /// <param name="seal">The seal, from <see cref="Begin"/>.</param>
    /// <param name="hash">The hash of the whole file, which the trailer is added to; none when not wanted.</param>
    public static void WriteTrailer(Stream output, IncrementalHash seal, IncrementalHash? hash)
    {
        Span<byte> trailer = stackalloc byte[TrailerLength];
        Marker.CopyTo(trailer);
        seal.GetHashAndReset(trailer[Marker.Length..]);
        hash?.AppendData(trailer);
        output.Write(trailer);
    }

    /// <summary>Checks a trailer read from the end of a file against the seal over the bytes before it.</summary>
    /// <param name="trailer">The trailer as read.</param>
    /// <param name="seal">The seal over every byte before the trailer.</param>
    /// <returns><see langword="true"/> when the marker and the tag hold.</returns>
    public static bool Holds(ReadOnlySpan<byte> trailer, IncrementalHash seal)
    {
        Span<byte> expected = stackalloc byte[TagSize];
        seal.GetHashAndReset(expected);
        return trailer.Length == TrailerLength
            && trailer[..Marker.Length].SequenceEqual(Marker)
            && CryptographicOperations.FixedTimeEquals(trailer[Marker.Length..], expected);
    }
}
