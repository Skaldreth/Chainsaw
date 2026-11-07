namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// What an analysis says about the other analyses' proposals for the same transaction, when it has seen something
/// that makes them unsafe (a CoinJoin, where the inputs have many owners).
/// </summary>
/// <param name="Kind">How far the proposals are lowered.</param>
/// <param name="Reason">Why, in plain Swedish, shown with every proposal it lowered.</param>
public sealed record OwnershipVeto(VetoKind Kind, string Reason);

/// <summary>How far an <see cref="OwnershipVeto"/> lowers the other proposals.</summary>
public enum VetoKind
{
    /// <summary>Merges become suggestions.</summary>
    Suggest,

    /// <summary>Merges and suggestions are stopped.</summary>
    Stop,
}
