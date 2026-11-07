using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing.Contracts;

/// <summary>
/// A tracing method as a step with a name and a version: given which satoshi of each input are traced, it says how
/// many of them end up in each output and in the fee. Another method (LIFO, poison, or a local model later,
/// principle 5) is added by registering one more.
/// </summary>
/// <remarks>
/// A method never creates or loses money: what is traced in the inputs is exactly what is traced in the outputs plus
/// the fee. Chainsaw checks that after every transaction and refuses a method that breaks it.
/// </remarks>
public interface ITracingMethod
{
    /// <summary>Gets the method and its version.</summary>
    TracingMethodId Id { get; }

    /// <summary>Carries the traced satoshi through one transaction.</summary>
    /// <param name="transaction">The transaction; not a coinbase, and every input has its amount.</param>
    /// <param name="inputs">
    /// The traced part of each input, in the inputs' order; each set lies within its input's amount.
    /// </param>
    /// <returns>The traced part of each output and of the fee.</returns>
    TransactionTaint Propagate(BitcoinTransaction transaction, IReadOnlyList<TaintSet> inputs);
}

/// <summary>How the traced satoshi went through one transaction, by one method.</summary>
/// <param name="Outputs">The traced part of each output, in the outputs' order.</param>
/// <param name="Fee">How much of the traced money went to the miner as the fee.</param>
public sealed record TransactionTaint(IReadOnlyList<TaintSet> Outputs, Satoshi Fee);
