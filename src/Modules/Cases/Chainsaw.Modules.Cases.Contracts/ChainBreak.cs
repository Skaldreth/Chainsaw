namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>Where a sealed chain breaks first.</summary>
/// <param name="Sequence">The sequence number of the first record that does not hold (0 for the chain as a whole).</param>
/// <param name="Reason">Why it does not hold.</param>
public sealed record ChainBreak(long Sequence, ChainBreakReason Reason);
