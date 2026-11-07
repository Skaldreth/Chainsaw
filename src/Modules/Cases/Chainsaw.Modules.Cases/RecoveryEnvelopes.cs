using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The envelopes for the organisation's recovery key: the file key encrypted to a P-384 public key with ECIES, so
/// that only the holder of the private key can open it and the application never needs that key.
/// </summary>
/// <remarks>
/// <para>The construction (ECIES in the form of SEC 1 and ISO 18033-2, built from .NET's own primitives):</para>
/// <list type="number">
/// <item>A new ephemeral P-384 key pair is made for every envelope.</item>
/// <item>ECDH between the ephemeral private key and the recovery public key gives the shared secret (the
/// x-coordinate, 48 bytes).</item>
/// <item>HKDF-SHA384 turns the secret into a 32-byte AES key. The info binds the format, the kind, the case id, the
/// ephemeral public key and the recovery key's fingerprint, so that a key derived for one envelope is useless for
/// any other.</item>
/// <item>AES-256-GCM encrypts the file key under a random nonce, with the same context as associated data.</item>
/// </list>
/// <para>
/// The envelope stores the recovery key's fingerprint (so that a tool can say which key a file is open to, and the
/// application can see when the built-in key has been replaced), the ephemeral public key, the nonce, the encrypted
/// file key and the tag. The ephemeral private key and the shared secret are cleared as soon as the AES key exists.
/// </para>
/// </remarks>
internal static class RecoveryEnvelopes
{
    /// <summary>The kind of a recovery envelope.</summary>
    public const string Kind = "recovery";

    /// <summary>The name of the construction, stored in the envelope so that a later one can be told apart.</summary>
    public const string Algorithm = "ecies-p384-hkdf-sha384-aes256gcm";

    /// <summary>The field with the construction.</summary>
    private const string AlgorithmField = "alg";

    /// <summary>The field with the recovery key's fingerprint.</summary>
    private const string FingerprintField = "fingerprint";

    /// <summary>The field with the ephemeral public key (DER <c>SubjectPublicKeyInfo</c>).</summary>
    private const string EphemeralField = "ephemeral";

    /// <summary>The field with the nonce.</summary>
    private const string NonceField = "nonce";

    /// <summary>The field with the encrypted file key.</summary>
    private const string KeyField = "key";

    /// <summary>The field with the tag.</summary>
    private const string TagField = "tag";

    /// <summary>The length of a fingerprint in hexadecimal digits.</summary>
    private const int FingerprintLength = 64;

    /// <summary>The largest ephemeral key that is read; a P-384 SubjectPublicKeyInfo is 120 bytes.</summary>
    private const int MaximumEphemeralSize = 256;

    /// <summary>Encrypts the file key to the recovery key.</summary>
    /// <param name="caseId">The case id.</param>
    /// <param name="fileKey">The file key.</param>
    /// <param name="recoveryKey">The organisation's public recovery key.</param>
    /// <returns>The envelope.</returns>
    public static JsonObject Create(Guid caseId, ReadOnlySpan<byte> fileKey, RecoveryPublicKey recoveryKey)
    {
        ArgumentNullException.ThrowIfNull(recoveryKey);
        using var recipient = recoveryKey.CreateKey();
        using var ephemeral = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        var ephemeralPublic = RecoveryPublicKey.FromPrivateKey(ephemeral).ExportSubjectPublicKeyInfo();
        var context = Context(caseId, recoveryKey.Fingerprint, ephemeralPublic);
        var wrappingKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            DeriveWrappingKey(ephemeral, recipient.PublicKey, context, wrappingKey);
            var (nonce, ciphertext, tag) = CaseCrypto.Wrap(wrappingKey, fileKey, context);
            return new JsonObject
            {
                [KeyEnvelopes.KindField] = Kind,
                [AlgorithmField] = Algorithm,
                [FingerprintField] = recoveryKey.Fingerprint,
                [EphemeralField] = Convert.ToBase64String(ephemeralPublic),
                [NonceField] = Convert.ToBase64String(nonce),
                [KeyField] = Convert.ToBase64String(ciphertext),
                [TagField] = Convert.ToBase64String(tag),
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>Tries every recovery envelope made for the private key's public half.</summary>
    /// <param name="header">The header.</param>
    /// <param name="privateKey">The organisation's private recovery key.</param>
    /// <param name="fileKey">Receives the file key when an envelope opens.</param>
    /// <returns><see langword="true"/> when an envelope opened.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when such an envelope is malformed.</exception>
    /// <exception cref="CryptographicException">The private key is not a P-384 key.</exception>
    /// <remarks>
    /// A damaged envelope does not stop the others from being tried; the file is only corrupt when no envelope for
    /// the key opens and one of them was malformed.
    /// </remarks>
    public static bool TryOpen(CaseHeader header, ECDiffieHellman privateKey, Span<byte> fileKey)
    {
        var fingerprint = RecoveryPublicKey.FromPrivateKey(privateKey).Fingerprint;
        CaseException? malformed = null;
        foreach (var envelope in Readable(header).Where(item => item.Fingerprint == fingerprint))
        {
            try
            {
                if (TryOpenOne(header.CaseId, envelope.Envelope, privateKey, fingerprint, fileKey))
                {
                    return true;
                }
            }
            catch (CaseException exception) when (exception.Error == CaseError.Corrupt)
            {
                malformed = exception;
            }
        }

        return malformed is null ? false : throw malformed;
    }

    /// <summary>
    /// Gives the fingerprints of the recovery keys the header has envelopes of this construction for. Recovery
    /// envelopes of another construction (a later format) or with a malformed fingerprint are left out and kept as
    /// they are, like envelopes of unknown kinds, so that they never stop a case from opening.
    /// </summary>
    /// <param name="header">The header.</param>
    /// <returns>The fingerprints, in file order, without repeats.</returns>
    public static IReadOnlyList<string> Fingerprints(CaseHeader header) =>
        [.. Readable(header).Select(item => item.Fingerprint).Distinct(StringComparer.Ordinal)];

    /// <summary>
    /// Gives the header without the envelopes of this construction for other keys than the given one, so that a
    /// replaced (perhaps compromised) key no longer opens later copies of the file.
    /// </summary>
    /// <param name="header">The header.</param>
    /// <param name="fingerprint">The fingerprint of the key that stays.</param>
    /// <returns>The envelopes that stay, and the fingerprints of those removed.</returns>
    public static (IReadOnlyList<JsonObject> Kept, IReadOnlyList<string> Removed) WithoutOtherKeys(
        CaseHeader header, string fingerprint)
    {
        var removed = Readable(header).Where(item => item.Fingerprint != fingerprint).ToList();
        var kept = header.Envelopes.Where(envelope => !removed.Any(item => ReferenceEquals(item.Envelope, envelope)));
        return ([.. kept], [.. removed.Select(item => item.Fingerprint).Distinct(StringComparer.Ordinal)]);
    }

    /// <summary>Gives the recovery envelopes of this construction with a well-formed fingerprint.</summary>
    /// <param name="header">The header.</param>
    /// <returns>The envelopes and their fingerprints, in file order.</returns>
    private static IEnumerable<(JsonObject Envelope, string Fingerprint)> Readable(CaseHeader header)
    {
        foreach (var envelope in KeyEnvelopes.OfKind(header, Kind))
        {
            if (envelope[AlgorithmField] is JsonValue algorithm
                && algorithm.TryGetValue<string>(out var name) && name == Algorithm
                && envelope[FingerprintField] is JsonValue value
                && value.TryGetValue<string>(out var fingerprint)
                && fingerprint.Length == FingerprintLength && fingerprint.All(char.IsAsciiHexDigitLower))
            {
                yield return (envelope, fingerprint);
            }
        }
    }

    /// <summary>Opens one envelope.</summary>
    /// <param name="caseId">The case id.</param>
    /// <param name="envelope">The envelope.</param>
    /// <param name="privateKey">The private recovery key.</param>
    /// <param name="fingerprint">The private key's fingerprint.</param>
    /// <param name="fileKey">Receives the file key.</param>
    /// <returns><see langword="true"/> when it opened.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when the envelope is malformed.</exception>
    private static bool TryOpenOne(
        Guid caseId, JsonObject envelope, ECDiffieHellman privateKey, string fingerprint, Span<byte> fileKey)
    {
        var ephemeralBytes = ReadBytes(envelope, EphemeralField);
        RecoveryPublicKey ephemeralKey;
        try
        {
            ephemeralKey = RecoveryPublicKey.FromSubjectPublicKeyInfo(ephemeralBytes);
        }
        catch (CryptographicException exception)
        {
            throw new CaseException(CaseError.Corrupt, exception);
        }

        // The context uses the bytes as stored, so that an envelope whose key was re-encoded does not open.
        var context = Context(caseId, fingerprint, ephemeralBytes);
        var nonce = ReadBytes(envelope, NonceField, CaseCrypto.NonceSize);
        var ciphertext = ReadBytes(envelope, KeyField, CaseCrypto.KeySize);
        var tag = ReadBytes(envelope, TagField, CaseCrypto.TagSize);
        using var ephemeral = ephemeralKey.CreateKey();
        var wrappingKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            DeriveWrappingKey(privateKey, ephemeral.PublicKey, context, wrappingKey);
            return CaseCrypto.TryUnwrap(wrappingKey, nonce, ciphertext, tag, context, fileKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>Derives the AES key from the ECDH secret with HKDF-SHA384, and clears the secret.</summary>
    /// <param name="own">The own private key (the ephemeral key or the recovery key).</param>
    /// <param name="other">The other side's public key.</param>
    /// <param name="context">The context the key is bound to.</param>
    /// <param name="destination">Receives the 32-byte key.</param>
    private static void DeriveWrappingKey(
        ECDiffieHellman own, ECDiffieHellmanPublicKey other, byte[] context, Span<byte> destination)
    {
        var secret = own.DeriveRawSecretAgreement(other);
        try
        {
            HKDF.DeriveKey(HashAlgorithmName.SHA384, secret, destination, [], context);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    /// <summary>
    /// The context of an envelope, used both as HKDF info and as AES-GCM associated data: the format, the kind, the
    /// case id, the recovery key's fingerprint and the ephemeral public key.
    /// </summary>
    /// <param name="caseId">The case id.</param>
    /// <param name="fingerprint">The recovery key's fingerprint.</param>
    /// <param name="ephemeralPublic">The ephemeral public key as stored.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Context(Guid caseId, string fingerprint, byte[] ephemeralPublic) =>
        Encoding.UTF8.GetBytes(
            $"chainsaw/case/envelope/v1\n{Kind}\n{Algorithm}\n{caseId:D}\n{fingerprint}\n"
            + Convert.ToBase64String(ephemeralPublic));

    /// <summary>Reads a string field.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="name">The field.</param>
    /// <returns>The value; empty when it is missing.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when it is not a string.</exception>
    private static string ReadString(JsonObject envelope, string name)
    {
        try
        {
            return envelope[name]?.GetValue<string>() ?? string.Empty;
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            throw new CaseException(CaseError.Corrupt, exception);
        }
    }

    /// <summary>Reads a Base64 field, of an exact size when one is given.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="name">The field.</param>
    /// <param name="size">The size it must have, or <see langword="null"/> for any size up to a limit.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Corrupt"/> when it is missing or has another size.
    /// </exception>
    private static byte[] ReadBytes(JsonObject envelope, string name, int? size = null)
    {
        try
        {
            var bytes = Convert.FromBase64String(ReadString(envelope, name));
            var fits = size is { } exact ? bytes.Length == exact : bytes.Length is > 0 and <= MaximumEphemeralSize;
            return fits ? bytes : throw new CaseException(CaseError.Corrupt);
        }
        catch (FormatException exception)
        {
            throw new CaseException(CaseError.Corrupt, exception);
        }
    }
}
