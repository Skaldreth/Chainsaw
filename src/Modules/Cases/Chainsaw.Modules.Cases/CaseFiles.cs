using System.Security.Cryptography;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// Copying between the case file and its working copy, and the record of which case file a working copy belongs to.
/// </summary>
internal static class CaseFiles
{
    /// <summary>The size of a SHA-256 hash.</summary>
    private const int HashSize = 32;

    /// <summary>The size of the copy buffer.</summary>
    private const int BufferSize = 81920;

    /// <summary>Copies a stream to its end and adds every byte copied to a hash.</summary>
    /// <param name="input">The stream to copy from.</param>
    /// <param name="output">The stream to copy to.</param>
    /// <param name="hash">The hash to add to.</param>
    public static void CopyHashing(Stream input, Stream output, IncrementalHash hash)
    {
        var buffer = new byte[BufferSize];
        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            output.Write(buffer, 0, read);
        }
    }

    /// <summary>
    /// Copies the database out of a case file into a new working copy and records the case file's hash, in one pass.
    /// </summary>
    /// <param name="paths">The case's files.</param>
    /// <param name="caseId">The case id the key was opened for; the header must still say it.</param>
    /// <returns>The SHA-256 of the case file the working copy was taken from.</returns>
    /// <exception cref="Contracts.CaseException">
    /// <see cref="Contracts.CaseError.Corrupt"/> when the file changed in between.
    /// </exception>
    public static byte[] Extract(CasePaths paths, Guid caseId)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var input = new FileStream(paths.CaseFile, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
        if (CaseContainer.ReadHeader(input).CaseId != caseId)
        {
            throw new Contracts.CaseException(Contracts.CaseError.Corrupt);
        }

        var headerLength = (int)input.Position;
        input.Position = 0;
        var header = new byte[headerLength];
        input.ReadExactly(header);
        hash.AppendData(header);
        using (var output = new FileStream(
            paths.WorkingCopyTemporary, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
        {
            CopyHashing(input, output, hash);
            output.Flush(flushToDisk: true);
        }

        // The base is written before the working copy gets its name, so that a working copy always has its base.
        var caseFileHash = hash.GetHashAndReset();
        WriteBase(paths, [caseFileHash]);

        // A rollback journal left without its working copy (one removed by hand after a conflict) would be taken
        // for a hot journal of the new copy, and SQLite would write another session's pages into it.
        File.Delete(paths.WorkingJournal);
        File.Move(paths.WorkingCopyTemporary, paths.WorkingCopy);
        return caseFileHash;
    }

    /// <summary>Records the hashes of the case files the working copy belongs to.</summary>
    /// <param name="paths">The case's files.</param>
    /// <param name="caseFileHashes">
    /// The SHA-256 of the case file now and, during a save, of the file that is about to replace it.
    /// </param>
    public static void WriteBase(CasePaths paths, IEnumerable<byte[]> caseFileHashes)
    {
        // Written aside and moved into place, so that a crash never leaves a half-written base.
        var temporary = paths.WorkingBase + ".tmp";
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            foreach (var hash in caseFileHashes)
            {
                output.Write(hash);
            }

            output.Flush(flushToDisk: true);
        }

        File.Move(temporary, paths.WorkingBase, overwrite: true);
    }

    /// <summary>Reads the recorded hashes; none when the base is missing or malformed.</summary>
    /// <param name="paths">The case's files.</param>
    /// <returns>The hashes.</returns>
    public static IReadOnlyList<byte[]> ReadBase(CasePaths paths)
    {
        if (!File.Exists(paths.WorkingBase))
        {
            return [];
        }

        var bytes = File.ReadAllBytes(paths.WorkingBase);
        return bytes.Length is 0 || bytes.Length % HashSize != 0 ? [] : [.. bytes.Chunk(HashSize)];
    }

    /// <summary>
    /// Checks that a working copy left by an interrupted session belongs to the case file as it is now: the case file
    /// is the one the working copy was taken from or last saved to, or the one a save was writing when it stopped.
    /// </summary>
    /// <param name="paths">The case's files.</param>
    /// <returns>The case file's SHA-256 when it is recorded; otherwise <see langword="null"/>.</returns>
    public static byte[]? MatchingBase(CasePaths paths)
    {
        var recorded = ReadBase(paths);
        if (recorded.Count == 0)
        {
            return null;
        }

        using var input = new FileStream(paths.CaseFile, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
        var actual = SHA256.HashData(input);
        return recorded.Any(hash => CryptographicOperations.FixedTimeEquals(hash, actual)) ? actual : null;
    }
}
