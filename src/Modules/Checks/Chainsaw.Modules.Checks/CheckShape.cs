using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>Readings of transactions that several analyses share, so that each rule is written once.</summary>
internal static class CheckShape
{
    /// <summary>Gives the outputs that pay an address, in index order.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The outputs with an address.</returns>
    public static IReadOnlyList<BitcoinOutput> PayingOutputs(this BitcoinTransaction transaction) =>
        [.. transaction.Outputs.Where(output => output.Address is not null).OrderBy(output => output.Index)];

    /// <summary>Gives the inputs' addresses, each once, in input order.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The addresses.</returns>
    public static IReadOnlyList<BitcoinAddress> InputAddresses(this BitcoinTransaction transaction) =>
        [.. transaction.Inputs.OrderBy(input => input.Index).Select(input => input.Address)
            .OfType<BitcoinAddress>().Distinct()];

    /// <summary>Sums the inputs, when every input's amount is known.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The sum, or <see langword="null"/> when an amount is missing.</returns>
    public static Satoshi? TotalIn(this BitcoinTransaction transaction) =>
        transaction.Inputs.Any(input => input.Amount is null)
            ? null
            : transaction.Inputs.Aggregate(Satoshi.Zero, (sum, input) => sum + input.Amount!.Value);

    /// <summary>Indexes the transactions by txid.</summary>
    /// <param name="input">The input.</param>
    /// <returns>The transactions by txid.</returns>
    public static Dictionary<Txid, BitcoinTransaction> ById(this CheckInput input) =>
        input.Transactions.ToDictionary(transaction => transaction.Txid);

    /// <summary>Indexes which transaction in the input spends each coin.</summary>
    /// <param name="input">The input.</param>
    /// <returns>The spender of each coin <c>(txid, index)</c> that one of the transactions spends.</returns>
    public static Dictionary<(Txid Txid, int Index), BitcoinTransaction> Spenders(this CheckInput input)
    {
        var spenders = new Dictionary<(Txid, int), BitcoinTransaction>();
        foreach (var transaction in input.Transactions)
        {
            foreach (var spent in transaction.Inputs)
            {
                if (spent is { PreviousTxid: { } txid, PreviousIndex: { } index })
                {
                    spenders.TryAdd((txid, index), transaction);
                }
            }
        }

        return spenders;
    }

    /// <summary>Gives the transactions the clustering takes to be CoinJoins, recognised or possible.</summary>
    /// <param name="input">The input.</param>
    /// <returns>The CoinJoin finding of each such transaction.</returns>
    public static Dictionary<Txid, CoinJoinFinding> CoinJoins(this CheckInput input) =>
        LatestFindings.Of<CoinJoinFinding>(input)
            .GroupBy(found => found.Transaction.Txid)
            .ToDictionary(group => group.Key, group => group.First().Finding);

    /// <summary>Gives the name of the analysis that stored a conclusion, with its version, as the texts say it.</summary>
    /// <param name="analysis">The analysis.</param>
    /// <returns>For example <c>split-merge v1</c>.</returns>
    public static string Named(this ICheckAnalysis analysis) => analysis.Id.ToString();
}
