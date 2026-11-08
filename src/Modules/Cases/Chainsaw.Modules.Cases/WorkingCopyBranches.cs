using System.Globalization;
using System.Security.Cryptography;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// Moves the files of a working copy that does not belong to its case file (sketch ÄR1b): aside, unchanged, to a
/// folder of its own under <see cref="RecoveredFolder"/>, or copies it to a new case number. Nothing the user made is
/// ever removed; a move that stops half-way is moved back.
/// </summary>
internal static class WorkingCopyBranches
{
    /// <summary>The folder under the cases folder where working copies that were set aside are kept.</summary>
    public const string RecoveredFolder = "Återställda";

    /// <summary>The letters tried for a new case number after the file's own, <c>-B</c> first.</summary>
    private const string BranchLetters = "BCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>The time in a set-aside folder's name, in UTC, safe in a file name on Windows.</summary>
    private const string StampFormat = "yyyyMMdd-HHmmss'Z'";

    /// <summary>A working copy that was set aside: where it went, its SHA-256, and the moves to undo it.</summary>
    /// <param name="Folder">
    /// The folder relative to the cases folder, for example <c>Återställda/2026-0117 20261108-091200Z</c>.
    /// </param>
    /// <param name="FullPath">The folder's full path.</param>
    /// <param name="Sha256">The working copy's SHA-256 in lower-case hexadecimal.</param>
    /// <param name="Moves">Where each file was and where it went.</param>
    public sealed record SetAside(
        string Folder, string FullPath, string Sha256, IReadOnlyList<(string From, string To)> Moves);

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
    /// <returns>Where they went, the working copy's hash, and the moves.</returns>
    public static SetAside MoveAside(CasePaths paths, string directory, string caseNumber, DateTimeOffset utcNow)
    {
        var sha256 = HashOf(paths.WorkingCopy);
        var stamp = utcNow.UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture);
        var name = FreeName(Path.Combine(directory, RecoveredFolder), $"{caseNumber} {stamp}");
        var folder = Path.Combine(directory, RecoveredFolder, name);
        Directory.CreateDirectory(folder);
        List<(string From, string To)> moves =
            [.. WorkingFiles(paths).Select(file => (file, Path.Combine(folder, Path.GetFileName(file))))];
        try
        {
            MoveAll(moves);
        }
        catch
        {
            RemoveIfEmpty(folder);
            throw;
        }

        return new SetAside($"{RecoveredFolder}/{name}", folder, sha256, moves);
    }

    /// <summary>
    /// Undoes <see cref="MoveAside"/> when the case could not be opened, so that the working copy lies where it was
    /// and the choice is offered again; the folder goes when it is empty.
    /// </summary>
    /// <param name="setAside">What was set aside.</param>
    public static void MoveBack(SetAside setAside)
    {
        MoveAll([.. setAside.Moves.Reverse().Select(move => (move.To, move.From))]);
        RemoveIfEmpty(setAside.FullPath);
    }

    /// <summary>
    /// Copies the working copy, and its rollback journal when there is one, to a new case number's working copy,
    /// unchanged, so that the original stays untouched until the new case is made.
    /// </summary>
    /// <param name="paths">The case's files.</param>
    /// <param name="branch">The new case number's files; none of them may exist.</param>
    public static void CopyToBranch(CasePaths paths, CasePaths branch)
    {
        File.Copy(paths.WorkingCopy, branch.WorkingCopy, overwrite: false);
        if (File.Exists(paths.WorkingJournal))
        {
            File.Copy(paths.WorkingJournal, branch.WorkingJournal, overwrite: false);
        }
    }

    /// <summary>
    /// Writes a new case file from a header and the database in its working copy: aside first and then given its
    /// name, never over a file; what is written aside is removed when it fails.
    /// </summary>
    /// <param name="branch">The new case number's files.</param>
    /// <param name="header">The new case's header.</param>
    public static void WriteCaseFile(CasePaths branch, CaseHeader header)
    {
        try
        {
            using (var output = new FileStream(branch.SaveFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                CaseContainer.WriteHeader(output, header);
                using (var input = new FileStream(branch.WorkingCopy, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    input.CopyTo(output);
                }

                output.Flush(flushToDisk: true);
            }

            File.Move(branch.SaveFile, branch.CaseFile, overwrite: false);
        }
        finally
        {
            File.Delete(branch.SaveFile);
        }
    }

    /// <summary>Removes a new case number's working files once its case file is written, or all of them.</summary>
    /// <param name="branch">The new case number's files.</param>
    /// <param name="caseFileToo">Whether the case file goes too, when the new case could not be made.</param>
    public static void RemoveBranch(CasePaths branch, bool caseFileToo)
    {
        File.Delete(branch.WorkingCopy);
        File.Delete(branch.WorkingJournal);
        File.Delete(branch.WorkingBase);
        File.Delete(branch.WorkingCopyTemporary);
        File.Delete(branch.SaveFile);
        if (caseFileToo)
        {
            File.Delete(branch.CaseFile);
        }
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
        !File.Exists(paths.CaseFile) && !File.Exists(paths.SaveFile) && !File.Exists(paths.WorkingCopy)
        && !File.Exists(paths.WorkingJournal);

    /// <summary>Gives the SHA-256 of a file in lower-case hexadecimal.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The hash.</returns>
    private static string HashOf(string path)
    {
        using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        return Convert.ToHexStringLower(SHA256.HashData(input));
    }

    /// <summary>Removes a folder when nothing is left in it.</summary>
    /// <param name="folder">The folder.</param>
    private static void RemoveIfEmpty(string folder)
    {
        if (Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
        {
            Directory.Delete(folder);
        }
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
