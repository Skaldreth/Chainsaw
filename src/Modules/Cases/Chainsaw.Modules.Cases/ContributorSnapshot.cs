using Chainsaw.Modules.Cases.Contracts;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// A copy of a module's schema contributor, taken once when the store is built and then checked, so that nothing a
/// contributor answers later can change what was checked.
/// </summary>
/// <param name="Module">The module's name.</param>
/// <param name="IsCases">
/// Whether this is the Cases module's own schema, decided by its type and never by its name.
/// </param>
/// <param name="Steps">The steps, in order.</param>
/// <param name="SealedChains">The sealed chains, with their columns copied.</param>
internal sealed record ContributorSnapshot(
    string? Module, bool IsCases, IReadOnlyList<CaseSchemaStep> Steps, IReadOnlyList<SealedChain> SealedChains);
