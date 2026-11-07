using System.Security.Cryptography;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// Copying between the case file and its working copy, and the record of which case file a working copy belongs to.
/// </summary>
internal static class CaseFiles
{
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
    /// <exception cref="Contracts.CaseException">
    /// <see cref="Contracts.CaseError.Corrupt"/> when the file changed in between.
    /// </exception>
    public static void Extract(CasePaths paths, Guid caseId)
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
            paths.WorkingCopy, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize))
        {
            CopyHashing(input, output, hash);
            output.Flush(flushToDisk: true);
        }

        WriteBase(paths, hash.GetHashAndReset());
    }

    /// <summary>Records the hash of the case file the working copy now belongs to.</summary>
    /// <param name="paths">The case's files.</param>
    /// <param name="caseFileHash">The SHA-256 of the case file.</param>
    public static void WriteBase(CasePaths paths, byte[] caseFileHash)
    {
        using var output = new FileStream(paths.WorkingBase, FileMode.Create, FileAccess.Write, FileShare.None);
        output.Write(caseFileHash);
        output.Flush(flushToDisk: true);
    }

    /// <summary>
    /// Checks that a working copy left by an interrupted session belongs to the case file as it is now: the case file
    /// has not been replaced since the working copy was taken from it or saved to it.
    /// </summary>
    /// <param name="paths">The case's files.</param>
    /// <returns><see langword="true"/> when the recorded hash is the case file's.</returns>
    public static bool BelongsTogether(CasePaths paths)
    {
        if (!File.Exists(paths.WorkingBase))
        {
            return false;
        }

        var recorded = File.ReadAllBytes(paths.WorkingBase);
        using var input = new FileStream(paths.CaseFile, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize);
        return CryptographicOperations.FixedTimeEquals(recorded, SHA256.HashData(input));
    }
}
