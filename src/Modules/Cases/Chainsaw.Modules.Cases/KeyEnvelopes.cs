using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The key envelopes in a case file's header. Each envelope holds the file key wrapped with AES-256-GCM, with
/// associated data that binds it to its kind, its case and (for a password) its derivation parameters.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>operator</c>: wrapped with this vault's purpose key <see cref="CaseCrypto.EnvelopePurpose"/>. A file may
/// hold several (one per vault that has been given access); they carry no identifier, so a file does not show which
/// vault opens it, and opening tries each.</item>
/// <item><c>password</c>: the sharing envelope, wrapped with a key derived from a password with PBKDF2-SHA512. At most
/// one.</item>
/// <item>Any other kind is kept as it is for later formats (for example a recovery key).</item>
/// </list>
/// </remarks>
internal static class KeyEnvelopes
{
    /// <summary>The kind of an envelope for a vault.</summary>
    public const string OperatorKind = "operator";

    /// <summary>The kind of the sharing envelope.</summary>
    public const string PasswordKind = "password";

    /// <summary>The field with an envelope's kind.</summary>
    public const string KindField = "kind";

    /// <summary>The field with the nonce.</summary>
    private const string NonceField = "nonce";

    /// <summary>The field with the wrapped file key.</summary>
    public const string KeyField = "key";

    /// <summary>The field with the tag.</summary>
    private const string TagField = "tag";

    /// <summary>The field with a sharing envelope's key derivation.</summary>
    private const string KdfField = "kdf";

    /// <summary>The field with a sharing envelope's iteration count.</summary>
    private const string IterationsField = "iterations";

    /// <summary>The field with a sharing envelope's salt.</summary>
    private const string SaltField = "salt";

    /// <summary>The key derivation of a sharing envelope.</summary>
    private const string PasswordKdf = "pbkdf2-sha512";

    /// <summary>
    /// The highest iteration count that is accepted (a few seconds of PBKDF2), so that a crafted file cannot stall
    /// the application.
    /// </summary>
    private const int MaximumIterations = 2_000_000;

    /// <summary>Wraps the file key for a vault, with the operator's name encrypted under the file key.</summary>
    /// <param name="caseId">The case id.</param>
    /// <param name="fileKey">The file key.</param>
    /// <param name="wrappingKey">The vault's purpose key.</param>
    /// <param name="name">The operator's name (see <see cref="EnvelopeLabels"/>), or none.</param>
    /// <returns>The envelope.</returns>
    public static JsonObject CreateOperator(
        Guid caseId, ReadOnlySpan<byte> fileKey, ReadOnlySpan<byte> wrappingKey, string? name)
    {
        var (nonce, ciphertext, tag) = CaseCrypto.Wrap(wrappingKey, fileKey, OperatorData(caseId));
        var envelope = new JsonObject
        {
            [KindField] = OperatorKind,
            [NonceField] = Convert.ToBase64String(nonce),
            [KeyField] = Convert.ToBase64String(ciphertext),
            [TagField] = Convert.ToBase64String(tag),
        };
        EnvelopeLabels.Add(envelope, caseId, fileKey, name);
        return envelope;
    }

    /// <summary>Wraps the file key with a sharing password under a new salt.</summary>
    /// <param name="caseId">The case id.</param>
    /// <param name="fileKey">The file key.</param>
    /// <param name="password">The password.</param>
    /// <param name="iterations">The PBKDF2 iteration count.</param>
    /// <returns>The envelope.</returns>
    public static JsonObject CreatePassword(
        Guid caseId, ReadOnlySpan<byte> fileKey, ReadOnlySpan<char> password, int iterations)
    {
        var salt = RandomNumberGenerator.GetBytes(CaseCrypto.SaltSize);
        var wrappingKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            CaseCrypto.DerivePasswordKey(password, salt, iterations, wrappingKey);
            var (nonce, ciphertext, tag) = CaseCrypto.Wrap(
                wrappingKey, fileKey, PasswordData(caseId, iterations, salt));
            return new JsonObject
            {
                [KindField] = PasswordKind,
                [KdfField] = PasswordKdf,
                [IterationsField] = iterations,
                [SaltField] = Convert.ToBase64String(salt),
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

    /// <summary>Tries every vault envelope with this vault's purpose key.</summary>
    /// <param name="header">The header.</param>
    /// <param name="wrappingKey">The vault's purpose key.</param>
    /// <param name="fileKey">Receives the file key when an envelope opens.</param>
    /// <returns><see langword="true"/> when an envelope opened.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when an envelope is malformed.</exception>
    public static bool TryOpenOperator(CaseHeader header, ReadOnlySpan<byte> wrappingKey, Span<byte> fileKey)
    {
        var associatedData = OperatorData(header.CaseId);
        foreach (var envelope in OfKind(header, OperatorKind))
        {
            var (nonce, ciphertext, tag) = ReadSealed(envelope);
            if (CaseCrypto.TryUnwrap(wrappingKey, nonce, ciphertext, tag, associatedData, fileKey))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Tries one vault envelope with a vault's purpose key. A malformed envelope does not open, so that describing the
    /// envelopes never fails on one of them.
    /// </summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="caseId">The case id.</param>
    /// <param name="wrappingKey">The vault's purpose key.</param>
    /// <param name="fileKey">Receives the file key when the envelope opens.</param>
    /// <returns><see langword="true"/> when the envelope opened.</returns>
    public static bool TryOpenOne(
        JsonObject envelope, Guid caseId, ReadOnlySpan<byte> wrappingKey, Span<byte> fileKey)
    {
        if (!IsKind(envelope, OperatorKind))
        {
            return false;
        }

        try
        {
            var (nonce, ciphertext, tag) = ReadSealed(envelope);
            return CaseCrypto.TryUnwrap(wrappingKey, nonce, ciphertext, tag, OperatorData(caseId), fileKey);
        }
        catch (CaseException exception) when (exception.Error == CaseError.Corrupt)
        {
            return false;
        }
    }

    /// <summary>Tries the sharing envelope with a password.</summary>
    /// <param name="header">The header.</param>
    /// <param name="password">The password.</param>
    /// <param name="fileKey">Receives the file key when the envelope opens.</param>
    /// <returns><see langword="true"/> when the envelope opened.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when the envelope is malformed.</exception>
    public static bool TryOpenPassword(CaseHeader header, ReadOnlySpan<char> password, Span<byte> fileKey)
    {
        var envelope = OfKind(header, PasswordKind).FirstOrDefault();
        if (envelope is null)
        {
            return false;
        }

        var (iterations, salt) = ReadKdf(envelope);
        var (nonce, ciphertext, tag) = ReadSealed(envelope);
        var wrappingKey = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            CaseCrypto.DerivePasswordKey(password, salt, iterations, wrappingKey);
            return CaseCrypto.TryUnwrap(
                wrappingKey, nonce, ciphertext, tag, PasswordData(header.CaseId, iterations, salt), fileKey);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(wrappingKey);
        }
    }

    /// <summary>Gives the envelopes of one kind.</summary>
    /// <param name="header">The header.</param>
    /// <param name="kind">The kind.</param>
    /// <returns>The envelopes, in file order.</returns>
    public static IEnumerable<JsonObject> OfKind(CaseHeader header, string kind) =>
        header.Envelopes.Where(envelope => IsKind(envelope, kind));

    /// <summary>Checks an envelope's kind.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="kind">The kind.</param>
    /// <returns><see langword="true"/> when the envelope is of that kind.</returns>
    public static bool IsKind(JsonObject envelope, string kind) =>
        string.Equals(envelope[KindField]?.GetValue<string>(), kind, StringComparison.Ordinal);

    /// <summary>The associated data of a vault envelope.</summary>
    /// <param name="caseId">The case id.</param>
    /// <returns>The bytes.</returns>
    private static byte[] OperatorData(Guid caseId) =>
        Encoding.UTF8.GetBytes($"chainsaw/case/envelope/v1\n{OperatorKind}\n{caseId:D}");

    /// <summary>The associated data of a sharing envelope, which also binds the derivation parameters.</summary>
    /// <param name="caseId">The case id.</param>
    /// <param name="iterations">The iteration count.</param>
    /// <param name="salt">The salt.</param>
    /// <returns>The bytes.</returns>
    private static byte[] PasswordData(Guid caseId, int iterations, byte[] salt) =>
        Encoding.UTF8.GetBytes(string.Create(
            CultureInfo.InvariantCulture,
            $"chainsaw/case/envelope/v1\n{PasswordKind}\n{caseId:D}\n{PasswordKdf}\n{iterations}\n"
            + $"{Convert.ToBase64String(salt)}"));

    /// <summary>Reads and checks the nonce, wrapped key and tag of an envelope.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <returns>The three values.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Corrupt"/> when one is missing or has the wrong size.
    /// </exception>
    private static (byte[] Nonce, byte[] Ciphertext, byte[] Tag) ReadSealed(JsonObject envelope)
    {
        var nonce = ReadBytes(envelope, NonceField, CaseCrypto.NonceSize);
        var ciphertext = ReadBytes(envelope, KeyField, CaseCrypto.KeySize);
        var tag = ReadBytes(envelope, TagField, CaseCrypto.TagSize);
        return (nonce, ciphertext, tag);
    }

    /// <summary>Reads and checks the derivation parameters of a sharing envelope.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <returns>The iteration count and the salt.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when they do not hold.</exception>
    private static (int Iterations, byte[] Salt) ReadKdf(JsonObject envelope)
    {
        try
        {
            var kdf = envelope[KdfField]?.GetValue<string>();
            var iterations = envelope[IterationsField]?.GetValue<int>() ?? 0;
            if (!string.Equals(kdf, PasswordKdf, StringComparison.Ordinal)
                || iterations is < 1 or > MaximumIterations)
            {
                throw new CaseException(CaseError.Corrupt);
            }

            return (iterations, ReadBytes(envelope, SaltField, CaseCrypto.SaltSize));
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            throw new CaseException(CaseError.Corrupt, exception);
        }
    }

    /// <summary>Reads a Base64 field of an exact size.</summary>
    /// <param name="envelope">The envelope.</param>
    /// <param name="name">The field.</param>
    /// <param name="size">The size it must have.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Corrupt"/> when it is missing or has another size.
    /// </exception>
    private static byte[] ReadBytes(JsonObject envelope, string name, int size)
    {
        try
        {
            var bytes = Convert.FromBase64String(envelope[name]?.GetValue<string>() ?? string.Empty);
            return bytes.Length == size ? bytes : throw new CaseException(CaseError.Corrupt);
        }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException)
        {
            throw new CaseException(CaseError.Corrupt, exception);
        }
    }
}
