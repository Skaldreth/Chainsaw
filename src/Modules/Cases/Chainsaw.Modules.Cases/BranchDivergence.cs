using System.Security.Cryptography;
using Chainsaw.Modules.Cases.Contracts;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// Finds where a case file and a working copy that no longer belongs to it went apart (sketch ÄR1b): the last journal
/// entry they share and how many each has after it. Both are read from copies made for the purpose, so that neither
/// is touched (a working copy left by a crash may have a rollback journal, which SQLite would play into it).
/// </summary>
internal static class BranchDivergence
{
    /// <summary>The suffix of the copy of the file's database.</summary>
    private const string FileCopySuffix = ".diverge-file";

    /// <summary>The suffix of the copy of the working copy.</summary>
    private const string WorkingCopySuffix = ".diverge-copy";

    /// <summary>The suffix SQLite gives a rollback journal.</summary>
    private const string JournalSuffix = "-journal";

    /// <summary>Reads the journal's sequence numbers and hashes in order.</summary>
    private const string JournalSql = "SELECT sequence, hash FROM journal ORDER BY sequence";

    /// <summary>Compares the journals of the file and its working copy.</summary>
    /// <param name="paths">The case's files.</param>
    /// <param name="caseId">The case id from the header.</param>
    /// <param name="fileKey">The file key, opened with this vault.</param>
    /// <param name="open">Opens a database file with the file key.</param>
    /// <returns>Where they went apart, or <see langword="null"/> when either could not be read.</returns>
    public static CaseDivergence? Find(
        CasePaths paths, Guid caseId, ReadOnlySpan<byte> fileKey, Func<string, CaseDatabase> open)
    {
        var fileCopy = paths.CaseFile + FileCopySuffix;
        var workingCopy = paths.CaseFile + WorkingCopySuffix;
        try
        {
            if (!CaseFiles.ExtractDatabase(paths, caseId, fileKey, fileCopy))
            {
                return null;
            }

            File.Copy(paths.WorkingCopy, workingCopy, overwrite: true);
            if (File.Exists(paths.WorkingJournal))
            {
                File.Copy(paths.WorkingJournal, workingCopy + JournalSuffix, overwrite: true);
            }

            var file = ReadJournal(open(fileCopy));
            var copy = ReadJournal(open(workingCopy));
            return Compare(file, copy);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or SqliteException or CaseException)
        {
            return null;
        }
        finally
        {
            foreach (var path in new[] { fileCopy, fileCopy + JournalSuffix, workingCopy, workingCopy + JournalSuffix })
            {
                TryDelete(path);
            }
        }
    }

    /// <summary>Compares two journals entry by entry.</summary>
    /// <param name="file">The file's journal.</param>
    /// <param name="copy">The working copy's journal.</param>
    /// <returns>The last shared entry and how many each has after it.</returns>
    internal static CaseDivergence Compare(
        IReadOnlyList<(long Sequence, byte[] Hash)> file, IReadOnlyList<(long Sequence, byte[] Hash)> copy)
    {
        var shared = 0;
        while (shared < file.Count && shared < copy.Count
            && file[shared].Sequence == copy[shared].Sequence
            && CryptographicOperations.FixedTimeEquals(file[shared].Hash, copy[shared].Hash))
        {
            shared++;
        }

        return shared == 0
            ? new CaseDivergence(0, string.Empty, file.Count, copy.Count)
            : new CaseDivergence(
                file[shared - 1].Sequence,
                Convert.ToHexStringLower(file[shared - 1].Hash),
                file.Count - shared,
                copy.Count - shared);
    }

    /// <summary>Reads a journal and closes the database.</summary>
    /// <param name="database">The open database; disposed here.</param>
    /// <returns>The entries' sequence numbers and hashes, in order.</returns>
    private static List<(long Sequence, byte[] Hash)> ReadJournal(CaseDatabase database)
    {
        using (database)
        {
            using var scope = database.As(GuardMode.Internal);
            using var transaction = database.Begin(deferred: true);
            using var command = database.Command(JournalSql, transaction);
            using var reader = command.ExecuteReader();
            List<(long, byte[])> entries = [];
            while (reader.Read())
            {
                entries.Add((reader.GetInt64(0), (byte[])reader.GetValue(1)));
            }

            return entries;
        }
    }

    /// <summary>Removes a file made here, if it is there; a file that cannot be removed now is left.</summary>
    /// <param name="path">The file.</param>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Left for the next attempt, which overwrites it; it is encrypted like the case.
        }
    }
}
