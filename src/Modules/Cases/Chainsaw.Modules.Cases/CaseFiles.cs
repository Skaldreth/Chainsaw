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
    /// Copies the database out of a case file into a new working copy and records the case file's hash, in one pass,
    /// checking the seal over the whole file (<see cref="CaseFileSeal"/>) before the working copy gets its name.
    /// </summary>
    /// <param name="paths">The case's files.</param>
    /// <param name="caseId">The case id the key was opened for; the header must still say it.</param>
    /// <param name="fileKey">The file key, which the seal is derived from.</param>
    /// <returns>The SHA-256 of the case file the working copy was taken from.</returns>
    /// <exception cref="Contracts.CaseException">
    /// <see cref="Contracts.CaseError.Corrupt"/> when the file changed in between, is cut short or its seal does not
    /// hold.
    /// </exception>
    public static byte[] Extract(CasePaths paths, Guid caseId, ReadOnlySpan<byte> fileKey)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var seal = CaseFileSeal.Begin(fileKey, caseId);
        using var input = new FileStream(paths.CaseFile, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
        if (CaseContainer.ReadHeader(input).CaseId != caseId)
        {
            throw new Contracts.CaseException(Contracts.CaseError.Corrupt);
        }

        var headerLength = (int)input.Position;
        var databaseLength = input.Length - headerLength - CaseFileSeal.TrailerLength;
        if (databaseLength <= 0)
        {
            throw new Contracts.CaseException(Contracts.CaseError.Corrupt);
        }

        input.Position = 0;
        var header = new byte[headerLength];
        input.ReadExactly(header);
        hash.AppendData(header);
        seal.AppendData(header);
        bool holds;
        using (var output = new FileStream(
            paths.WorkingCopyTemporary, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize))
        {
            CopySealing(input, output, databaseLength, hash, seal);
            var trailer = new byte[CaseFileSeal.TrailerLength];
            input.ReadExactly(trailer);
            hash.AppendData(trailer);
            holds = CaseFileSeal.Holds(trailer, seal);
            output.Flush(flushToDisk: true);
        }

        if (!holds)
        {
            // A file whose seal does not hold was changed by someone without the file key, or is damaged; nothing of
            // it becomes a working copy.
            File.Delete(paths.WorkingCopyTemporary);
            throw new Contracts.CaseException(Contracts.CaseError.Corrupt);
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

    /// <summary>
    /// Writes a whole case file: the header, the database and the seal over both, adding every byte written to a
    /// hash when one is given.
    /// </summary>
    /// <param name="output">The new file, at its start.</param>
    /// <param name="header">The header.</param>
    /// <param name="database">The database, at its start.</param>
    /// <param name="fileKey">The file key, which the seal is derived from.</param>
    /// <param name="hash">The hash of the whole file; none when not wanted.</param>
    public static void Write(
        Stream output, CaseHeader header, Stream database, ReadOnlySpan<byte> fileKey, IncrementalHash? hash)
    {
        using var seal = CaseFileSeal.Begin(fileKey, header.CaseId);
        using (var buffer = new MemoryStream())
        {
            CaseContainer.WriteHeader(buffer, header);
            hash?.AppendData(buffer.GetBuffer(), 0, (int)buffer.Length);
            seal.AppendData(buffer.GetBuffer(), 0, (int)buffer.Length);
            buffer.WriteTo(output);
        }

        CopySealing(database, output, long.MaxValue, hash, seal);
        CaseFileSeal.WriteTrailer(output, seal, hash);
    }

    /// <summary>Copies up to a number of bytes, adding them to the seal and, when given, to a hash.</summary>
    /// <param name="input">The stream to copy from.</param>
    /// <param name="output">The stream to copy to.</param>
    /// <param name="length">How many bytes to copy; <see cref="long.MaxValue"/> copies to the end.</param>
    /// <param name="hash">The hash, or none.</param>
    /// <param name="seal">The seal.</param>
    /// <exception cref="Contracts.CaseException">
    /// <see cref="Contracts.CaseError.Corrupt"/> when a known length ends early.
    /// </exception>
    private static void CopySealing(
        Stream input, Stream output, long length, IncrementalHash? hash, IncrementalHash seal)
    {
        var buffer = new byte[BufferSize];
        var left = length;
        while (left > 0)
        {
            var read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, left));
            if (read == 0)
            {
                if (length != long.MaxValue)
                {
                    throw new Contracts.CaseException(Contracts.CaseError.Corrupt);
                }

                return;
            }

            hash?.AppendData(buffer, 0, read);
            seal.AppendData(buffer, 0, read);
            output.Write(buffer, 0, read);
            left -= read;
        }
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
