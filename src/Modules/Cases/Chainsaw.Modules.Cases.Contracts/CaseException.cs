namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// A case operation failed for a known reason. The message never contains a key, a password or case content, so the
/// exception can be logged as it is.
/// </summary>
public sealed class CaseException : Exception
{
    /// <summary>Creates the exception for a reason, with the reason's standard message.</summary>
    /// <param name="error">Why the operation failed.</param>
    public CaseException(CaseError error)
        : base(MessageFor(error))
    {
        Error = error;
    }

    /// <summary>Creates the exception for a reason with the underlying failure as the inner exception.</summary>
    /// <param name="error">Why the operation failed.</param>
    /// <param name="innerException">The failure that caused it; it must not carry secrets either.</param>
    public CaseException(CaseError error, Exception innerException)
        : base(MessageFor(error), innerException)
    {
        Error = error;
    }

    /// <summary>Gets why the operation failed.</summary>
    public CaseError Error { get; }

    /// <summary>Gives the fixed, secret-free message for a reason.</summary>
    /// <param name="error">The reason.</param>
    /// <returns>An English message for logs; the user interface shows its own Swedish text.</returns>
    private static string MessageFor(CaseError error) => error switch
    {
        CaseError.InvalidCaseNumber => "The case number cannot be used as a file name.",
        CaseError.NotFound => "The case file does not exist.",
        CaseError.AlreadyExists => "A case file with that number already exists.",
        CaseError.NoAccess => "No key envelope in the case file opens with what was given.",
        CaseError.Corrupt => "The case file is damaged or has been changed.",
        CaseError.UnsupportedVersion => "The case file has a format or schema version this build does not support.",
        CaseError.InUse => "The case is already open.",
        CaseError.Conflict => "A working copy from an interrupted session does not belong to this case file.",
        CaseError.VaultLocked => "The vault is locked.",
        CaseError.NoOperatorProfile => "No operator profile is saved.",
        CaseError.PasswordTooShort =>
            $"The sharing password must be at least {SharingPasswordRules.MinimumLength} characters long.",
        CaseError.WouldLoseAccess => "The change would leave no key envelope that this vault can open.",
        CaseError.Closed => "The case is closed.",
        _ => "The case operation failed.",
    };
}
