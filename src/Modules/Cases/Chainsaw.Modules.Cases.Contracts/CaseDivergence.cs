namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// Where a case file and a working copy that no longer belongs to it went apart (sketch ÄR1b): the last journal entry
/// they share and how many entries each has after it.
/// </summary>
/// <param name="Sequence">The last shared entry's sequence number; 0 when they share none.</param>
/// <param name="Hash">The last shared entry's hash in lower-case hexadecimal; empty when they share none.</param>
/// <param name="FileEntries">How many journal entries the file has after it.</param>
/// <param name="CopyEntries">How many journal entries the working copy has after it.</param>
public sealed record CaseDivergence(long Sequence, string Hash, int FileEntries, int CopyEntries);
