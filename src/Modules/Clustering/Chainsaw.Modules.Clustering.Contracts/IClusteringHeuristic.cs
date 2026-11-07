using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// One heuristic or analysis, as a step with a name and a version, registered for one model of chain and the chains
/// whose habits it knows (<see cref="Scope"/>). What it reads depends on the model, so each model has its own
/// contract: <see cref="IUtxoHeuristic"/> for coins. Register another implementation in the container to add an
/// analysis (principle 5); one registered for another model or chain never runs on, or changes, Bitcoin's.
/// </summary>
/// <remarks>
/// The account model's contract (for example deposit addresses that are emptied into the same wallet) comes with the
/// first account chain, when clusters can hold other addresses than Bitcoin's.
/// </remarks>
public interface IClusteringHeuristic
{
    /// <summary>Gets the analysis's name and version.</summary>
    HeuristicId Id { get; }

    /// <summary>Gets the model and the chains the analysis is registered for.</summary>
    ChainScope Scope { get; }
}

/// <summary>
/// A heuristic for the UTXO model: it takes a transaction and what the case knows around it, and gives proposals and
/// findings with their reasons. It reads Bitcoin's transaction and addresses, the only UTXO chain Chainsaw has; the
/// next UTXO chain (Litecoin, Zcash's transparent part) gives the contract a transaction and addresses of its own.
/// </summary>
/// <remarks>
/// An analysis is pure: the same transaction and context always give the same result, it keeps no state and writes
/// nothing. It runs off the UI thread.
/// </remarks>
public interface IUtxoHeuristic : IClusteringHeuristic
{
    /// <summary>Analyses one transaction.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="context">What the case knows around it.</param>
    /// <returns>What the analysis concluded; <see cref="HeuristicResult.Nothing"/> when nothing.</returns>
    HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context);
}
