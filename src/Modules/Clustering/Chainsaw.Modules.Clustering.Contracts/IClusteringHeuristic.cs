using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// One heuristic or analysis, as a step with a name and a version: it takes a transaction and what the case knows
/// around it, and gives proposals and findings with their reasons. Register another implementation in the container
/// to add an analysis (principle 5); nothing else needs to change.
/// </summary>
/// <remarks>
/// An analysis is pure: the same transaction and context always give the same result, it keeps no state and writes
/// nothing. It runs off the UI thread.
/// </remarks>
public interface IClusteringHeuristic
{
    /// <summary>Gets the analysis's name and version.</summary>
    HeuristicId Id { get; }

    /// <summary>Analyses one transaction.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="context">What the case knows around it.</param>
    /// <returns>What the analysis concluded; <see cref="HeuristicResult.Nothing"/> when nothing.</returns>
    HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context);
}
