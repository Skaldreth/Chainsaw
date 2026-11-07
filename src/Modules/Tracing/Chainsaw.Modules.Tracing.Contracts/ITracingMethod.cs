using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// A tracing method as a step with a name and a version, registered for one model of chain and the chains it covers
/// (<see cref="Scope"/>). What a method does depends on the model, so each model has its own contract:
/// <see cref="IUtxoTracingMethod"/> for coins and <see cref="IAccountTracingMethod"/> for balances. Another method
/// (LIFO, poison, or a local model later, principle 5) is added by registering one more, and a method for one model
/// never changes what the other gives.
/// </summary>
/// <remarks>
/// A method never creates or loses money: what is traced in is exactly what is traced out plus what stays or goes to
/// the fee. Chainsaw checks that after every step and refuses a method that breaks it.
/// </remarks>
public interface ITracingMethod
{
    /// <summary>Gets the method and its version.</summary>
    TracingMethodId Id { get; }

    /// <summary>Gets the model and the chains the method is registered for.</summary>
    ChainScope Scope { get; }
}

/// <summary>
/// A tracing method for the UTXO model: given which units of each input are traced, it says how many of them end up
/// in each output and in the fee. It only sees the amounts, so the same method serves every UTXO chain.
/// </summary>
public interface IUtxoTracingMethod : ITracingMethod
{
    /// <summary>Carries the traced units through one transaction.</summary>
    /// <param name="flow">The transaction's amounts; not a coinbase.</param>
    /// <param name="inputs">
    /// The traced part of each input, in the inputs' order; each set lies within its input's amount.
    /// </param>
    /// <returns>The traced part of each output and of the fee.</returns>
    TransactionTaint Propagate(UtxoFlow flow, IReadOnlyList<TaintSet> inputs);
}

/// <summary>
/// The amounts of one UTXO transaction, in the chain's smallest unit: what each input spends and each output locks.
/// What the inputs leave over is the fee. A UTXO chain's coins fit in 64 bits (Bitcoin's whole supply is about
/// 2.1 × 10^15 satoshi), which the coins' positions (<see cref="TaintSet"/>) rely on.
/// </summary>
public sealed record UtxoFlow
{
    /// <summary>Creates a flow after checking it.</summary>
    /// <param name="inputs">Each input's amount, in order.</param>
    /// <param name="outputs">Each output's amount, in order.</param>
    /// <exception cref="ArgumentException">
    /// There is no input, an amount is negative, or the outputs are more than the inputs.
    /// </exception>
    public UtxoFlow(IReadOnlyList<long> inputs, IReadOnlyList<long> outputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(outputs);
        if (inputs.Count == 0 || inputs.Concat(outputs).Any(amount => amount < 0))
        {
            throw new ArgumentException("A flow has at least one input, and no amount is negative.", nameof(inputs));
        }

        TotalIn = inputs.Aggregate(0L, (sum, amount) => checked(sum + amount));
        TotalOut = outputs.Aggregate(0L, (sum, amount) => checked(sum + amount));
        if (TotalOut > TotalIn)
        {
            throw new ArgumentException("The outputs cannot be more than the inputs.", nameof(outputs));
        }

        (Inputs, Outputs) = ([.. inputs], [.. outputs]);
    }

    /// <summary>Gets each input's amount, in order.</summary>
    public IReadOnlyList<long> Inputs { get; }

    /// <summary>Gets each output's amount, in order.</summary>
    public IReadOnlyList<long> Outputs { get; }

    /// <summary>Gets the sum of the inputs.</summary>
    public long TotalIn { get; }

    /// <summary>Gets the sum of the outputs.</summary>
    public long TotalOut { get; }

    /// <summary>Gets the fee: what the inputs leave over.</summary>
    public long Fee => TotalIn - TotalOut;
}

/// <summary>How the traced units went through one UTXO transaction, by one method.</summary>
/// <param name="Outputs">The traced part of each output, in the outputs' order.</param>
/// <param name="Fee">How many of the traced units went to the miner as the fee.</param>
public sealed record TransactionTaint(IReadOnlyList<TaintSet> Outputs, long Fee);
