using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Weighs the analyses of one transaction against each other before anything is stored: a CoinJoin lowers every
/// other proposal, and two analyses that point out different change outputs cancel each other's merges.
/// </summary>
internal static class ResultWeighing
{
    /// <summary>The adjustment written when the analyses disagree about the change.</summary>
    public const string ChangeDisagreement = "Analyserna av tx:en pekar ut olika outputs som växel.";

    /// <summary>Weighs every result of one transaction.</summary>
    /// <param name="results">The results of every analysis of the same transaction.</param>
    /// <returns>The results with each proposal's final strength and why it was lowered, in the same order.</returns>
    public static IReadOnlyList<WeighedResult> Weigh(IReadOnlyList<HeuristicResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);
        var changeOutputs = results.SelectMany(result => result.Proposals)
            .Select(proposal => proposal.ChangeOutput)
            .OfType<int>()
            .Distinct()
            .Count();
        var disagreement = changeOutputs > 1;
        return [.. results.Select(result => new WeighedResult(
            result,
            [.. result.Proposals.Select(proposal => Weigh(proposal, StrongestVeto(results, result), disagreement))]))];
    }

    /// <summary>
    /// Gives the strongest veto other analyses of the transaction have made; an analysis never vetoes itself.
    /// </summary>
    /// <param name="results">Every result of the transaction.</param>
    /// <param name="own">The result whose proposals are weighed.</param>
    /// <returns>The veto, or <see langword="null"/>.</returns>
    private static OwnershipVeto? StrongestVeto(IReadOnlyList<HeuristicResult> results, HeuristicResult own) =>
        results.Where(result => result.Heuristic != own.Heuristic)
            .Select(result => result.Veto)
            .OfType<OwnershipVeto>()
            .OrderByDescending(veto => veto.Kind)
            .FirstOrDefault();

    /// <summary>Weighs one proposal.</summary>
    /// <param name="proposal">The proposal.</param>
    /// <param name="veto">The strongest veto from the other analyses, or <see langword="null"/>.</param>
    /// <param name="disagreement">Whether the analyses point out different change outputs.</param>
    /// <returns>The proposal with its final strength.</returns>
    private static WeighedProposal Weigh(OwnershipProposal proposal, OwnershipVeto? veto, bool disagreement)
    {
        var strength = proposal.Strength;
        var reasons = new List<string>();
        if (disagreement && proposal.ChangeOutput is not null && strength == EvidenceStrength.Merge)
        {
            strength = EvidenceStrength.Suggestion;
            reasons.Add(ChangeDisagreement);
        }

        if (veto is { Kind: VetoKind.Stop } && strength != EvidenceStrength.Stopped)
        {
            strength = EvidenceStrength.Stopped;
            reasons.Add(veto.Reason);
        }
        else if (veto is { Kind: VetoKind.Suggest } && strength == EvidenceStrength.Merge)
        {
            strength = EvidenceStrength.Suggestion;
            reasons.Add(veto.Reason);
        }

        return new WeighedProposal(proposal, strength, reasons.Count == 0 ? null : string.Join(" ", reasons));
    }
}

/// <summary>One analysis's result of a transaction after weighing.</summary>
/// <param name="Result">The result as the analysis gave it.</param>
/// <param name="Proposals">Its proposals with their final strength, in the same order.</param>
internal sealed record WeighedResult(HeuristicResult Result, IReadOnlyList<WeighedProposal> Proposals);

/// <summary>A proposal with its final strength.</summary>
/// <param name="Proposal">The proposal as the analysis gave it.</param>
/// <param name="Strength">The final strength.</param>
/// <param name="Adjustment">Why it is lower than the analysis gave it, or <see langword="null"/>.</param>
internal sealed record WeighedProposal(OwnershipProposal Proposal, EvidenceStrength Strength, string? Adjustment);
