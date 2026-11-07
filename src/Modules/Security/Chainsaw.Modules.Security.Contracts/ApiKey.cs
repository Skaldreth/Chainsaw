namespace Chainsaw.Modules.Security.Contracts;

/// <summary>An API key for an external source, kept encrypted in the vault and never in a case.</summary>
/// <param name="Service">The source the key is for, for example <c>esplora</c>. There is one key per service.</param>
/// <param name="Identifier">The account or key id the source knows the key by; empty when the source has none.</param>
/// <param name="Secret">The key itself.</param>
public sealed record ApiKey(string Service, string Identifier, string Secret)
{
    /// <summary>Gives the record without the secret, so that it can be logged or shown safely.</summary>
    /// <returns>The service and identifier with the secret masked.</returns>
    public override string ToString() => $"ApiKey {{ Service = {Service}, Identifier = {Identifier}, Secret = *** }}";
}
