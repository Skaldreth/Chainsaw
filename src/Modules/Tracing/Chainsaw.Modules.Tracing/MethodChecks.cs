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

    /// <summary>
    /// Gives a UTXO transaction's amounts, which the UTXO methods work on. A Zcash transaction that touches a shielded
    /// pool gets the pool as one more input (what comes out of it) and one more output (what goes into it), after the
    /// transparent ones, so that the amounts add up as on the chain; the pool is never a coin in the graph.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>Each input's and output's amount in the chain's smallest unit, the pool's last.</returns>
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
        var outputs = transaction.Outputs.Select(output => output.Amount.Value).ToList();
        if (HasPoolInput(transaction))
        {
            inputs.Add(transaction.Shielded!.OutOfPool.Value);
        }

        if (HasPoolOutput(transaction))
        {
            outputs.Add(transaction.Shielded!.IntoPool.Value);
        }

        return new UtxoFlow(inputs, outputs);
    }

    /// <summary>Says whether money comes out of a shielded pool in the transaction (the flow's last input).</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool HasPoolInput(BitcoinTransaction transaction) => transaction.Shielded?.OutOfPool.Value > 0;

    /// <summary>Says whether money goes into a shielded pool in the transaction (the flow's last output).</summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool HasPoolOutput(BitcoinTransaction transaction) => transaction.Shielded?.IntoPool.Value > 0;
}
