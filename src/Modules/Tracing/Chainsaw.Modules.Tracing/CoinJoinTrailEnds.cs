using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Ends the trail at a CoinJoin that Clustering has recognised as a known pattern (its stored finding), with the
/// analysis and version that recognised it. A possible CoinJoin does not end the trail; Clustering only proposes it.
/// </summary>
/// <param name="clustering">The clustering service, whose stored conclusions are read.</param>
internal sealed class CoinJoinTrailEnds(IClusteringService clustering) : ITrailEndClassifier
{
    /// <inheritdoc/>
    public IReadOnlyList<TrailEndHint> ClassifyAddress(ICase @case, BitcoinAddress address) => [];

    /// <inheritdoc/>
    public IReadOnlyList<TrailEndHint> ClassifyTransaction(ICase @case, BitcoinTransaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var conclusions = clustering.GetConclusions(@case, transaction.Txid);
        var stored = conclusions.Findings
            .FirstOrDefault(found => found.Finding is CoinJoinFinding { EndsTrail: true });
        if (stored is null)
        {
            return [];
        }

        var finding = (CoinJoinFinding)stored.Finding;
        return
        [
            new TrailEndHint(
                TrailEndReason.CoinJoin,
                stored.Heuristic.ToString(),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} känner igen tx:en som en CoinJoin ({1}): {2}",
                    stored.Heuristic,
                    finding.Pattern,
                    finding.Reason)),
        ];
    }
}
