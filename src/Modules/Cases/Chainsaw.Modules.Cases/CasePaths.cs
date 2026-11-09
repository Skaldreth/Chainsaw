namespace Chainsaw.Modules.Cases;

/// <summary>The files that belong to one case file: the case itself, its working copy, its lock and its save.</summary>
/// <param name="CaseFile">The <c>.cut</c> file.</param>
internal sealed record CasePaths(string CaseFile)
{
    /// <summary>The extension of a case file.</summary>
    public const string Extension = ".cut";

    /// <summary>Gets the encrypted working copy that SQLite works on while the case is open.</summary>
    public string WorkingCopy => CaseFile + ".work";

    /// <summary>
    /// Gets the file a new working copy is written to before it gets its name, so that a half-written one is never
    /// taken for a working copy.
    /// </summary>
    public string WorkingCopyTemporary => WorkingCopy + ".tmp";

    /// <summary>Gets SQLite's rollback journal for the working copy (encrypted like the working copy).</summary>
    public string WorkingJournal => WorkingCopy + "-journal";

    /// <summary>
    /// Gets the file that records the SHA-256 of the case file the working copy was last taken from or saved to (and,
    /// during a save, of the file being written), so that a working copy is only recovered into a file it belongs to.
    /// </summary>
    public string WorkingBase => WorkingCopy + ".base";

    /// <summary>
    /// Gets the copy of the database that is encrypted under a new file key while the key is replaced; it becomes the
    /// working copy once the new case file is in place, and is removed if anything stops the replacement first.
    /// </summary>
    public string RekeyCopy => WorkingCopy + ".rekey";

    /// <summary>Gets the rollback journal SQLite may leave beside <see cref="RekeyCopy"/>.</summary>
    public string RekeyJournal => RekeyCopy + "-journal";

    /// <summary>
    /// Gets where the working copy under the old file key waits while the new case file is moved into place, so that
    /// a move that fails can go back to it; it is the same as the old case file and is removed afterwards.
    /// </summary>
    public string RekeyBackup => WorkingCopy + ".old-key";

    /// <summary>
    /// Gets whether an interrupted replacement of the file key left a copy beside the file. The new case file written
    /// aside (<see cref="SaveFile"/>) is not counted, since an interrupted save leaves one too.
    /// </summary>
    public bool HasRekeyLeftovers => File.Exists(RekeyCopy) || File.Exists(RekeyBackup);

    /// <summary>
    /// Removes what an interrupted replacement of the file key may have left, the new case file written aside
    /// included, which holds the new key under the new envelopes (a new case password among them): none of it is ever
    /// the only copy of anything, since the case file, old or new, is whole, and the working copy is beside it.
    /// </summary>
    public void DeleteRekeyLeftovers()
    {
        File.Delete(RekeyCopy);
        File.Delete(RekeyJournal);
        File.Delete(RekeyBackup);
        File.Delete(SaveFile);
    }

    /// <summary>Gets the lock file that is held open while the case is open, so that it is only opened once.</summary>
    public string LockFile => CaseFile + ".lock";

    /// <summary>Gets the file a save is written to before it replaces the case file.</summary>
    public string SaveFile => CaseFile + ".tmp";

    /// <summary>Gives the paths for a case number in a folder.</summary>
    /// <param name="directory">The cases folder.</param>
    /// <param name="caseNumber">A validated case number.</param>
    /// <returns>The paths.</returns>
    public static CasePaths For(string directory, string caseNumber) =>
        new(Path.Combine(directory, caseNumber + Extension));
}
