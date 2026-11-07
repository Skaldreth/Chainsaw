using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>A heuristic's proposal that some addresses have the same owner, with its reason.</summary>
/// <param name="Addresses">The addresses, at least two and all different.</param>
/// <param name="Strength">How much the proposal counts.</param>
/// <param name="Reason">Why, in plain Swedish, so that the user can ask <em>why?</em> (principle 3).</param>
/// <param name="ChangeOutput">
/// The output the proposal takes to be the sender's change, or <see langword="null"/> when it is not about change.
/// When two analyses of the same transaction point out different change, neither is merged.
/// </param>
public sealed record OwnershipProposal(
    IReadOnlyList<BitcoinAddress> Addresses,
    EvidenceStrength Strength,
    string Reason,
    int? ChangeOutput = null);
