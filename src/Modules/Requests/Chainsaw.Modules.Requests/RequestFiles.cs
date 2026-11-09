using System.Security.Cryptography;
using System.Text;

namespace Chainsaw.Modules.Requests;

/// <summary>
/// Writes a request's file and never over another (CLAUDE.md, BG1b): when the name is taken, the new file gets a
/// number, <c>(2)</c>, <c>(3)</c> and so on, and every file is created with <see cref="FileMode.CreateNew"/>, so that
/// not even a file made at the same moment by another program is written over.
/// </summary>
internal static class RequestFiles
{
    /// <summary>The most numbers tried before the folder is given up on.</summary>
    public const int MostNumbers = 999;

    /// <summary>The file's encoding: UTF-8 without a byte order mark, which every program reads.</summary>
    private static readonly UTF8Encoding FileEncoding = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>Writes a document's Markdown to a new file, as <see cref="WriteNew"/> does, and hashes it.</summary>
    /// <param name="path">Where the user wants the file.</param>
    /// <param name="markdown">The document's text.</param>
    /// <returns>Where it was written, and the SHA-256 of its bytes in lower-case hexadecimal.</returns>
    /// <exception cref="ArgumentException">The path names no file.</exception>
    /// <exception cref="IOException">The file could not be written, or every number is taken.</exception>
    public static (string Path, string Sha256) WriteMarkdown(string path, string markdown)
    {
        var bytes = FileEncoding.GetBytes(markdown);
        return (WriteNew(path, bytes), Convert.ToHexStringLower(SHA256.HashData(bytes)));
    }

    /// <summary>Writes the bytes to a new file at the path, or at the path with the first free number.</summary>
    /// <param name="path">Where the user wants the file.</param>
    /// <param name="content">The file's bytes.</param>
    /// <returns>Where it was written.</returns>
    /// <exception cref="ArgumentException">The path names no file.</exception>
    /// <exception cref="IOException">
    /// The file could not be written, or every number up to <see cref="MostNumbers"/> is taken.
    /// </exception>
    private static string WriteNew(string path, ReadOnlySpan<byte> content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        var folder = Path.GetDirectoryName(full);
        var name = Path.GetFileNameWithoutExtension(full);
        if (folder is null || name.Length == 0)
        {
            throw new ArgumentException("The path names no file.", nameof(path));
        }

        var extension = Path.GetExtension(full);
        for (var number = 1; number <= MostNumbers; number++)
        {
            var candidate = number == 1 ? full : Path.Combine(folder, $"{name} ({number}){extension}");
            if (TryCreate(candidate, content))
            {
                return candidate;
            }
        }

        throw new IOException($"Every name from {name}{extension} to ({MostNumbers}) is taken.");
    }

    /// <summary>Creates the file with the bytes, unless a file already has the name.</summary>
    /// <param name="path">The file.</param>
    /// <param name="content">The bytes.</param>
    /// <returns><see langword="false"/> when the name was taken.</returns>
    private static bool TryCreate(string path, ReadOnlySpan<byte> content)
    {
        if (File.Exists(path) || Directory.Exists(path))
        {
            return false;
        }

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException) when (File.Exists(path))
        {
            // Made by someone else since the check: take the next number rather than write over it.
            return false;
        }

        try
        {
            using (stream)
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
        }
        catch
        {
            // A half-written file is no request and must not be left (principle 1); the failure is the answer.
            TryDelete(path);
            throw;
        }

        return true;
    }

    /// <summary>Removes a file this export created; a failure leaves it, since nothing more can be done.</summary>
    /// <param name="path">The file.</param>
    public static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The export's own failure is what the user is told.
        }
    }
}
