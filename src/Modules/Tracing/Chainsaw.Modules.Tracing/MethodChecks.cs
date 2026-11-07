using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>The checks every tracing method makes on what it is given, written once.</summary>
internal static class MethodChecks
{
    /// <summary>
    /// Checks that a transaction can be traced through and that each input's traced part lies within the input.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="inputs">The traced part of each input.</param>
    /// <returns>Each input's amount in satoshi, in order.</returns>
    /// <exception cref="ArgumentException">
    /// It is a coinbase, an input has no amount, the number of sets is wrong, or a set reaches past its input.
    /// </exception>
    public static long[] InputAmounts(BitcoinTransaction transaction, IReadOnlyList<TaintSet> inputs)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(inputs);
        if (transaction.IsCoinbase || inputs.Count != transaction.Inputs.Count)
        {
            throw new ArgumentException("One traced set per input of a transaction that is not a coinbase.");
        }

        var amounts = new long[inputs.Count];
        for (var i = 0; i < inputs.Count; i++)
        {
            var amount = transaction.Inputs[i].Amount
                ?? throw new ArgumentException("Every input needs its amount.", nameof(transaction));
            if (inputs[i] is null || inputs[i].End > amount.Value)
            {
                throw new ArgumentException("A traced set lies within its input.", nameof(inputs));
            }

            amounts[i] = amount.Value;
        }

        return amounts;
    }
}
