using System.Globalization;
using System.Security.Cryptography;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// Moves the files of a working copy that does not belong to its case file (sketch ÄR1b): aside, unchanged, to a
/// folder of its own under <see cref="RecoveredFolder"/>, or over to a new case number. Nothing is ever removed; a
/// move that stops half-way is moved back.
/// </summary>
internal static class WorkingCopyBranches
{
    /// <summary>The folder under the cases folder where working copies that were set aside are kept.</summary>
    public const string RecoveredFolder = "Återställda";

    /// <summary>The letters tried for a new case number after the file's own, <c>-B</c> first.</summary>
    private const string BranchLetters = "BCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>The time in a set-aside folder's name, in UTC, safe in a file name on Windows.</summary>
    private const string StampFormat = "yyyyMMdd-HHmmss'Z'";

    /// <summary>A working copy that was set aside: its folder (relative to the cases folder) and its SHA-256.</summary>
    /// <param name="Folder">The folder, for example <c>Återställda/2026-0117 20261108-091200Z</c>.</param>
    /// <param name="Sha256">The working copy's SHA-256 in lower-case hexadecimal.</param>
    public sealed record SetAside(string Folder, string Sha256);

    /// <summary>Gives the working copy's own files that exist, the working copy first and its journal second.</summary>
    /// <param name="paths">The case's files.</param>
    /// <returns>The files' paths.</returns>
    private static List<string> WorkingFiles(CasePaths paths) =>
        [.. new[] { paths.WorkingCopy, paths.WorkingJournal, paths.WorkingBase, paths.WorkingCopyTemporary }
            .Where(File.Exists)];

    /// <summary>
    /// Moves the working copy and the files that belong to it to a new folder under <see cref="RecoveredFolder"/>,
    /// with their names, after hashing the working copy for the journal.
    /// </summary>
    /// <param name="paths">The case's files.</param>
    /// <param name="directory">The cases folder.</param>
    /// <param name="caseNumber">The case number, for the folder's name.</param>
    /// <param name="utcNow">The time now, for the folder's name.</param>
    /// <returns>Where they went and the working copy's hash.</returns>
    public static SetAside MoveAside(CasePaths paths, string directory, string caseNumber, DateTimeOffset utcNow)
    {
        var sha256 = HashOf(paths.WorkingCopy);
        var stamp = utcNow.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture);
        var name = FreeName(Path.Combine(directory, RecoveredFolder), $"{caseNumber} {stamp}");
        var folder = Path.Combine(directory, RecoveredFolder, name);
        Directory.CreateDirectory(folder);
        MoveAll([.. WorkingFiles(paths).Select(file => (file, Path.Combine(folder, Path.GetFileName(file))))]);
        return new SetAside($"{RecoveredFolder}/{name}", sha256);
    }

    /// <summary>
    /// Makes the working copy the working copy of a new case number: writes the new case file from the case file's
    /// header and the working copy as it lies (the opening that follows recovers it and saves it properly), records
    /// the new file as the working copy's base, and moves the working copy and its journal over.
    /// </summary>
    /// <param name="paths">The case's files.</param>
    /// <param name="branch">The new case number's files; none of them may exist.</param>
    /// <param name="header">The case file's header, with the envelopes the new case gets.</param>
    public static void MoveToBranch(CasePaths paths, CasePaths branch, CaseHeader header)
    {
        byte[] hash;
        using (var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            using (var output = new FileStream(branch.SaveFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                using (var headerBytes = new MemoryStream())
                {
                    CaseContainer.WriteHeader(headerBytes, header);
                    sha.AppendData(headerBytes.GetBuffer(), 0, (int)headerBytes.Length);
                    headerBytes.WriteTo(output);
                }

                using (var input = new FileStream(paths.WorkingCopy, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    CaseFiles.CopyHashing(input, output, sha);
                }

                output.Flush(flushToDisk: true);
            }

            hash = sha.GetHashAndReset();
        }

        try
        {
            // Never over a case file that appeared meanwhile.
            File.Move(branch.SaveFile, branch.CaseFile, overwrite: false);
        }
        catch
        {
            File.Delete(branch.SaveFile);
            throw;
        }

        CaseFiles.WriteBase(branch, [hash]);
        var moves = new List<(string From, string To)> { (paths.WorkingCopy, branch.WorkingCopy) };
        if (File.Exists(paths.WorkingJournal))
        {
            moves.Add((paths.WorkingJournal, branch.WorkingJournal));
        }

        try
        {
            MoveAll(moves);
        }
        catch
        {
            File.Delete(branch.WorkingBase);
            File.Delete(branch.CaseFile);
            throw;
        }
    }

    /// <summary>
    /// Undoes <see cref="MoveToBranch"/> when the new case could not be opened: the working copy and its journal go
    /// back, the case's base is written as it was, and the new case file and its base are removed (they hold nothing
    /// that is not in the working copy).
    /// </summary>
    /// <param name="paths">The case's files.</param>
    /// <param name="branch">The new case number's files.</param>
    /// <param name="caseBase">The hashes the case's base recorded before.</param>
    public static void MoveBack(CasePaths paths, CasePaths branch, IReadOnlyList<byte[]> caseBase)
    {
        var moves = new List<(string From, string To)> { (branch.WorkingCopy, paths.WorkingCopy) };
        if (File.Exists(branch.WorkingJournal))
        {
            moves.Add((branch.WorkingJournal, paths.WorkingJournal));
        }

        MoveAll(moves);
        if (caseBase.Count > 0)
        {
            CaseFiles.WriteBase(paths, caseBase);
        }

        File.Delete(branch.WorkingBase);
        File.Delete(branch.CaseFile);
    }

    /// <summary>
    /// Suggests a free case number for a working copy saved as a case of its own: the case number with <c>-B</c>,
    /// <c>-C</c> and so on, then <c>-B2</c>, and an empty text when none is free and valid.
    /// </summary>
    /// <param name="directory">The cases folder.</param>
    /// <param name="caseNumber">The case number of the file.</param>
    /// <returns>The suggestion, or an empty text.</returns>
    public static string SuggestBranchNumber(string directory, string caseNumber)
    {
        var candidates = BranchLetters.Select(letter => $"{caseNumber}-{letter}")
            .Concat(Enumerable.Range(2, 98).Select(number =>
                string.Create(CultureInfo.InvariantCulture, $"{caseNumber}-B{number}")));
        return candidates.FirstOrDefault(candidate =>
            Contracts.CaseNumberRules.IsValid(candidate) && IsFree(CasePaths.For(directory, candidate)))
            ?? string.Empty;
    }

    /// <summary>Tells whether a case number has neither a case file nor a working copy in the folder.</summary>
    /// <param name="paths">The case number's files.</param>
    /// <returns><see langword="true"/> when it is free.</returns>
    public static bool IsFree(CasePaths paths) =>
        !File.Exists(paths.CaseFile) && !File.Exists(paths.WorkingCopy) && !File.Exists(paths.WorkingJournal);

    /// <summary>Gives the SHA-256 of a file in lower-case hexadecimal.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The hash.</returns>
    private static string HashOf(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }

    /// <summary>Gives a folder name that is not taken yet, adding <c> (2)</c> and so on when it is.</summary>
    /// <param name="parent">The folder the name is in.</param>
    /// <param name="name">The name wanted.</param>
    /// <returns>A free name.</returns>
    private static string FreeName(string parent, string name)
    {
        var candidate = name;
        for (var number = 2; Directory.Exists(Path.Combine(parent, candidate)); number++)
        {
            candidate = string.Create(CultureInfo.InvariantCulture, $"{name} ({number})");
        }

        return candidate;
    }

    /// <summary>
    /// Moves files one by one, never over a file, and moves those already moved back when one fails, so that a
    /// working copy and its journal are never parted.
    /// </summary>
    /// <param name="moves">Where each file is and where it goes.</param>
    private static void MoveAll(IReadOnlyList<(string From, string To)> moves)
    {
        var done = new Stack<(string From, string To)>();
        try
        {
            foreach (var move in moves)
            {
                File.Move(move.From, move.To, overwrite: false);
                done.Push(move);
            }
        }
        catch
        {
            while (done.TryPop(out var move))
            {
                File.Move(move.To, move.From, overwrite: false);
            }

            throw;
        }
    }
}
