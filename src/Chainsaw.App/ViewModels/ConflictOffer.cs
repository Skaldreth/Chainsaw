using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// A working copy in conflict offered for the choice of branch (sketch ÄR1b), with why the two went apart, so that the
/// dialog says what is true.
/// </summary>
/// <param name="Conflict">The conflict.</param>
/// <param name="FileReplaced">
/// <see langword="true"/> when the case file was changed or replaced on disk while the case was open; otherwise the
/// working copy was left by a crash.
/// </param>
public sealed record ConflictOffer(WorkingCopyConflict Conflict, bool FileReplaced);
