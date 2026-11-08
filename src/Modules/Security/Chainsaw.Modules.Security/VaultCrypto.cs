using System.Security.Cryptography;
using System.Text;
using Chainsaw.SharedKernel.Text;

namespace Chainsaw.Modules.Security;

/// <summary>
/// The cryptography behind the vault, in one place: PBKDF2-SHA512 for the master key, AES-256-GCM for the data key
/// and the content, and HKDF-SHA256 for keys derived from the data key.
/// </summary>
internal static class VaultCrypto
{
    /// <summary>The size of the data key and of every key derived for AES-256-GCM.</summary>
    public const int KeySize = 32;

    /// <summary>The size of a PBKDF2 salt.</summary>
    public const int SaltSize = 32;

    /// <summary>The size of an AES-GCM nonce.</summary>
    public const int NonceSize = 12;

    /// <summary>The size of an AES-GCM authentication tag.</summary>
    public const int TagSize = 16;

    /// <summary>The HKDF info for the key that encrypts the vault content, kept apart from every purpose key.</summary>
    private const string ContentKeyInfo = "chainsaw/vault/content-key/v1";

    /// <summary>The HKDF info prefix for keys that other modules ask for by purpose.</summary>
    private const string PurposeKeyInfoPrefix = "chainsaw/purpose/v1/";

    /// <summary>Allocates a buffer for key material that the garbage collector will not move or copy.</summary>
    /// <param name="length">The number of bytes.</param>
    /// <returns>A pinned array that the owner clears with <see cref="CryptographicOperations.ZeroMemory"/>.</returns>
    public static byte[] AllocateSecret(int length) => GC.AllocateArray<byte>(length, pinned: true);

    /// <summary>Creates a new random data key.</summary>
    /// <returns>A pinned 32-byte key.</returns>
    public static byte[] NewDataKey()
    {
        var key = AllocateSecret(KeySize);
        RandomNumberGenerator.Fill(key);
        return key;
    }

    /// <summary>Creates a new random salt for PBKDF2.</summary>
    /// <returns>A 32-byte salt.</returns>
    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    /// <summary>
    /// Derives the key that wraps the data key from the master key: in Unicode normalization form C when the
    /// parameters say so, so that the same key typed with another input method opens the vault, and otherwise as
    /// typed (a vault written before the normalization).
    /// </summary>
    /// <param name="masterKey">
    /// The master key; it is normalized into a pinned buffer that is cleared, and encoded as UTF-8 inside the
    /// derivation, and not kept.
    /// </param>
    /// <param name="kdf">The parameters stored in the vault file.</param>
    /// <param name="normalize">Whether to normalize, which overrides the parameters for the fallback of an old file.</param>
    /// <param name="destination">Receives the 32-byte wrapping key; the caller clears it.</param>
    public static void DeriveWrappingKey(
        ReadOnlySpan<char> masterKey, KdfParameters kdf, bool normalize, Span<byte> destination)
    {
        if (!normalize)
        {
            Rfc2898DeriveBytes.Pbkdf2(masterKey, kdf.Salt, destination, kdf.Iterations, HashAlgorithmName.SHA512);
            return;
        }

        var normalized = SecretText.Normalize(masterKey);
        try
        {
            Rfc2898DeriveBytes.Pbkdf2(normalized, kdf.Salt, destination, kdf.Iterations, HashAlgorithmName.SHA512);
        }
        finally
        {
            SecretText.Clear(normalized);
        }
    }

    /// <summary>Derives the key that encrypts the vault content from the data key.</summary>
    /// <param name="dataKey">The data key.</param>
    /// <param name="destination">Receives the 32-byte content key; the caller clears it.</param>
    public static void DeriveContentKey(ReadOnlySpan<byte> dataKey, Span<byte> destination) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, dataKey, destination, [], Encoding.ASCII.GetBytes(ContentKeyInfo));

    /// <summary>Derives a key for a named purpose from the data key.</summary>
    /// <param name="dataKey">The data key.</param>
    /// <param name="purpose">The validated purpose name.</param>
    /// <param name="destination">Receives the key; the caller clears it.</param>
    public static void DerivePurposeKey(ReadOnlySpan<byte> dataKey, string purpose, Span<byte> destination) =>
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            dataKey,
            destination,
            [],
            Encoding.ASCII.GetBytes(PurposeKeyInfoPrefix + purpose));

    /// <summary>Encrypts with AES-256-GCM under a fresh random nonce.</summary>
    /// <param name="key">The 32-byte key.</param>
    /// <param name="plaintext">The bytes to encrypt.</param>
    /// <param name="associatedData">Data that is authenticated but not encrypted, binding the box to its place.</param>
    /// <returns>The nonce, ciphertext and tag.</returns>
    public static SealedBox Seal(
        ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> associatedData)
    {
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, ciphertext, tag, associatedData);
        return new SealedBox(nonce, ciphertext, tag);
    }

    /// <summary>Decrypts and authenticates a box.</summary>
    /// <param name="key">The 32-byte key.</param>
    /// <param name="box">The box; its sizes must already have been checked.</param>
    /// <param name="associatedData">The same associated data that was given to <see cref="Seal"/>.</param>
    /// <param name="plaintext">Receives the plaintext; as long as the ciphertext.</param>
    /// <exception cref="AuthenticationTagMismatchException">Wrong key, or the box or its data changed.</exception>
    public static void Open(
        ReadOnlySpan<byte> key, SealedBox box, ReadOnlySpan<byte> associatedData, Span<byte> plaintext)
    {
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(box.Nonce, box.Ciphertext, box.Tag, plaintext, associatedData);
    }
}
