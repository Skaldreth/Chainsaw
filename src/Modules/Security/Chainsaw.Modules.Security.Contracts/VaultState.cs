namespace Chainsaw.Modules.Security.Contracts;

/// <summary>The state of the vault, as seen by the rest of Chainsaw.</summary>
public enum VaultState
{
    /// <summary>There is no vault file yet; the first-run setup must create one with a master key.</summary>
    NotCreated,

    /// <summary>The vault file exists but the data key is not in memory; nothing secret can be read.</summary>
    Locked,

    /// <summary>The master key has been given and the data key is in memory until the vault is locked.</summary>
    Unlocked,
}
