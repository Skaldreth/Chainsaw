using System.Text.Json;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security;

/// <summary>
/// Reads and writes the vault file. A write goes to a temporary file in the same folder, is flushed to disk and then
/// replaces the old file in one move, so a crash leaves either the old vault or the new one, never half of one.
/// </summary>
internal sealed class VaultFile
{
    /// <summary>The name of the vault file in the vault folder.</summary>
    public const string FileName = "vault.json";

    /// <summary>The suffix of the temporary file a write goes through.</summary>
    public const string TemporarySuffix = ".tmp";

    /// <summary>The JSON settings of the file: camelCase names, byte arrays as Base64.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    /// <summary>Creates the reader and writer for the vault file in a folder.</summary>
    /// <param name="directory">The folder that holds, or will hold, the vault file.</param>
    public VaultFile(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
        FilePath = Path.Combine(directory, FileName);
        TemporaryPath = FilePath + TemporarySuffix;
    }

    /// <summary>Gets the folder that holds the vault file.</summary>
    public string Directory { get; }

    /// <summary>Gets the full path of the vault file.</summary>
    public string FilePath { get; }

    /// <summary>Gets the full path of the temporary file a write goes through.</summary>
    public string TemporaryPath { get; }

    /// <summary>Gets whether the vault file exists.</summary>
    public bool Exists => File.Exists(FilePath);

    /// <summary>Reads and checks the vault file.</summary>
    /// <returns>The document, with the version, the algorithm and every size checked.</returns>
    /// <exception cref="VaultException">
    /// <see cref="VaultError.NotCreated"/>, <see cref="VaultError.UnsupportedVersion"/> or
    /// <see cref="VaultError.Corrupt"/>.
    /// </exception>
    public VaultDocument Read()
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(FilePath);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new VaultException(VaultError.NotCreated, exception);
        }

        return Parse(bytes);
    }

    /// <summary>Writes the document atomically through the temporary file.</summary>
    /// <param name="document">The document to write.</param>
    /// <exception cref="IOException">The file could not be written; the old file is left as it was.</exception>
    public void Write(VaultDocument document)
    {
        System.IO.Directory.CreateDirectory(Directory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        try
        {
            using (var stream = new FileStream(TemporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(TemporaryPath, FilePath, overwrite: true);
        }
        catch
        {
            DeleteTemporaryFile();
            throw;
        }
    }

    /// <summary>Parses and checks the bytes of a vault file.</summary>
    /// <param name="bytes">The file's bytes.</param>
    /// <returns>The checked document.</returns>
    /// <exception cref="VaultException">
    /// <see cref="VaultError.UnsupportedVersion"/> or <see cref="VaultError.Corrupt"/>.
    /// </exception>
    internal static VaultDocument Parse(byte[] bytes)
    {
        try
        {
            var header = JsonSerializer.Deserialize<VaultHeader>(bytes, JsonOptions);
            if (header?.Format != VaultDocument.FormatName)
            {
                throw new VaultException(VaultError.Corrupt);
            }

            if (header.Version != VaultDocument.CurrentVersion)
            {
                throw new VaultException(VaultError.UnsupportedVersion);
            }

            var document = JsonSerializer.Deserialize<VaultDocument>(bytes, JsonOptions);
            if (document is null || !IsWellFormed(document))
            {
                throw new VaultException(VaultError.Corrupt);
            }

            return document;
        }
        catch (JsonException exception)
        {
            throw new VaultException(VaultError.Corrupt, exception);
        }
    }

    /// <summary>Checks that every field the cryptography relies on is present and has the right size.</summary>
    /// <param name="document">The parsed document; its fields may be null when the file lacked them.</param>
    /// <returns><see langword="true"/> when the document can be handed to the cryptography.</returns>
    private static bool IsWellFormed(VaultDocument document) =>
        document.Kdf is { Algorithm: KdfParameters.AlgorithmName, Salt.Length: VaultCrypto.SaltSize } kdf
        && kdf.Iterations is > 0 and <= KdfParameters.MaximumIterations
        && IsWellFormed(document.DataKey) && document.DataKey.Ciphertext.Length == VaultCrypto.KeySize
        && IsWellFormed(document.Content);

    /// <summary>Checks the sizes of an encrypted box.</summary>
    /// <param name="box">The box; it or its fields may be null when the file lacked them.</param>
    /// <returns><see langword="true"/> when the nonce and tag have their sizes and the ciphertext is present.</returns>
    private static bool IsWellFormed(SealedBox? box) =>
        box is { Nonce.Length: VaultCrypto.NonceSize, Tag.Length: VaultCrypto.TagSize, Ciphertext: not null };

    /// <summary>Removes a temporary file left by a failed write, if it is a file and can be removed.</summary>
    private void DeleteTemporaryFile()
    {
        try
        {
            if (File.Exists(TemporaryPath))
            {
                File.Delete(TemporaryPath);
            }
        }
        catch (IOException)
        {
            // The write has already failed; the leftover file is overwritten by the next write.
        }
        catch (UnauthorizedAccessException)
        {
            // As above: nothing more can be done here.
        }
    }
}
