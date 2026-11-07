namespace Chainsaw.Modules.Cases;

/// <summary>The files that belong to one case file: the case itself, its working copy, its lock and its save.</summary>
/// <param name="CaseFile">The <c>.cut</c> file.</param>
internal sealed record CasePaths(string CaseFile)
{
    /// <summary>The extension of a case file.</summary>
    public const string Extension = ".cut";

    /// <summary>Gets the encrypted working copy that SQLite works on while the case is open.</summary>
    public string WorkingCopy => CaseFile + ".work";

    /// <summary>Gets SQLite's rollback journal for the working copy (encrypted like the working copy).</summary>
    public string WorkingJournal => WorkingCopy + "-journal";

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
