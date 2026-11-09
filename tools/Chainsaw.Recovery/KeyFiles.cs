using System.Formats.Asn1;
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

    /// <summary>
    /// The most PBKDF2 iterations a key file may ask for (a few seconds), so that a crafted file cannot stall the tool.
    /// </summary>
    private const int MaximumIterations = 2_000_000;

    /// <summary>The object identifier of PBES2 (PKCS #5).</summary>
    private const string Pbes2Oid = "1.2.840.113549.1.5.13";

    /// <summary>The object identifier of PBKDF2 (PKCS #5).</summary>
    private const string Pbkdf2Oid = "1.2.840.113549.1.5.12";

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

        RequireSaneIterations(text);
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

    /// <summary>
    /// Checks, before anything is decrypted, that every encrypted private key in a text is protected the way this tool
    /// writes it (PBES2 with PBKDF2) with no more iterations than <see cref="MaximumIterations"/>.
    /// </summary>
    /// <param name="text">The file's text.</param>
    /// <exception cref="CryptographicException">
    /// A key is protected otherwise or asks for too many iterations.
    /// </exception>
    private static void RequireSaneIterations(string text)
    {
        var rest = text.AsMemory();
        while (PemEncoding.TryFind(rest.Span, out var fields))
        {
            if (rest.Span[fields.Label].SequenceEqual(EncryptedPrivateKeyLabel.AsSpan()))
            {
                try
                {
                    var der = Convert.FromBase64String(rest.Span[fields.Base64Data].ToString());
                    var algorithm = new AsnReader(der, AsnEncodingRules.BER).ReadSequence().ReadSequence();
                    var scheme = algorithm.ReadObjectIdentifier();
                    var derivation = algorithm.ReadSequence().ReadSequence();
                    if (scheme != Pbes2Oid || derivation.ReadObjectIdentifier() != Pbkdf2Oid)
                    {
                        throw new CryptographicException("The private key is not protected with PBES2 and PBKDF2.");
                    }

                    var parameters = derivation.ReadSequence();
                    parameters.ReadEncodedValue();
                    if (!parameters.TryReadInt32(out var iterations) || iterations is < 1 or > MaximumIterations)
                    {
                        throw new CryptographicException("The private key asks for an unreasonable iteration count.");
                    }
                }
                catch (Exception exception) when (exception is AsnContentException or FormatException)
                {
                    throw new CryptographicException("The private key cannot be read.", exception);
                }
            }

            rest = rest[fields.Location.End..];
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

    /// <summary>Creates a new file; on Unix, readable only by the owner when asked.</summary>
    /// <param name="path">The file.</param>
    /// <param name="ownerOnly">Whether only the owner may read it (on Unix).</param>
    /// <returns>The stream.</returns>
    private static FileStream Create(string path, bool ownerOnly)
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

        return new FileStream(path, options);
    }

    /// <summary>
    /// Creates a new file on Windows with an access list that is protected from the folder's and gives the current
    /// user alone full control, set when the file is created so that it is never readable by others, not even for a
    /// moment.
    /// </summary>
    /// <param name="path">The file.</param>
    /// <returns>The stream.</returns>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static FileStream CreateOwnerOnlyOnWindows(string path)
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var owner = identity.User ?? throw new UnauthorizedAccessException("The current user has no security id.");
        var security = new System.Security.AccessControl.FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(owner);
        security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
            owner,
            System.Security.AccessControl.FileSystemRights.FullControl,
            System.Security.AccessControl.AccessControlType.Allow));
        return new FileInfo(path).Create(
            FileMode.CreateNew,
            System.Security.AccessControl.FileSystemRights.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.None,
            security);
    }

    /// <summary>Writes a new file; an existing one is never replaced.</summary>
    /// <param name="path">The file.</param>
    /// <param name="text">The text.</param>
    /// <param name="ownerOnly">
    /// Whether only the owner may read it: on Unix with the file's mode, on Windows with an access list of its own
    /// that gives the current user alone full control and inherits nothing from the folder.
    /// </param>
    private static void WriteNew(string path, string text, bool ownerOnly)
    {
        using var stream = ownerOnly && OperatingSystem.IsWindows()
            ? CreateOwnerOnlyOnWindows(path)
            : Create(path, ownerOnly);
        using var writer = new StreamWriter(stream);
        writer.Write(text);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }
}
