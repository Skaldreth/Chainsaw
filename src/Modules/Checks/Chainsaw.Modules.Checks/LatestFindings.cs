using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Reads the clustering's conclusions the way the clustering counts them: per transaction and heuristic, only the
/// highest version stored.
/// </summary>
internal static class LatestFindings
{
    /// <summary>Gives the findings of a kind, of the highest version of each heuristic, per transaction.</summary>
    /// <typeparam name="T">The kind of finding.</typeparam>
    /// <param name="input">The input.</param>
    /// <returns>The transaction, the stored finding and the finding, in the input's order of transactions.</returns>
    public static IEnumerable<(BitcoinTransaction Transaction, StoredFinding Stored, T Finding)> Of<T>(CheckInput input)
        where T : HeuristicFinding
    {
        foreach (var transaction in input.Transactions)
        {
            if (!input.Conclusions.TryGetValue(transaction.Txid, out var conclusions))
            {
                continue;
            }

            foreach (var stored in Highest(conclusions.Findings, found => found.Heuristic))
            {
                if (stored.Finding is T finding)
                {
                    yield return (transaction, stored, finding);
                }
            }
        }
    }

    /// <summary>Keeps, of items from several versions of the same heuristics, those of each one's highest version.</summary>
    /// <typeparam name="T">The kind of item.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="heuristic">The heuristic of an item.</param>
    /// <returns>The items of the highest versions, in their order.</returns>
    public static IEnumerable<T> Highest<T>(IReadOnlyList<T> items, Func<T, HeuristicId> heuristic)
    {
        var highest = items
            .GroupBy(item => heuristic(item).Name, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Max(item => heuristic(item).Version), StringComparer.Ordinal);
        return items.Where(item => heuristic(item).Version == highest[heuristic(item).Name]);
    }
}
