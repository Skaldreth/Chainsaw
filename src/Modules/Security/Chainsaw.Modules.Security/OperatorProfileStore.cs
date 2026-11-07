using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security;

/// <summary>The operator profile in the vault.</summary>
/// <param name="vault">The vault that holds the profile.</param>
internal sealed class OperatorProfileStore(Vault vault) : IOperatorProfileStore
{
    /// <inheritdoc />
    public OperatorProfile? Get() => vault.Read(content => content.Operator);

    /// <inheritdoc />
    public void Save(OperatorProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.Name);
        ArgumentNullException.ThrowIfNull(profile.Title);
        ArgumentNullException.ThrowIfNull(profile.Organization);
        vault.Update(content => content with { Operator = profile });
    }
}
