namespace Chainsaw.Modules.Security.Contracts;

/// <summary>What is known about a stored API key without the key itself, for lists in the settings.</summary>
/// <param name="Service">The source the key is for.</param>
/// <param name="Identifier">The account or key id the source knows the key by; empty when the source has none.</param>
public sealed record ApiKeyInfo(string Service, string Identifier);
