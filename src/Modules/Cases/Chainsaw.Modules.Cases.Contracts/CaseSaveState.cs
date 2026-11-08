namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// How the case's own saves in the background go (<see cref="ICase.SaveStateChanged"/>): how many have failed one
/// after another since the last that succeeded. The working copy still holds every write when a save fails, so
/// nothing is lost, but the case file is not fresh until a save succeeds.
/// </summary>
/// <param name="FailuresInARow">The saves that failed in a row; zero once a save succeeds again.</param>
public sealed record CaseSaveState(int FailuresInARow)
{
    /// <summary>Gets whether the case file holds everything again after saves that failed.</summary>
    public bool IsSaved => FailuresInARow == 0;
}
