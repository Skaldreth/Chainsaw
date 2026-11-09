using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Pattern recognition without a model (principle 5): deterministic rules over the graph's shape
/// (<see cref="GraphShape"/>) that recognise known patterns of crime and compare them with like cases in the same case.
/// It is the first analysis through the open door <see cref="IGraphShapeAnalysis"/>; a local model later is added the
/// same way. Every rule reads only what was fetched, so it says <em>among the fetched transactions</em>, and the start
/// points are never counted as part of a pattern's business, which can hide a pattern when the start point is the
/// suspect's. Four rules, each a check with an area of the sort Obfuscation, a judgement and never a fact (principle 2),
/// whose <em>Varför?</em> names exactly which actors and lines look alike and how (principle 3):
/// <list type="bullet">
/// <item><see cref="CollectionPattern"/>: collecting actors (many payers, one address, a sweep) that sweep to the same
/// actor.</item>
/// <item><see cref="RoutePattern"/>: coins from separate parts of the case passed on the same way to the same labelled
/// actor.</item>
/// <item><see cref="RoundTripPattern"/>: coins that come back to the actor they left, through others.</item>
/// <item><see cref="StructuringPattern"/>: alike amounts in a row to the same recipient, as when an amount is split to
/// stay under a limit.</item>
/// </list>
/// </summary>
internal sealed class PatternCheck : IGraphShapeAnalysis
{
    /// <summary>The analysis's stable name.</summary>
    public const string Name = "pattern";

    /// <inheritdoc/>
    public HeuristicId Id { get; } = new(Name, 1);

    /// <inheritdoc/>
    public string DisplayName => "Mönster";

    /// <inheritdoc/>
    public IReadOnlyList<CheckFinding> Run(GraphShape shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        return
        [
            .. CollectionPattern.Find(shape, this),
            .. RoutePattern.Find(shape, this),
            .. RoundTripPattern.Find(shape, this),
            .. StructuringPattern.Find(shape, this),
        ];
    }
}
