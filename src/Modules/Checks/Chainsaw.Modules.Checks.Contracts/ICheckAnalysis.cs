using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>
/// A check or a suggestion as an analysis with a name and a version, like the heuristics (principle 5): it runs in the
/// background on what the case holds and gives what it found, which is a judgement and never a fact (principle 2).
/// A new analysis, for example a local model later, is added by registering another implementation.
/// </summary>
/// <remarks>An analysis must be deterministic: the same input gives the same findings, in the same order.</remarks>
public interface ICheckAnalysis
{
    /// <summary>Gets the analysis's stable name and version; raise the version whenever it may find differently.</summary>
    HeuristicId Id { get; }

    /// <summary>Gets the analysis's name as the user reads it, in Swedish, for example <c>Mixerutgångar</c>.</summary>
    string DisplayName { get; }

    /// <summary>Gets the chains the analysis covers; it is never run on another.</summary>
    ChainScope Scope { get; }

    /// <summary>Runs the analysis over what the case holds.</summary>
    /// <param name="input">The transactions, the known mixers and the clustering's findings.</param>
    /// <returns>What it found; empty when nothing.</returns>
    IReadOnlyList<CheckFinding> Run(CheckInput input);
}
