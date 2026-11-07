namespace Chainsaw.Modules.Security.Contracts;

/// <summary>
/// The operator profile in the vault. Every member needs an unlocked vault and throws a <see cref="VaultException"/>
/// with <see cref="VaultError.Locked"/> otherwise.
/// </summary>
public interface IOperatorProfileStore
{
    /// <summary>Gets the stored profile.</summary>
    /// <returns>The profile, or <see langword="null"/> when none has been saved yet.</returns>
    OperatorProfile? Get();

    /// <summary>Stores the profile, replacing the one stored before, and writes the vault file.</summary>
    /// <param name="profile">The profile; the name must not be empty.</param>
    void Save(OperatorProfile profile);
}
