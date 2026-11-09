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
/// <item>The ephemeral P-384 private key is derived from the file key, the case id and the recovery key's
/// fingerprint (see <see cref="DeriveEphemeralScalar"/>), so that one case, file key and recovery key always give the
/// same envelope, and any other combination an unrelated one.</item>
/// <item>ECDH between the ephemeral private key and the recovery public key gives the shared secret (the
/// x-coordinate, 48 bytes).</item>
/// <item>HKDF-SHA384 turns the secret into a 32-byte AES key. The info binds the format, the kind, the case id, the
/// ephemeral public key and the recovery key's fingerprint, so that a key derived for one envelope is useless for
/// any other.</item>
/// <item>AES-256-GCM encrypts the file key with the same context as associated data, under a nonce derived like the
/// ephemeral key. The AES key is only ever used for this one file key, so a fixed nonce per key is never reused for
/// another message.</item>
/// </list>
/// <para>
/// The envelope stores the recovery key's fingerprint (so that a tool can say which key a file is open to, and the
/// application can see when the built-in key has been replaced), the ephemeral public key, the nonce, the encrypted
/// file key and the tag. The ephemeral private key and the shared secret are cleared as soon as the AES key exists.
/// </para>
/// <para>
/// Because the envelope is a function of the file key, anyone who has opened the case can make it again and compare
/// it byte for byte with the stored one (<see cref="Matches"/>), without the private key. A stored envelope that
/// wraps anything but the file key, even a well-formed one that someone with the file key wrote, never matches, and
/// Chainsaw writes the right one when the case is opened. The administrator's side is unchanged: the tool opens the
/// envelope with the private key and the ephemeral public key as stored.
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

    /// <summary>The HKDF info label of the ephemeral private key.</summary>
    private const string EphemeralLabel = "chainsaw/case/recovery-ephemeral/v2";

    /// <summary>The HKDF info label of the nonce.</summary>
    private const string NonceLabel = "chainsaw/case/recovery-nonce/v2";

    /// <summary>The length of a fingerprint in hexadecimal digits.</summary>
    private const int FingerprintLength = 64;

    /// <summary>The largest ephemeral key that is read; a P-384 SubjectPublicKeyInfo is 120 bytes.</summary>
    private const int MaximumEphemeralSize = 256;

    /// <summary>The size of a P-384 private scalar.</summary>
    private const int ScalarSize = 48;

    /// <summary>
    /// The order n of the P-384 base point, big-endian (FIPS 186-5 / SEC 2); a private scalar lies in 1..n-1.
    /// </summary>
    internal static readonly byte[] Order = Convert.FromHexString(
        "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFC7634D81F4372DDF581A0DB248B0A77AECEC196ACCC52973");

    /// <summary>Tests a file key that an envelope gave, before it is taken for the case's.</summary>
    /// <param name="fileKey">The key the envelope gave.</param>
    /// <returns><see langword="true"/> when it is the case's file key.</returns>
    public delegate bool FileKeyTest(ReadOnlySpan<byte> fileKey);

    /// <summary>
    /// Encrypts the file key to the recovery key. The result depends only on the arguments, so that the same call
    /// gives the same envelope byte for byte.
    /// </summary>
    /// <param name="caseId">The case id.</param>
    /// <param name="fileKey">The file key.</param>
    /// <param name="recoveryKey">The organisation's public recovery key.</param>
    /// <returns>The envelope.</returns>
    public static JsonObject Create(Guid caseId, ReadOnlySpan<byte> fileKey, RecoveryPublicKey recoveryKey)
    {
        ArgumentNullException.ThrowIfNull(recoveryKey);
        using var recipient = recoveryKey.CreateKey();
        using var ephemeral = DeriveEphemeralKey(caseId, fileKey, recoveryKey.Fingerprint);
        var ephemeralPublic = RecoveryPublicKey.FromPrivateKey(ephemeral).ExportSubjectPublicKeyInfo();
        var context = Context(caseId, recoveryKey.Fingerprint, ephemeralPublic);
        var nonce = new byte[CaseCrypto.NonceSize];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA384, fileKey, nonce, [], Label(NonceLabel, caseId, recoveryKey.Fingerprint));
        var ciphertext = new byte[fileKey.Length];
        var tag = new byte[CaseCrypto.TagSize];
        var wrappingKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            DeriveWrappingKey(ephemeral, recipient.PublicKey, context, wrappingKey);
            using var aes = new AesGcm(wrappingKey, CaseCrypto.TagSize);
            aes.Encrypt(nonce, fileKey, ciphertext, tag, context);
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

    /// <summary>
    /// Compares a stored envelope byte for byte, in constant time, with the one <see cref="Create"/> gives for the
    /// case's file key: only then does it hold the file key for that recovery key.
    /// </summary>
    /// <param name="stored">The stored envelope.</param>
    /// <param name="expected">The envelope made now.</param>
    /// <returns><see langword="true"/> when they are the same.</returns>
    public static bool Matches(JsonObject stored, JsonObject expected) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(stored.ToJsonString()), Encoding.UTF8.GetBytes(expected.ToJsonString()));

    /// <summary>
    /// Picks the envelopes to drop so that one more fits in a header of <see cref="CaseContainer.MaximumEnvelopes"/>,
    /// when it is full: first envelopes that nothing uses (another kind or construction, which anyone can write, and
    /// copies of the sharing envelope or of this vault's envelope, which are never read), then recovery envelopes for
    /// keys other than the configured one, then vault envelopes that <paramref name="isOwn"/> does not claim; each
    /// group from the end of the file. The sharing envelope (the first of its kind, the one that is opened) and this
    /// vault's first envelope are never picked, so that copies of them cannot keep the recovery envelope out.
    /// </summary>
    /// <param name="envelopes">The envelopes the header keeps.</param>
    /// <param name="fingerprint">The configured recovery key's fingerprint.</param>
    /// <param name="isOwn">Tells whether a vault envelope is one this vault opens.</param>
    /// <returns>The envelopes to drop; fewer than needed when the rest may not go.</returns>
    public static IReadOnlyList<JsonObject> ToMakeRoom(
        IReadOnlyList<JsonObject> envelopes, string fingerprint, Func<JsonObject, bool> isOwn)
    {
        ArgumentNullException.ThrowIfNull(isOwn);
        var needed = envelopes.Count + 1 - CaseContainer.MaximumEnvelopes;
        if (needed <= 0)
        {
            return [];
        }

        bool Operator(JsonObject envelope) => KeyEnvelopes.IsKind(envelope, KeyEnvelopes.OperatorKind);
        bool Known(JsonObject envelope) => Operator(envelope)
            || KeyEnvelopes.IsKind(envelope, KeyEnvelopes.PasswordKind) || FingerprintOf(envelope) is not null;
        var sharing = envelopes.FirstOrDefault(envelope => KeyEnvelopes.IsKind(envelope, KeyEnvelopes.PasswordKind));
        var own = envelopes.FirstOrDefault(envelope => Operator(envelope) && isOwn(envelope));
        bool Copy(JsonObject envelope) =>
            (KeyEnvelopes.IsKind(envelope, KeyEnvelopes.PasswordKind) && !ReferenceEquals(envelope, sharing))
            || (Operator(envelope) && !ReferenceEquals(envelope, own) && isOwn(envelope));
        var backwards = envelopes.Reverse().ToList();
        return
        [
            .. backwards.Where(envelope => !Known(envelope) || Copy(envelope))
                .Concat(backwards.Where(envelope => FingerprintOf(envelope) is { } other && other != fingerprint))
                .Concat(backwards.Where(envelope =>
                    KeyEnvelopes.IsKind(envelope, KeyEnvelopes.OperatorKind) && !isOwn(envelope)))
                .Take(needed),
        ];
    }

    /// <summary>Tries every recovery envelope made for the private key's public half.</summary>
    /// <param name="header">The header.</param>
    /// <param name="privateKey">The organisation's private recovery key.</param>
    /// <param name="fileKey">
    /// Receives the file key when an envelope opens and <paramref name="accept"/> takes it.
    /// </param>
    /// <param name="accept">
    /// Tells whether the key an envelope gave is the case's (the seal over the file holds under it). Anyone can write
    /// an envelope that opens with the private key and wraps another key, so that one must not hide a sound one.
    /// </param>
    /// <returns><see langword="true"/> when an envelope gave the case's file key.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Corrupt"/> when none did and an envelope was malformed or gave a key the file does not
    /// take.
    /// </exception>
    /// <exception cref="CryptographicException">The private key is not a P-384 key.</exception>
    /// <remarks>
    /// A damaged envelope does not stop the others from being tried; the file is only corrupt when no envelope for
    /// the key opens and one of them was malformed or opened to the wrong key.
    /// </remarks>
    public static bool TryOpen(CaseHeader header, ECDiffieHellman privateKey, Span<byte> fileKey, FileKeyTest accept)
    {
        ArgumentNullException.ThrowIfNull(accept);
        var fingerprint = RecoveryPublicKey.FromPrivateKey(privateKey).Fingerprint;
        CaseException? malformed = null;
        foreach (var envelope in Readable(header).Where(item => item.Fingerprint == fingerprint))
        {
            try
            {
                if (!TryOpenOne(header.CaseId, envelope.Envelope, privateKey, fingerprint, fileKey))
                {
                    continue;
                }

                if (accept(fileKey))
                {
                    return true;
                }

                CryptographicOperations.ZeroMemory(fileKey);
                malformed = new CaseException(CaseError.Corrupt);
            }
            catch (CaseException exception) when (exception.Error == CaseError.Corrupt)
            {
                CryptographicOperations.ZeroMemory(fileKey);
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
    /// Gives the header's envelopes without those of this construction for revoked keys, so that a compromised key
    /// no longer opens later copies of the file.
    /// </summary>
    /// <param name="header">The header.</param>
    /// <param name="revoked">The fingerprints of the revoked keys.</param>
    /// <returns>The envelopes that stay, and the fingerprints of those removed.</returns>
    public static (IReadOnlyList<JsonObject> Kept, IReadOnlyList<string> Removed) Without(
        CaseHeader header, IReadOnlySet<string> revoked)
    {
        var removed = Readable(header).Where(item => revoked.Contains(item.Fingerprint)).ToList();
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
            if (FingerprintOf(envelope) is { } fingerprint)
            {
                yield return (envelope, fingerprint);
            }
        }
    }

    /// <summary>
    /// Gives the fingerprint of a recovery envelope of this construction, or <see langword="null"/> for another kind,
    /// another construction or a malformed fingerprint.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <returns>The fingerprint, or <see langword="null"/>.</returns>
    public static string? FingerprintOf(JsonObject envelope) =>
        KeyEnvelopes.IsKind(envelope, Kind)
        && envelope[AlgorithmField] is JsonValue algorithm
        && algorithm.TryGetValue<string>(out var name) && name == Algorithm
        && envelope[FingerprintField] is JsonValue value
        && value.TryGetValue<string>(out var fingerprint)
        && fingerprint.Length == FingerprintLength && fingerprint.All(char.IsAsciiHexDigitLower)
            ? fingerprint
            : null;

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

    /// <summary>
    /// Makes the ephemeral key pair of an envelope from its private scalar (<see cref="DeriveEphemeralScalar"/>).
    /// </summary>
    /// <param name="caseId">The case id.</param>
    /// <param name="fileKey">The file key.</param>
    /// <param name="fingerprint">The recovery key's fingerprint.</param>
    /// <returns>The key pair; the caller disposes it.</returns>
    /// <remarks>
    /// Only the private scalar is imported. .NET computes the public point from it, on Windows (CNG) as on Linux
    /// (OpenSSL), since .NET 5; the round trip is checked by a test.
    /// </remarks>
    private static ECDiffieHellman DeriveEphemeralKey(Guid caseId, ReadOnlySpan<byte> fileKey, string fingerprint)
    {
        var scalar = CaseCrypto.AllocateSecret(ScalarSize);
        var key = ECDiffieHellman.Create();
        try
        {
            DeriveEphemeralScalar(caseId, fileKey, fingerprint, scalar);
            key.ImportParameters(new ECParameters { Curve = ECCurve.NamedCurves.nistP384, D = scalar });
            return key;
        }
        catch
        {
            key.Dispose();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(scalar);
        }
    }

    /// <summary>
    /// Derives the ephemeral private scalar d of an envelope: HKDF-SHA384 with the file key as input key material
    /// and the version label, the case id, the recovery key's fingerprint and a counter as info, 48 bytes read as a
    /// big-endian number, taken when 1 &lt;= d &lt;= n-1 and otherwise drawn again with the next counter (rejection
    /// sampling, as in FIPS 186-5 A.2.2 and RFC 6979). A value that is taken is uniform on 1..n-1, since every
    /// accepted value is equally likely; n is within 2^-190 of 2^384, so a second draw practically never happens.
    /// </summary>
    /// <param name="caseId">The case id.</param>
    /// <param name="fileKey">The file key.</param>
    /// <param name="fingerprint">The recovery key's fingerprint.</param>
    /// <param name="destination">Receives the 48-byte scalar.</param>
    internal static void DeriveEphemeralScalar(
        Guid caseId, ReadOnlySpan<byte> fileKey, string fingerprint, Span<byte> destination)
    {
        for (var counter = 0; ; counter++)
        {
            var info = Label(EphemeralLabel, caseId, fingerprint, counter);
            HKDF.DeriveKey(HashAlgorithmName.SHA384, fileKey, destination[..ScalarSize], [], info);
            if (IsScalar(destination[..ScalarSize]))
            {
                return;
            }
        }
    }

    /// <summary>
    /// Tells, without branching on the value, whether a 48-byte big-endian number lies in 1..n-1: it is not zero, and
    /// subtracting n from it borrows.
    /// </summary>
    /// <param name="value">The number.</param>
    /// <returns><see langword="true"/> when it is a valid private scalar.</returns>
    private static bool IsScalar(ReadOnlySpan<byte> value)
    {
        var borrow = 0;
        var any = 0;
        for (var index = ScalarSize - 1; index >= 0; index--)
        {
            var difference = value[index] - Order[index] - borrow;
            borrow = (difference >> 31) & 1;
            any |= value[index];
        }

        return (borrow & (any == 0 ? 0 : 1)) == 1;
    }

    /// <summary>The HKDF info for a value derived from the file key for one case and recovery key.</summary>
    /// <param name="label">The label of the value.</param>
    /// <param name="caseId">The case id.</param>
    /// <param name="fingerprint">The recovery key's fingerprint.</param>
    /// <param name="counter">The draw, for the ephemeral scalar.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Label(string label, Guid caseId, string fingerprint, int counter = 0) =>
        Encoding.UTF8.GetBytes(string.Create(
            System.Globalization.CultureInfo.InvariantCulture, $"{label}\n{caseId:D}\n{fingerprint}\n{counter}"));

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
