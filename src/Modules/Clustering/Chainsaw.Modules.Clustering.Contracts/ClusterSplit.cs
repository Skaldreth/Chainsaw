using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Clustering.Contracts;

/// <summary>
/// The user's decision that two addresses do not have the same owner. It is sealed in the case and holds: the
/// heuristic it names never merges the two again, and any other analysis that would merge them is shown as a
/// suggestion instead.
/// </summary>
/// <param name="Sequence">The record's number in the sealed chain <c>clustering.splits</c>.</param>
/// <param name="First">One address.</param>
/// <param name="Second">The other address.</param>
/// <param name="Heuristic">The name of the heuristic whose merge the user broke (any version).</param>
/// <param name="Reason">The user's reason.</param>
/// <param name="JournalSequence">The journal entry of the split: who decided it and when.</param>
/// <param name="RecordedUtc">When it was decided.</param>
public sealed record ClusterSplit(
    long Sequence,
    BitcoinAddress First,
    BitcoinAddress Second,
    string Heuristic,
    string Reason,
    long JournalSequence,
    DateTimeOffset RecordedUtc);

/// <summary>What the user asks for when breaking a merge.</summary>
/// <param name="First">One address.</param>
/// <param name="Second">The other address; not the same as <paramref name="First"/>.</param>
/// <param name="Heuristic">
/// The name of the heuristic whose merge is broken, usually the one on the link between them in
/// <see cref="IClusteringService.ExplainConnection"/>.
/// </param>
/// <param name="Reason">The user's reason; may be empty, at most 1 000 characters.</param>
public sealed record SplitRequest(BitcoinAddress First, BitcoinAddress Second, string Heuristic, string Reason);
