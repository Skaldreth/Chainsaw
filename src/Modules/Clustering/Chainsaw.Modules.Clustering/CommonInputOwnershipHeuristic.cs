using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Common-input-ownership v1: whoever spends several outputs in one transaction must hold the keys to all of them,
/// so the inputs' addresses have the same owner. It does not hold in a CoinJoin, which the CoinJoin analysis stops.
/// </summary>
internal sealed class CommonInputOwnershipHeuristic : IClusteringHeuristic
{
    /// <summary>The heuristic's name and version.</summary>
    public static readonly HeuristicId Identity = new(HeuristicNames.CommonInputOwnership, 1);

    /// <inheritdoc/>
    public HeuristicId Id => Identity;

    /// <inheritdoc/>
    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        var addresses = transaction.InputAddresses();
        if (transaction.IsCoinbase || addresses.Count < 2)
        {
            return HeuristicResult.Nothing(Identity, transaction.Txid);
        }

        var reason = string.Format(
            CultureInfo.InvariantCulture,
            "De {0} adresserna spenderas tillsammans som inputs i samma tx; "
                + "den som signerar måste ha nycklarna till alla.",
            addresses.Count);
        return new HeuristicResult(
            Identity, transaction.Txid, [new OwnershipProposal(addresses, EvidenceStrength.Merge, reason)], []);
    }
}
