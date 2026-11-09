namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// How the case's own saves in the background go (<see cref="ICase.SaveStateChanged"/>): how many have failed one
/// after another since the last that succeeded. The working copy still holds every write when a save fails, so
/// nothing is lost, but the case file is not fresh until a save succeeds.
/// </summary>
/// <param name="FailuresInARow">The saves that failed in a row; zero once a save succeeds again.</param>
/// <param name="FileReplaced">
/// Whether a save found the case file changed or replaced outside Chainsaw (Explorer, a sync tool, a colleague) since
/// the case last read or wrote it. The file is then left as it is and the case is not saved again: once it is closed,
/// its working copy is a working copy in conflict (<see cref="ICaseStore.FindConflict"/>), to be saved as a case of
/// its own (sketch ÄR1b). Said once.
/// </param>
public sealed record CaseSaveState(int FailuresInARow, bool FileReplaced = false)
{
    /// <summary>Gets whether the case file holds everything again after saves that failed.</summary>
    public bool IsSaved => FailuresInARow == 0 && !FileReplaced;
}
