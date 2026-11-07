namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>The names of the heuristics the Clustering module has; the versions belong to each heuristic.</summary>
public static class HeuristicNames
{
    /// <summary>All inputs of a transaction have the same owner.</summary>
    public const string CommonInputOwnership = "common-input-ownership";

    /// <summary>Recognises CoinJoin, where the inputs have many owners, and stops the other heuristics.</summary>
    public const string CoinJoin = "coinjoin";

    /// <summary>Points out the output that is probably the sender's change.</summary>
    public const string ChangeAddress = "change-address";

    /// <summary>Recognises chains where a little is peeled off in each hop and the change goes on.</summary>
    public const string PeelChain = "peel-chain";
}
