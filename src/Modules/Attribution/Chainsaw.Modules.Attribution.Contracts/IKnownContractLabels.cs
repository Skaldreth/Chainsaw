using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// Labels the addresses of an open case that are on Chainsaw's list of known contracts on Ethereum (the Chains
/// module's <c>KnownContracts</c>): Tornado Cash's pools as <see cref="LabelKind.Mixer"/> and the layer-2 chains'
/// bridges as <see cref="LabelKind.SwapBridge"/>, so that the trail ends there with the list as its reason. The label is
/// an open source's judgement with the list's name, version, the page the address was read from and the day, never a
/// fact (principle 2). An exchange's or an aggregator's router is not labelled: a swap there changes the token but not
/// the owner, and names the swap in <em>varför?</em> instead.
/// </summary>
/// <remarks>Writes to the case; call it off the UI thread.</remarks>
public interface IKnownContractLabels
{
    /// <summary>
    /// Labels the addresses that are known contracts, with one line in the journal. A contract already labelled from the
    /// list in the case, also one taken back or one that came from another case, is not labelled again, whatever the
    /// list's version (as long as the contract keeps its name and kind), so that a newer list never stands as a
    /// second judgement.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses; those on other chains than Ethereum are passed over.</param>
    /// <returns>The new labels; empty when none was added.</returns>
    IReadOnlyList<LabelEntry> Check(ICase @case, IReadOnlyCollection<ChainAddress> addresses);
}
