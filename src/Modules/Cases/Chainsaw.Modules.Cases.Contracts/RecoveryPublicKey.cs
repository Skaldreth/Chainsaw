using System.Formats.Asn1;
using System.Security.Cryptography;

namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// The organisation's public recovery key: a P-384 key that every case file gets an envelope for, so that the
/// administrator, who alone has the private key, can open a case whose operator has lost the master key.
/// </summary>
/// <remarks>
/// <para>
/// The key is kept in a canonical form (a DER <c>SubjectPublicKeyInfo</c> with the named curve and the uncompressed
/// point), whatever form it was read in, so that its fingerprint is the same on every platform and in every tool.
/// </para>
/// <para>
/// The fingerprint is the SHA-256 of that canonical form. It is not secret; it says which key an envelope was made
/// for, and the administrator compares it with what Chainsaw shows.
/// </para>
/// </remarks>
public sealed class RecoveryPublicKey : IEquatable<RecoveryPublicKey>
{
    /// <summary>The PEM label of a public key.</summary>
    public const string PemLabel = "PUBLIC KEY";

    /// <summary>The object identifier of an elliptic curve public key (RFC 5480).</summary>
    private const string EcPublicKeyOid = "1.2.840.10045.2.1";

    /// <summary>The object identifier of the curve P-384 (secp384r1).</summary>
    private const string P384Oid = "1.3.132.0.34";

    /// <summary>The size of a P-384 coordinate.</summary>
    private const int CoordinateSize = 48;

    /// <summary>The size of an uncompressed P-384 point: the prefix 0x04 and both coordinates.</summary>
    private const int PointSize = 1 + (2 * CoordinateSize);

    /// <summary>The prefix of an uncompressed point.</summary>
    private const byte UncompressedPrefix = 0x04;

    /// <summary>The canonical DER encoding.</summary>
    private readonly byte[] _subjectPublicKeyInfo;

    /// <summary>Keeps a checked canonical key.</summary>
    /// <param name="subjectPublicKeyInfo">The canonical DER encoding.</param>
    private RecoveryPublicKey(byte[] subjectPublicKeyInfo)
    {
        _subjectPublicKeyInfo = subjectPublicKeyInfo;
        Fingerprint = Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo));
    }

    /// <summary>Gets the fingerprint: the SHA-256 of the canonical key as 64 lowercase hexadecimal digits.</summary>
    public string Fingerprint { get; }

    /// <summary>
    /// Gets the fingerprint for people to compare: uppercase, in sixteen groups of four digits.
    /// </summary>
    public string DisplayFingerprint => FormatFingerprint(Fingerprint);

    /// <summary>Reads a public key from its DER <c>SubjectPublicKeyInfo</c>.</summary>
    /// <param name="subjectPublicKeyInfo">The DER bytes.</param>
    /// <returns>The key in canonical form.</returns>
    /// <exception cref="CryptographicException">
    /// The bytes are not a P-384 public key with an uncompressed point on the curve.
    /// </exception>
    public static RecoveryPublicKey FromSubjectPublicKeyInfo(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        var point = ReadPoint(subjectPublicKeyInfo);
        var canonical = WriteSubjectPublicKeyInfo(point);

        // Importing checks that the point lies on the curve.
        using var key = ECDiffieHellman.Create();
        key.ImportSubjectPublicKeyInfo(canonical, out _);
        return new RecoveryPublicKey(canonical);
    }

    /// <summary>
    /// Reads a public key from PEM text. Text around the PEM block (for example a comment) is ignored, and text with
    /// no public key in it gives <see langword="null"/>, which means that no key is configured.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The key, or <see langword="null"/> when the text has no <c>PUBLIC KEY</c> block.</returns>
    /// <exception cref="CryptographicException">
    /// The block is not a P-384 public key, or the text holds more than one.
    /// </exception>
    public static RecoveryPublicKey? FromPem(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var begin = $"-----BEGIN {PemLabel}-----";
        var first = text.IndexOf(begin, StringComparison.Ordinal);
        if (first < 0)
        {
            return null;
        }

        if (text.IndexOf(begin, first + begin.Length, StringComparison.Ordinal) >= 0)
        {
            // Two keys would leave it to chance which one cases are made for.
            throw new CryptographicException("The text holds more than one public key.");
        }

        var fields = PemEncoding.Find(text);
        var label = text.AsSpan()[fields.Label];
        if (!label.SequenceEqual(PemLabel))
        {
            throw new CryptographicException("The PEM block is not a public key.");
        }

        var der = Convert.FromBase64String(text[fields.Base64Data]);
        return FromSubjectPublicKeyInfo(der);
    }

    /// <summary>Gives the public half of a P-384 private key.</summary>
    /// <param name="privateKey">The private key.</param>
    /// <returns>The public key in canonical form.</returns>
    /// <exception cref="CryptographicException">The key is not a P-384 key.</exception>
    public static RecoveryPublicKey FromPrivateKey(ECDiffieHellman privateKey)
    {
        ArgumentNullException.ThrowIfNull(privateKey);
        return FromSubjectPublicKeyInfo(privateKey.ExportSubjectPublicKeyInfo());
    }

    /// <summary>Formats a fingerprint for people: uppercase, in groups of four digits.</summary>
    /// <param name="fingerprint">The fingerprint as hexadecimal digits.</param>
    /// <returns>The formatted fingerprint.</returns>
    public static string FormatFingerprint(string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);
        const int GroupSize = 4;
        var upper = fingerprint.ToUpperInvariant();
        return string.Join(' ', upper.Chunk(GroupSize).Select(group => new string(group)));
    }

    /// <summary>Gives a copy of the canonical DER <c>SubjectPublicKeyInfo</c>.</summary>
    /// <returns>The bytes.</returns>
    public byte[] ExportSubjectPublicKeyInfo() => (byte[])_subjectPublicKeyInfo.Clone();

    /// <summary>Gives the key as PEM text.</summary>
    /// <returns>The PEM text.</returns>
    public string ExportPem() => PemEncoding.WriteString(PemLabel, _subjectPublicKeyInfo);

    /// <summary>Creates an ECDH key object with this public key, for key agreement.</summary>
    /// <returns>The key; the caller disposes it.</returns>
    public ECDiffieHellman CreateKey()
    {
        var key = ECDiffieHellman.Create();
        key.ImportSubjectPublicKeyInfo(_subjectPublicKeyInfo, out _);
        return key;
    }

    /// <inheritdoc />
    public bool Equals(RecoveryPublicKey? other) =>
        other is not null && string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as RecoveryPublicKey);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Fingerprint);

    /// <inheritdoc />
    public override string ToString() => DisplayFingerprint;

    /// <summary>
    /// Reads the uncompressed point out of a <c>SubjectPublicKeyInfo</c> and checks the algorithm and the curve, so
    /// that no other curve or point encoding is accepted.
    /// </summary>
    /// <param name="subjectPublicKeyInfo">The DER bytes.</param>
    /// <returns>The uncompressed point.</returns>
    /// <exception cref="CryptographicException">The encoding does not hold.</exception>
    private static byte[] ReadPoint(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        try
        {
            var reader = new AsnReader(subjectPublicKeyInfo.ToArray(), AsnEncodingRules.DER);
            var info = reader.ReadSequence();
            reader.ThrowIfNotEmpty();
            var algorithm = info.ReadSequence();
            var algorithmOid = algorithm.ReadObjectIdentifier();
            var curveOid = algorithm.ReadObjectIdentifier();
            algorithm.ThrowIfNotEmpty();
            var point = info.ReadBitString(out var unusedBits);
            info.ThrowIfNotEmpty();
            if (algorithmOid != EcPublicKeyOid || curveOid != P384Oid || unusedBits != 0
                || point.Length != PointSize || point[0] != UncompressedPrefix)
            {
                throw new CryptographicException("The key is not a P-384 public key with an uncompressed point.");
            }

            return point;
        }
        catch (AsnContentException exception)
        {
            throw new CryptographicException("The key is not a valid SubjectPublicKeyInfo.", exception);
        }
    }

    /// <summary>Writes the canonical <c>SubjectPublicKeyInfo</c> of a P-384 point.</summary>
    /// <param name="point">The uncompressed point.</param>
    /// <returns>The DER bytes.</returns>
    private static byte[] WriteSubjectPublicKeyInfo(byte[] point)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(EcPublicKeyOid);
                writer.WriteObjectIdentifier(P384Oid);
            }

            writer.WriteBitString(point);
        }

        return writer.Encode();
    }
}
