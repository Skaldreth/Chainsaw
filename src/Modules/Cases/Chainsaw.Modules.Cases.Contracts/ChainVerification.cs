namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>The result of verifying one sealed chain.</summary>
/// <param name="Chain">The chain's name.</param>
/// <param name="RecordCount">How many records were read.</param>
/// <param name="HeadSequence">The sequence number of the last record that was checked and found unbroken.</param>
/// <param name="HeadHash">
/// That record's hash in lower-case hex, or <see langword="null"/> when no record is unbroken. Writing it down outside
/// the case anchors the chain: a later verification that gives another hash for the same sequence shows a change.
/// </param>
/// <param name="Break">Where and why the chain breaks, or <see langword="null"/> when it is unbroken.</param>
public sealed record ChainVerification(
    string Chain, long RecordCount, long HeadSequence, string? HeadHash, ChainBreak? Break);
