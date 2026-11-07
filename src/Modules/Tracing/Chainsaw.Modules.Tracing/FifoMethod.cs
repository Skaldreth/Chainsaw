using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// FIFO v1: the inputs are laid end to end in their order, the outputs are cut from the same line in their order,
/// and what is left at the end is the fee. A traced satoshi keeps its place on the line, so money that came in
/// first goes out first, and money that came in together stays together.
/// </summary>
internal sealed class FifoMethod : ITracingMethod
{
    /// <inheritdoc/>
    public TracingMethodId Id { get; } = new(TracingMethodKind.Fifo, 1);

    /// <inheritdoc/>
    public TransactionTaint Propagate(BitcoinTransaction transaction, IReadOnlyList<TaintSet> inputs)
    {
        var amounts = MethodChecks.InputAmounts(transaction, inputs);
        var laid = new List<TaintRange>();
        long offset = 0;
        for (var i = 0; i < amounts.Length; i++)
        {
            laid.AddRange(inputs[i].Shift(offset).Ranges);
            offset = checked(offset + amounts[i]);
        }

        var line = TaintSet.Of(laid);
        var outputs = new List<TaintSet>(transaction.Outputs.Count);
        long position = 0;
        foreach (var output in transaction.Outputs)
        {
            var end = checked(position + output.Amount.Value);
            outputs.Add(line.Cut(position, end));
            position = end;
        }

        var fee = position <= offset ? line.Cut(position, offset).Amount : Satoshi.Zero;
        return new TransactionTaint(outputs, fee);
    }
}
