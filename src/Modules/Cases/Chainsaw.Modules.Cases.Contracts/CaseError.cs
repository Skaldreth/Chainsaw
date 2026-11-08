namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>Why an operation on a case failed. The user interface maps each reason to its own text.</summary>
public enum CaseError
{
    /// <summary>The case number cannot be used as a file name (see <see cref="CaseNumberRules"/>).</summary>
    InvalidCaseNumber,

    /// <summary>There is no case file with that number or path.</summary>
    NotFound,

    /// <summary>A case file with that number already exists and will not be overwritten.</summary>
    AlreadyExists,

    /// <summary>
    /// None of the file's key envelopes opens with what was given: the case belongs to another vault, or the sharing
    /// password is wrong or was removed.
    /// </summary>
    NoAccess,

    /// <summary>The file is damaged or has been changed outside Chainsaw.</summary>
    Corrupt,

    /// <summary>The file was written in a format or schema version this build does not know.</summary>
    UnsupportedVersion,

    /// <summary>The case is already open, in this or another Chainsaw.</summary>
    InUse,

    /// <summary>
    /// A working copy from an interrupted session belongs to another file and was left untouched for a person to
    /// look at; or, when saving, the case file was changed or replaced outside Chainsaw while the case was open, so it
    /// was not replaced, and the working copy keeps the session's work beside it (see
    /// <see cref="CaseSaveState.FileReplaced"/>).
    /// </summary>
    Conflict,

    /// <summary>The vault is locked; cases are only opened, written and kept open while it is unlocked.</summary>
    VaultLocked,

    /// <summary>No operator profile is saved, so the journal could not say who did what.</summary>
    NoOperatorProfile,

    /// <summary>The sharing password is shorter than <see cref="SharingPasswordRules.MinimumLength"/>.</summary>
    PasswordTooShort,

    /// <summary>The change would leave no envelope that this vault can open, so the case would be lost here.</summary>
    WouldLoseAccess,

    /// <summary>The case has been closed (also when the vault was locked).</summary>
    Closed,

    /// <summary>
    /// The file's header already holds as many key envelopes as a header may, so no envelope can be added.
    /// </summary>
    TooManyEnvelopes,
}
