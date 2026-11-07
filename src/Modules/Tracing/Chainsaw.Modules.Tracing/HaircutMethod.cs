using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Haircut v1: the traced share of the inputs is spread over the outputs and the fee in proportion to their amounts.
/// Shares are whole satoshi: each receiver first gets its share rounded down, and the satoshi left over go one each
/// to the receivers with the largest remainders (the earlier output on a tie, the fee last), so the sum is exact.
/// </summary>
internal sealed class HaircutMethod : ITracingMethod
{
    /// <inheritdoc/>
    public TracingMethodId Id { get; } = new(TracingMethodKind.Haircut, 1);

    /// <inheritdoc/>
    public TransactionTaint Propagate(BitcoinTransaction transaction, IReadOnlyList<TaintSet> inputs)
    {
        var amounts = MethodChecks.InputAmounts(transaction, inputs);
        long totalIn = amounts.Aggregate(0L, (sum, amount) => checked(sum + amount));
        long traced = inputs.Aggregate(0L, (sum, set) => checked(sum + set.Amount.Value));

        // The receivers are the outputs in order and then the fee, which is what the inputs leave over.
        var receivers = transaction.Outputs.Select(output => output.Amount.Value).ToList();
        receivers.Add(totalIn - receivers.Aggregate(0L, (sum, amount) => checked(sum + amount)));
        var shares = Shares(traced, totalIn, receivers);
        return new TransactionTaint(
            [.. shares.Take(transaction.Outputs.Count).Select(share => TaintSet.FirstPart(new Satoshi(share)))],
            new Satoshi(shares[^1]));
    }

    /// <summary>Splits an amount over receivers in proportion to their amounts, in whole satoshi.</summary>
    /// <param name="traced">The amount to split.</param>
    /// <param name="total">The receivers' total; the traced amount is not more.</param>
    /// <param name="receivers">The receivers' amounts.</param>
    /// <returns>Each receiver's share; they add up to <paramref name="traced"/>.</returns>
    internal static long[] Shares(long traced, long total, IReadOnlyList<long> receivers)
    {
        var shares = new long[receivers.Count];
        if (traced == 0 || total == 0)
        {
            return shares;
        }

        var remainders = new Int128[receivers.Count];
        long handedOut = 0;
        for (var i = 0; i < receivers.Count; i++)
        {
            var exact = (Int128)traced * receivers[i];
            shares[i] = (long)(exact / total);
            remainders[i] = exact % total;
            handedOut += shares[i];
        }

        // The remainders add up to (traced - handedOut) × total, and each is below total, so at least that many
        // receivers have one: a satoshi never goes to a receiver whose share is already its whole amount.
        var leftOver = traced - handedOut;
        foreach (var i in Enumerable.Range(0, receivers.Count)
            .OrderByDescending(i => remainders[i])
            .ThenBy(i => i)
            .Take((int)leftOver))
        {
            shares[i]++;
        }

        return shares;
    }
}
