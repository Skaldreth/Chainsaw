using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Common-input-ownership v1: whoever spends several outputs in one transaction must hold the keys to all of them,
/// so the inputs' addresses have the same owner. It does not hold in a CoinJoin, which the CoinJoin analysis stops on
/// Bitcoin; on Litecoin and Zcash, where that analysis does not run, a transaction with equal outputs is only proposed.
/// </summary>
internal sealed class CommonInputOwnershipHeuristic : IUtxoHeuristic
{
    /// <summary>The heuristic's name and version.</summary>
    public static readonly HeuristicId Identity = new(HeuristicNames.CommonInputOwnership, 1);

    /// <inheritdoc/>
    public HeuristicId Id => Identity;

    /// <inheritdoc/>
    /// <remarks>
    /// Bitcoin, Litecoin and Zcash's transparent part: all three spend whole coins with Bitcoin's scripts, so the
    /// reasoning holds alike.
    /// </remarks>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin, Chain.Litecoin, Chain.Zcash);

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
        if (transaction.Txid.Chain != Chain.Bitcoin && HasEqualOutputs(transaction))
        {
            // The CoinJoin analysis knows Bitcoin's CoinJoins only; on another chain a transaction with equal outputs
            // may be a join of strangers, so it is never merged on its own, only proposed.
            return new HeuristicResult(
                Identity,
                transaction.Txid,
                [new OwnershipProposal(addresses, EvidenceStrength.Suggestion, reason + JoinCaveat)],
                []);
        }

        return new HeuristicResult(
            Identity, transaction.Txid, [new OwnershipProposal(addresses, EvidenceStrength.Merge, reason)], []);
    }

    /// <summary>What a proposal on another chain than Bitcoin adds when the outputs could be a CoinJoin's.</summary>
    internal const string JoinCaveat =
        " Tx:en har flera utgångar med samma belopp, som i en CoinJoin, och CoinJoin-analysen känner bara Bitcoins "
        + "CoinJoins; därför slås adresserna inte ihop av sig själva utan föreslås.";

    /// <summary>
    /// Says whether two or more outputs to different addresses have the same amount, the mark of a join where whose
    /// output is whose cannot be read.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns><see langword="true"/> when some amount is paid to two different addresses.</returns>
    private static bool HasEqualOutputs(BitcoinTransaction transaction) =>
        transaction.Outputs.Where(output => output.Address is not null && output.Amount.Value > 0)
            .GroupBy(output => output.Amount.Value)
            .Any(group => group.Select(output => output.Address).Distinct().Count() > 1);
}
