namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>A record that was added to a sealed table.</summary>
/// <param name="Chain">The name of the sealed chain.</param>
/// <param name="Sequence">The record's place in the chain, from 1 without gaps.</param>
/// <param name="Hash">The record's SHA-256 in the chain, in lower-case hex.</param>
public sealed record SealedRecord(string Chain, long Sequence, string Hash);
