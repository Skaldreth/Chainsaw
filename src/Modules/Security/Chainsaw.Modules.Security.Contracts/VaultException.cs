namespace Chainsaw.Modules.Security.Contracts;

/// <summary>
/// A vault operation failed for a known reason. The message never contains a key, a secret or any vault content, so
/// the exception can be logged as it is.
/// </summary>
public sealed class VaultException : Exception
{
    /// <summary>Creates the exception for a reason, with the reason's standard message.</summary>
    /// <param name="error">Why the operation failed.</param>
    public VaultException(VaultError error)
        : base(MessageFor(error))
    {
        Error = error;
    }

    /// <summary>Creates the exception for a reason with the underlying failure as the inner exception.</summary>
    /// <param name="error">Why the operation failed.</param>
    /// <param name="innerException">The failure that caused it; it must not carry secrets either.</param>
    public VaultException(VaultError error, Exception innerException)
        : base(MessageFor(error), innerException)
    {
        Error = error;
    }

    /// <summary>Gets why the operation failed.</summary>
    public VaultError Error { get; }

    /// <summary>Gives the fixed, secret-free message for a reason.</summary>
    /// <param name="error">The reason.</param>
    /// <returns>An English message for logs; the user interface shows its own Swedish text.</returns>
    private static string MessageFor(VaultError error) => error switch
    {
        VaultError.WrongMasterKey => "The master key does not open the vault.",
        VaultError.MasterKeyTooShort =>
            $"The master key must be at least {MasterKeyRules.MinimumLength} characters long.",
        VaultError.Locked => "The vault is locked.",
        VaultError.NotCreated => "The vault has not been created.",
        VaultError.AlreadyCreated => "The vault already exists.",
        VaultError.Corrupt => "The vault file is damaged or has been changed.",
        VaultError.UnsupportedVersion => "The vault file has a format version this build does not support.",
        _ => "The vault operation failed.",
    };
}
