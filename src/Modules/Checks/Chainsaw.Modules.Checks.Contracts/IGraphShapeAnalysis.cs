using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks.Contracts;

/// <summary>
/// The open door for pattern recognition (principle 5, <em>Inget LLM nu, men en öppen dörr</em>): an analysis that
/// reads only the graph's shape (<see cref="GraphShape"/>: the actors with their sorts, the lines between them with
/// amounts and blocks, and the clustering's conclusions about each line) and gives checks or suggestions with its
/// name and version, like every other analysis. It is registered as an <see cref="ICheckAnalysis"/>, so the rules of
/// <c>pattern</c> today and a local model later are added the same way, without anything else changing.
/// </summary>
/// <remarks>
/// An implementation must be deterministic (the same shape gives the same findings, in the same order), must run on
/// this computer only and must never reach out or send anything of the case anywhere (principle 4). What it finds is
/// a judgement, never a fact (principle 2), and its <see cref="CheckFinding.Why"/> says exactly which actors and lines
/// it rests on (principle 3). An analysis that would drive the investigation by itself is not let in.
/// </remarks>
public interface IGraphShapeAnalysis : ICheckAnalysis
{
    /// <summary>Gets the chains the analysis covers; the shape is read from Bitcoin today.</summary>
    ChainScope ICheckAnalysis.Scope => ChainScope.Only(Chain.Bitcoin);

    /// <summary>Runs the analysis over the graph's shape.</summary>
    /// <param name="shape">The shape, read from what the case holds.</param>
    /// <returns>What it found; empty when nothing.</returns>
    IReadOnlyList<CheckFinding> Run(GraphShape shape);

    /// <summary>Reads the graph's shape from the input and runs the analysis over it.</summary>
    /// <param name="input">What the case holds.</param>
    /// <returns>What it found.</returns>
    IReadOnlyList<CheckFinding> ICheckAnalysis.Run(CheckInput input) => Run(GraphShape.Of(input));
}
