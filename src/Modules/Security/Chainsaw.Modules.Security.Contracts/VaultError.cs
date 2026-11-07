namespace Chainsaw.Modules.Security.Contracts;

/// <summary>Why an operation on the vault failed. The user interface maps each reason to its own text.</summary>
public enum VaultError
{
    /// <summary>The master key does not open the vault.</summary>
    WrongMasterKey,

    /// <summary>The new master key is shorter than <see cref="MasterKeyRules.MinimumLength"/> characters.</summary>
    MasterKeyTooShort,

    /// <summary>The operation needs an unlocked vault.</summary>
    Locked,

    /// <summary>There is no vault file to open.</summary>
    NotCreated,

    /// <summary>A vault file already exists and will not be overwritten.</summary>
    AlreadyCreated,

    /// <summary>The vault file is damaged or has been changed outside Chainsaw.</summary>
    Corrupt,

    /// <summary>The vault file was written in a format version this build does not know.</summary>
    UnsupportedVersion,
}
