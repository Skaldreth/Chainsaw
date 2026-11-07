using System.Security.Cryptography;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// The folder the open Chainsaw and the background process share (<c>%LOCALAPPDATA%\Chainsaw\Bevakning</c>), and
/// writing and reading its files in one step: a file is written whole under another name, flushed to the disk and
/// then moved into place, so that a crash leaves either the old file or the new one, never half of one.
/// </summary>
/// <param name="directory">The folder.</param>
/// <param name="protector">Protects the files for the Windows user (DPAPI).</param>
internal sealed class BackgroundFolder(string directory, IUserDataProtector protector)
{
    /// <summary>Gets the folder.</summary>
    public string Directory { get; } = directory;

    /// <summary>Gives a path in the folder.</summary>
    /// <param name="parts">The parts under the folder.</param>
    /// <returns>The path.</returns>
    public string PathOf(params string[] parts) => Path.Combine([Directory, .. parts]);

    /// <summary>Protects bytes and writes them in one step.</summary>
    /// <param name="path">The file.</param>
    /// <param name="purpose">What the data is for, bound to the protection.</param>
    /// <param name="data">The bytes; they are wiped afterwards.</param>
    public void WriteProtected(string path, string purpose, byte[] data)
    {
        try
        {
            WriteWhole(path, protector.Protect(data, purpose));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }

    /// <summary>Reads a protected file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="purpose">The purpose it was protected for.</param>
    /// <returns>The bytes, or <see langword="null"/> when there is no file.</returns>
    /// <exception cref="InvalidDataException">It cannot be read or unprotected.</exception>
    public byte[]? ReadProtected(string path, string purpose)
    {
        byte[] stored;
        try
        {
            stored = File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }

        try
        {
            return protector.Unprotect(stored, purpose);
        }
        catch (CryptographicException exception)
        {
            throw new InvalidDataException("A file of the background watching cannot be read.", exception);
        }
    }

    /// <summary>Writes bytes whole, flushed through to the disk, and moves them into place.</summary>
    /// <param name="path">The file.</param>
    /// <param name="bytes">The bytes.</param>
    public static void WriteWhole(string path, byte[] bytes)
    {
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            using (var stream = new FileStream(
                temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
