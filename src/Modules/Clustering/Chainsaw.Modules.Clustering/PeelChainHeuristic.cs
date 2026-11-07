using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Peel chain v1: recognises a chain of transactions where each one has one input and two outputs, a small one that
/// is peeled off (a payment) and a large one that goes on to the next hop of the same shape (the change). When the
/// case knows of at least <see cref="MinimumChainLength"/> hops in a row, the continuing output is merged with the
/// hop's input; the peeled-off outputs are never merged.
/// </summary>
internal sealed class PeelChainHeuristic : IClusteringHeuristic
{
    /// <summary>The heuristic's name and version.</summary>
    public static readonly HeuristicId Identity = new(HeuristicNames.PeelChain, 1);

    /// <summary>How many hops in a row the case must know of before it is a peel chain.</summary>
    public const int MinimumChainLength = 3;

    /// <summary>
    /// How many times larger the continuing output must be than the peeled-off one: with 4 a hop peels off at most a
    /// fifth of what it holds.
    /// </summary>
    public const long MinimumContinuingRatio = 4;

    /// <inheritdoc/>
    public HeuristicId Id => Identity;

    /// <inheritdoc/>
    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(context);
        if (Hop(transaction) is not { } hop)
        {
            return HeuristicResult.Nothing(Identity, transaction.Txid);
        }

        var before = HopsBefore(transaction, context);
        var after = HopsAfter(transaction, hop.Continuing, context);
        if (before + 1 + after < MinimumChainLength)
        {
            return HeuristicResult.Nothing(Identity, transaction.Txid);
        }

        var reason = string.Format(
            CultureInfo.InvariantCulture,
            "Hopp {0} av {1} kända i en peel chain: output {2} skalas av och output {3} går vidare till nästa hopp "
                + "av samma form, så den är växeln.",
            before + 1,
            before + 1 + after,
            hop.Peel.Index,
            hop.Continuing.Index);
        var finding = new PeelChainFinding(reason, hop.Continuing.Index, hop.Peel.Index, before, after);
        var sender = transaction.InputAddresses().FirstOrDefault();
        IReadOnlyList<OwnershipProposal> proposals = sender is null || sender == hop.Continuing.Address
            ? []
            :
            [
                new OwnershipProposal(
                    [sender, hop.Continuing.Address!], EvidenceStrength.Merge, reason, hop.Continuing.Index),
            ];
        return new HeuristicResult(Identity, transaction.Txid, proposals, [finding]);
    }

    /// <summary>
    /// Reads a transaction as a peel hop: one input, two outputs with addresses (and nothing else), the larger at
    /// least <see cref="MinimumContinuingRatio"/> times the smaller.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The continuing and peeled-off outputs, or <see langword="null"/> when it is not a hop.</returns>
    private static (BitcoinOutput Continuing, BitcoinOutput Peel)? Hop(BitcoinTransaction transaction)
    {
        var outputs = transaction.PayingOutputs();
        if (transaction.IsCoinbase || transaction.Inputs.Count != 1 || outputs.Count != 2
            || transaction.Outputs.Count != 2)
        {
            return null;
        }

        var ordered = outputs.OrderByDescending(output => output.Amount.Value).ToList();
        var (large, small) = (ordered[0], ordered[1]);
        return small.Amount.Value > 0 && large.Amount.Value / MinimumContinuingRatio >= small.Amount.Value
            ? (large, small)
            : null;
    }

    /// <summary>Counts the hops before a transaction: each one's continuing output is the next one's input.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="context">What the case knows.</param>
    /// <returns>How many hops in a row come before it.</returns>
    private static int HopsBefore(BitcoinTransaction transaction, IHeuristicContext context)
    {
        var count = 0;
        var current = transaction;
        while (count < context.Count
            && current.Inputs[0] is { PreviousTxid: { } previousTxid, PreviousIndex: { } previousIndex }
            && context.Find(previousTxid) is { } previous
            && Hop(previous) is { } hop
            && hop.Continuing.Index == previousIndex)
        {
            count++;
            current = previous;
        }

        return count;
    }

    /// <summary>Counts the hops after a transaction, following the continuing output.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="continuing">Its continuing output.</param>
    /// <param name="context">What the case knows.</param>
    /// <returns>How many hops in a row come after it.</returns>
    private static int HopsAfter(BitcoinTransaction transaction, BitcoinOutput continuing, IHeuristicContext context)
    {
        var count = 0;
        var (txid, index) = (transaction.Txid, continuing.Index);
        while (count < context.Count
            && context.FindSpender(txid, index) is { } next
            && Hop(next) is { } hop)
        {
            count++;
            (txid, index) = (next.Txid, hop.Continuing.Index);
        }

        return count;
    }
}
