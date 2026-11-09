using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>A sort of API Chainsaw can speak for a chain, such as Esplora for Bitcoin.</summary>
/// <param name="Code">The code it is saved and sealed under, for example <c>esplora</c>.</param>
/// <param name="Name">Its name in the settings, for example <c>Esplora</c>.</param>
/// <param name="Chain">The chain it answers for.</param>
public sealed record SourceApiInfo(string Code, string Name, Chain Chain)
{
    /// <summary>
    /// Gets whether only an endpoint marked local (the user's own node) may speak it, as Bitcoin Core's own RPC, whose
    /// user name and password must never leave the user's own network: an endpoint of the sort that is not marked
    /// local cannot be added or tried, and is dropped when the settings are read.
    /// </summary>
    public bool LocalOnly { get; init; }

    /// <summary>
    /// Gets whether the sort takes its key in the address (Etherscan's <c>apikey</c>) rather than in a header. The key
    /// is still never sealed in a case; the settings say it, since it shows in a company proxy's log of addresses.
    /// </summary>
    public bool KeyInAddress { get; init; }
}
