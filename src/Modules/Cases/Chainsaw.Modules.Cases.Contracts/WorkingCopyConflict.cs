namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// A working copy left by an interrupted session that does not belong to the case file as it is now: the file has
/// been saved or replaced since (sketch ÄR1b, <em>gick åt två håll</em>). Neither is touched until the user chooses
/// a branch with <see cref="ICaseStore.OpenSettingAsideWorkingCopy"/> or
/// <see cref="ICaseStore.SaveWorkingCopyAsCase"/>.
/// </summary>
/// <param name="CaseNumber">The case number of the file.</param>
/// <param name="CaseFileWrittenUtc">When the case file was last written.</param>
/// <param name="WorkingCopyWrittenUtc">When the working copy was last written.</param>
/// <param name="SuggestedBranchNumber">
/// A case number that is free in the folder for the working copy as a case of its own, for example
/// <c>2026-0117-B</c>.
/// </param>
public sealed record WorkingCopyConflict(
    string CaseNumber,
    DateTimeOffset CaseFileWrittenUtc,
    DateTimeOffset WorkingCopyWrittenUtc,
    string SuggestedBranchNumber);
