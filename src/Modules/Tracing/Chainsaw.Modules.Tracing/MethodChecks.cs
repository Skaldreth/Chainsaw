using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>The checks every UTXO tracing method makes on what it is given, written once.</summary>
internal static class MethodChecks
{
    /// <summary>Checks that each input's traced part lies within the input.</summary>
    /// <param name="flow">The transaction's amounts.</param>
    /// <param name="inputs">The traced part of each input.</param>
    /// <exception cref="ArgumentException">The number of sets is wrong, or a set reaches past its input.</exception>
    public static void CheckInputs(UtxoFlow flow, IReadOnlyList<TaintSet> inputs)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Count != flow.Inputs.Count)
        {
            throw new ArgumentException("One traced set per input.", nameof(inputs));
        }

        for (var i = 0; i < inputs.Count; i++)
        {
            if (inputs[i] is null || inputs[i].End > flow.Inputs[i])
            {
                throw new ArgumentException("A traced set lies within its input.", nameof(inputs));
            }
        }
    }

    /// <summary>Gives a Bitcoin transaction's amounts, which the UTXO methods work on.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>Each input's and output's amount in satoshi.</returns>
    /// <exception cref="ArgumentException">It is a coinbase, or an input has no amount.</exception>
    public static UtxoFlow Flow(BitcoinTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        if (transaction.IsCoinbase)
        {
            throw new ArgumentException("A coinbase cannot be traced through.", nameof(transaction));
        }

        var inputs = transaction.Inputs
            .Select(input => input.Amount?.Value
                ?? throw new ArgumentException("Every input needs its amount.", nameof(transaction)))
            .ToList();
        return new UtxoFlow(inputs, [.. transaction.Outputs.Select(output => output.Amount.Value)]);
    }
}
