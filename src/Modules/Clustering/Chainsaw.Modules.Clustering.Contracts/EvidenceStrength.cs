namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>How much a proposal that addresses have the same owner counts.</summary>
public enum EvidenceStrength
{
    /// <summary>The addresses are merged into one cluster automatically (the user's decision 2026-11-06).</summary>
    Merge,

    /// <summary>The addresses are only proposed; nothing is merged, but the proposal is shown.</summary>
    Suggestion,

    /// <summary>
    /// Another analysis of the same transaction stopped the proposal, for example because it is a CoinJoin. Nothing
    /// is merged; the proposal is kept so that Chainsaw can say why not.
    /// </summary>
    Stopped,
}
