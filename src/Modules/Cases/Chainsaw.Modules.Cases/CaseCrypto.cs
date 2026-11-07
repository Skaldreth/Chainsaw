using System.Security.Cryptography;
using System.Text;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The cryptography of a case file, in one place: the random file key, AES-256-GCM for the key envelopes,
/// PBKDF2-SHA512 for a sharing password and HKDF-SHA256 for the database key derived from the file key.
/// </summary>
internal static class CaseCrypto
{
    /// <summary>The size of the file key and of every key derived from it.</summary>
    public const int KeySize = 32;

    /// <summary>The size of a PBKDF2 salt.</summary>
    public const int SaltSize = 32;

    /// <summary>The size of an AES-GCM nonce.</summary>
    public const int NonceSize = 12;

    /// <summary>The size of an AES-GCM tag.</summary>
    public const int TagSize = 16;

    /// <summary>The purpose name for the key that wraps this vault's envelopes (see <c>IPurposeKeys</c>).</summary>
    public const string EnvelopePurpose = "cases/envelope";

    /// <summary>The HKDF info for the key SQLite3 Multiple Ciphers encrypts the database with.</summary>
    private const string DatabaseKeyInfo = "chainsaw/case/database-key/v1";

    /// <summary>Allocates a buffer for key material that the garbage collector will not move or copy.</summary>
    /// <param name="length">The number of bytes.</param>
    /// <returns>A pinned array that the owner clears with <see cref="CryptographicOperations.ZeroMemory"/>.</returns>
    public static byte[] AllocateSecret(int length) => GC.AllocateArray<byte>(length, pinned: true);

    /// <summary>Creates a new random file key.</summary>
    /// <returns>A pinned 32-byte key.</returns>
    public static byte[] NewFileKey()
    {
        var key = AllocateSecret(KeySize);
        RandomNumberGenerator.Fill(key);
        return key;
    }

    /// <summary>Derives the key that wraps an envelope from a sharing password.</summary>
    /// <param name="password">The password; it is encoded as UTF-8 inside the derivation and not kept.</param>
    /// <param name="salt">The envelope's salt.</param>
    /// <param name="iterations">The envelope's iteration count.</param>
    /// <param name="destination">Receives the 32-byte key; the caller clears it.</param>
    public static void DerivePasswordKey(
        ReadOnlySpan<char> password, ReadOnlySpan<byte> salt, int iterations, Span<byte> destination) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, destination, iterations, HashAlgorithmName.SHA512);

    /// <summary>
    /// Derives the database key from the file key and the case id, so that the file key itself is only used for
    /// deriving and the database key is bound to its case.
    /// </summary>
    /// <param name="fileKey">The file key.</param>
    /// <param name="caseId">The case id from the header.</param>
    /// <param name="destination">Receives the 32-byte key; the caller clears it.</param>
    public static void DeriveDatabaseKey(ReadOnlySpan<byte> fileKey, Guid caseId, Span<byte> destination) =>
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            fileKey,
            destination,
            caseId.ToByteArray(),
            Encoding.ASCII.GetBytes(DatabaseKeyInfo));

    /// <summary>Encrypts a key with AES-256-GCM under a fresh random nonce.</summary>
    /// <param name="wrappingKey">The 32-byte key that wraps.</param>
    /// <param name="key">The key to wrap.</param>
    /// <param name="associatedData">Data that binds the envelope to its case and kind.</param>
    /// <returns>The nonce, the wrapped key and the tag.</returns>
    public static (byte[] Nonce, byte[] Ciphertext, byte[] Tag) Wrap(
        ReadOnlySpan<byte> wrappingKey, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[key.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(wrappingKey, TagSize);
        aes.Encrypt(nonce, key, ciphertext, tag, associatedData);
        return (nonce, ciphertext, tag);
    }

    /// <summary>Decrypts and authenticates a wrapped key.</summary>
    /// <param name="wrappingKey">The 32-byte key that wraps.</param>
    /// <param name="nonce">The envelope's nonce.</param>
    /// <param name="ciphertext">The wrapped key.</param>
    /// <param name="tag">The envelope's tag.</param>
    /// <param name="associatedData">The same associated data as when it was wrapped.</param>
    /// <param name="key">Receives the key; as long as the ciphertext.</param>
    /// <returns><see langword="false"/> when the wrapping key is wrong or the envelope was changed.</returns>
    public static bool TryUnwrap(
        ReadOnlySpan<byte> wrappingKey,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> tag,
        ReadOnlySpan<byte> associatedData,
        Span<byte> key)
    {
        try
        {
            using var aes = new AesGcm(wrappingKey, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, key, associatedData);
            return true;
        }
        catch (AuthenticationTagMismatchException)
        {
            CryptographicOperations.ZeroMemory(key);
            return false;
        }
    }

    /// <summary>
    /// Writes a 32-byte key as SQLite3 Multiple Ciphers' raw key literal (<c>x'…'</c>) into a pinned buffer, so that
    /// the key never becomes a string.
    /// </summary>
    /// <param name="key">The database key.</param>
    /// <returns>The ASCII literal in a pinned buffer that the caller clears.</returns>
    public static byte[] ToRawKeyLiteral(ReadOnlySpan<byte> key)
    {
        const string HexDigits = "0123456789abcdef";
        var literal = AllocateSecret(3 + (key.Length * 2));
        literal[0] = (byte)'x';
        literal[1] = (byte)'\'';
        for (var index = 0; index < key.Length; index++)
        {
            literal[2 + (index * 2)] = (byte)HexDigits[key[index] >> 4];
            literal[3 + (index * 2)] = (byte)HexDigits[key[index] & 0x0F];
        }

        literal[^1] = (byte)'\'';
        return literal;
    }
}
