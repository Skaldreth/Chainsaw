namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>The result of verifying a case.</summary>
/// <param name="CheckedUtc">When the verification ran, in UTC.</param>
/// <param name="DatabaseIntact">
/// Whether every page of the encrypted database could be read and authenticated and SQLite found its structure sound
/// (<c>PRAGMA quick_check</c>). A page changed without the key fails here.
/// </param>
/// <param name="Chains">One result per sealed chain, ordered by chain name.</param>
public sealed record CaseVerification(
    DateTimeOffset CheckedUtc, bool DatabaseIntact, IReadOnlyList<ChainVerification> Chains)
{
    /// <summary>Gets whether the database is sound and every chain is unbroken.</summary>
    public bool IsIntact => DatabaseIntact && Chains.All(chain => chain.Break is null);
}
