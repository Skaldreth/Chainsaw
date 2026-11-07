using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// FIFO v1: the inputs are laid end to end in their order, the outputs are cut from the same line in their order,
/// and what is left at the end is the fee. A traced unit keeps its place on the line, so money that came in first
/// goes out first, and money that came in together stays together. It only sees amounts, so it serves every UTXO
/// chain.
/// </summary>
internal sealed class FifoMethod : IUtxoTracingMethod
{
    /// <inheritdoc/>
    public TracingMethodId Id { get; } = new(TracingMethodKind.Fifo, 1);

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.AllOf(LedgerModel.Utxo);

    /// <inheritdoc/>
    public TransactionTaint Propagate(UtxoFlow flow, IReadOnlyList<TaintSet> inputs)
    {
        MethodChecks.CheckInputs(flow, inputs);
        var amounts = flow.Inputs;
        var laid = new List<TaintRange>();
        long offset = 0;
        for (var i = 0; i < amounts.Count; i++)
        {
            laid.AddRange(inputs[i].Shift(offset).Ranges);
            offset = checked(offset + amounts[i]);
        }

        var line = TaintSet.Of(laid);
        var outputs = new List<TaintSet>(flow.Outputs.Count);
        long position = 0;
        foreach (var output in flow.Outputs)
        {
            var end = checked(position + output);
            outputs.Add(line.Cut(position, end));
            position = end;
        }

        var fee = position <= offset ? line.Cut(position, offset).Units : 0;
        return new TransactionTaint(outputs, fee);
    }
}
