using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Security;

/// <summary>What the vault holds once decrypted. Immutable, so a change is only kept after it is written.</summary>
/// <param name="ApiKeys">The API keys, one per service.</param>
/// <param name="Operator">The operator profile, or <see langword="null"/> before it is saved.</param>
internal sealed record VaultContent(IReadOnlyList<ApiKey> ApiKeys, OperatorProfile? Operator)
{
    /// <summary>Gets the content of a new vault.</summary>
    public static VaultContent Empty { get; } = new([], null);
}
