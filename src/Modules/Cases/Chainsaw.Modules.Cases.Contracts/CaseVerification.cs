namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>The result of verifying a case.</summary>
/// <param name="CheckedUtc">When the verification ran, in UTC.</param>
/// <param name="Problems">What is wrong with the case as a whole; empty when nothing is.</param>
/// <param name="Chains">
/// One result per sealed chain, ordered by chain name; empty when the database is unsound, since its chains cannot
/// then be read reliably.
/// </param>
public sealed record CaseVerification(
    DateTimeOffset CheckedUtc, IReadOnlyList<CaseProblem> Problems, IReadOnlyList<ChainVerification> Chains)
{
    /// <summary>Gets whether the case as a whole is sound and every chain is unbroken.</summary>
    public bool IsIntact => Problems.Count == 0 && Chains.All(chain => chain.Break is null);
}
