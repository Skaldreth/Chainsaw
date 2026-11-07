using System.Security.Cryptography;
using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Recovery;

/// <summary>
/// The key pair's files. The private key is written as an encrypted PKCS #8 PEM (PBES2 with PBKDF2-HMAC-SHA512 and
/// AES-256-CBC), which OpenSSL and other tools can also read; the public key as a plain <c>PUBLIC KEY</c> PEM.
/// Neither file is ever overwritten.
/// </summary>
internal static class KeyFiles
{
    /// <summary>The name of the private key's file.</summary>
    public const string PrivateFileName = "chainsaw-recovery.private.pem";

    /// <summary>The name of the public key's file.</summary>
    public const string PublicFileName = "chainsaw-recovery.public.pem";

    /// <summary>The shortest password that protects the private key.</summary>
    public const int MinimumPasswordLength = 16;

    /// <summary>The PBKDF2 iterations that protect the private key (the same as for the master key).</summary>
    public const int Iterations = 600_000;

    /// <summary>The largest key file that is read; a key file is well under a kilobyte.</summary>
    private const long MaximumFileSize = 64 * 1024;

    /// <summary>The PEM label of an encrypted private key.</summary>
    private const string EncryptedPrivateKeyLabel = "ENCRYPTED PRIVATE KEY";

    /// <summary>Creates a new P-384 key pair.</summary>
    /// <returns>The key; the caller disposes it.</returns>
    public static ECDiffieHellman NewKey() => ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);

    /// <summary>Writes the key pair to two new files in a folder; neither may exist.</summary>
    /// <param name="directory">The folder.</param>
    /// <param name="key">The key pair.</param>
    /// <param name="password">The password that protects the private key.</param>
    /// <returns>The two files' paths.</returns>
    /// <exception cref="IOException">A file exists already or cannot be written.</exception>
    public static (string PrivatePath, string PublicPath) Write(
        string directory, ECDiffieHellman key, ReadOnlySpan<char> password)
    {
        var privatePath = Path.Combine(directory, PrivateFileName);
        var publicPath = Path.Combine(directory, PublicFileName);
        var privatePem = key.ExportEncryptedPkcs8PrivateKeyPem(
            password, new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA512, Iterations));
        var publicPem = RecoveryPublicKey.FromPrivateKey(key).ExportPem();

        // The public key goes first: if the private key then cannot be written, nothing is left that depends on it.
        WriteNew(publicPath, publicPem, ownerOnly: false);
        try
        {
            WriteNew(privatePath, privatePem, ownerOnly: true);
        }
        catch
        {
            File.Delete(publicPath);
            throw;
        }

        return (privatePath, publicPath);
    }

    /// <summary>Reads a public key file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The key, or <see langword="null"/> when the file has no public key.</returns>
    /// <exception cref="CryptographicException">The file's public key is not a P-384 key.</exception>
    public static RecoveryPublicKey? ReadPublic(string path) => RecoveryPublicKey.FromPem(ReadText(path));

    /// <summary>Reads and decrypts the private key.</summary>
    /// <param name="path">The file.</param>
    /// <param name="password">The password.</param>
    /// <returns>The key; the caller disposes it.</returns>
    /// <exception cref="CryptographicException">
    /// The password is wrong, or the file holds no encrypted P-384 private key.
    /// </exception>
    public static ECDiffieHellman ReadPrivate(string path, ReadOnlySpan<char> password)
    {
        var text = ReadText(path);
        if (!text.Contains($"-----BEGIN {EncryptedPrivateKeyLabel}-----", StringComparison.Ordinal))
        {
            throw new CryptographicException("The file holds no encrypted private key.");
        }

        var key = ECDiffieHellman.Create();
        try
        {
            key.ImportFromEncryptedPem(text, password);

            // Throws when the key is not on P-384.
            _ = RecoveryPublicKey.FromPrivateKey(key);
            return key;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            // For example two keys in one file: reported like any other unreadable key.
            key.Dispose();
            throw new CryptographicException("The file does not hold exactly one encrypted private key.", exception);
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    /// <summary>Reads a small text file.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The text.</returns>
    /// <exception cref="IOException">The file is too large to be a key.</exception>
    private static string ReadText(string path)
    {
        var info = new FileInfo(path);
        if (info.Exists && info.Length > MaximumFileSize)
        {
            throw new IOException($"The file {path} is too large to be a key.");
        }

        return File.ReadAllText(path);
    }

    /// <summary>Writes a new file; an existing one is never replaced.</summary>
    /// <param name="path">The file.</param>
    /// <param name="text">The text.</param>
    /// <param name="ownerOnly">
    /// Whether only the owner may read it (on Unix; on Windows the file inherits the folder's rights).
    /// </param>
    private static void WriteNew(string path, string text, bool ownerOnly)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (ownerOnly && !OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        using var stream = new FileStream(path, options);
        using var writer = new StreamWriter(stream);
        writer.Write(text);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }
}
