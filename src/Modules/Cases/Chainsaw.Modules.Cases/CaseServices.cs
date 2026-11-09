using System.Security.Cryptography;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// What every open case needs from outside: keys, the operator, the clock, the options and the schema.
/// </summary>
/// <param name="Vault">The vault, for its state.</param>
/// <param name="PurposeKeys">The vault's keys per purpose, for this vault's envelopes.</param>
/// <param name="Profiles">The operator profile, for the journal.</param>
/// <param name="Secrets">
/// The vault's named secrets, for the generation of this computer's anchors; every host must register them, so that
/// the check against an older anchor file is never left out without a word.
/// </param>
/// <param name="Clock">The clock, for the journal.</param>
/// <param name="Options">The options.</param>
/// <param name="Catalog">The modules' schemas and sealed chains.</param>
internal sealed record CaseServices(
    IVault Vault,
    IPurposeKeys PurposeKeys,
    IOperatorProfileStore Profiles,
    IVaultSecrets Secrets,
    IClock Clock,
    CasesOptions Options,
    SchemaCatalog Catalog)
{
    /// <summary>Derives a key from the vault, giving a locked vault the case's own error.</summary>
    /// <param name="purpose">The purpose, such as <see cref="CaseCrypto.EnvelopePurpose"/>.</param>
    /// <returns>The key in a pinned buffer that the caller hands over or clears; cleared here on failure.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.VaultLocked"/>.</exception>
    public byte[] DeriveVaultKey(string purpose)
    {
        var key = CaseCrypto.AllocateSecret(CaseCrypto.KeySize);
        try
        {
            PurposeKeys.DeriveKey(purpose, key);
            return key;
        }
        catch (Exception exception)
        {
            CryptographicOperations.ZeroMemory(key);
            if (exception is VaultException { Error: VaultError.Locked })
            {
                throw new CaseException(CaseError.VaultLocked, exception);
            }

            throw;
        }
    }

    /// <summary>Gets the operator from the vault for the journal.</summary>
    /// <returns>The profile.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.VaultLocked"/> or <see cref="CaseError.NoOperatorProfile"/>.
    /// </exception>
    public OperatorProfile CurrentOperator()
    {
        try
        {
            return Profiles.Get() ?? throw new CaseException(CaseError.NoOperatorProfile);
        }
        catch (VaultException exception) when (exception.Error == VaultError.Locked)
        {
            throw new CaseException(CaseError.VaultLocked, exception);
        }
    }
}
