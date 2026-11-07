using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>A sort of API Chainsaw can speak for a chain, such as Esplora for Bitcoin.</summary>
/// <param name="Code">The code it is saved and sealed under, for example <c>esplora</c>.</param>
/// <param name="Name">Its name in the settings, for example <c>Esplora</c>.</param>
/// <param name="Chain">The chain it answers for.</param>
public sealed record SourceApiInfo(string Code, string Name, Chain Chain);
