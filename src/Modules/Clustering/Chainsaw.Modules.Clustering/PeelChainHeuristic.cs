using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Peel chain v1: recognises a chain of transactions where each one has one input and two outputs, a small one that
/// is peeled off (a payment) and a large one that goes on to the next hop of the same shape (the change). When the
/// case knows of at least <see cref="MinimumChainLength"/> hops in a row, it is a peel chain, and the continuing
/// output is proposed as the hop's change. Version 1 only proposes; it never merges, and the peeled-off outputs are
/// never part of a proposal.
/// </summary>
/// <remarks>
/// Many ordinary payments have the same shape (one input, a small payment, the bulk back as change), so the shape
/// alone cannot tell where the owner changes: a victim's own earlier payments make a payment into a scam look like an
/// inner hop, and the last hop may send the bulk to an exchange. Until the tracer has tried it, a peel chain is only a
/// suggestion (TODO, *Väntar på beslut*).
/// </remarks>
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
        var length = before.Count + 1 + after.Count;
        if (length < MinimumChainLength)
        {
            return HeuristicResult.Nothing(Identity, transaction.Txid);
        }

        var reason = string.Format(
            CultureInfo.InvariantCulture,
            "Hopp {0} av {1} kända i en peel chain: output {2} skalas av och output {3} går vidare till nästa hopp "
                + "av samma form, så den är trolig växel. Formen räcker inte för att slå ihop, så det är ett förslag.",
            before.Count + 1,
            length,
            hop.Peel.Index,
            hop.Continuing.Index);
        var basis = before.Concat(after).ToList();
        var finding = new PeelChainFinding(
            reason, hop.Continuing.Index, hop.Peel.Index, before.Count, after.Count,
            [.. basis.Select(txid => txid.Value)]);
        var sender = transaction.InputAddresses().FirstOrDefault();
        IReadOnlyList<OwnershipProposal> proposals = sender is null || sender == hop.Continuing.Address
            ? []
            :
            [
                new OwnershipProposal(
                    [sender, hop.Continuing.Address!],
                    EvidenceStrength.Suggestion,
                    reason,
                    hop.Continuing.Index),
            ];
        return new HeuristicResult(Identity, transaction.Txid, proposals, [finding]) { Basis = basis };
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

    /// <summary>Finds the hops before a transaction: each one's continuing output is the next one's input.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="context">What the case knows.</param>
    /// <returns>The hops in a row before it, nearest first.</returns>
    private static List<Txid> HopsBefore(BitcoinTransaction transaction, IHeuristicContext context)
    {
        var hops = new List<Txid>();
        var current = transaction;
        while (hops.Count < context.Count
            && current.Inputs[0] is { PreviousTxid: { } previousTxid, PreviousIndex: { } previousIndex }
            && context.Find(previousTxid) is { } previous
            && Hop(previous) is { } hop
            && hop.Continuing.Index == previousIndex)
        {
            hops.Add(previous.Txid);
            current = previous;
        }

        return hops;
    }

    /// <summary>Finds the hops after a transaction, following the continuing output.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="continuing">Its continuing output.</param>
    /// <param name="context">What the case knows.</param>
    /// <returns>The hops in a row after it, nearest first.</returns>
    private static List<Txid> HopsAfter(
        BitcoinTransaction transaction, BitcoinOutput continuing, IHeuristicContext context)
    {
        var hops = new List<Txid>();
        var (txid, index) = (transaction.Txid, continuing.Index);
        while (hops.Count < context.Count
            && context.FindSpender(txid, index) is { } next
            && Hop(next) is { } hop)
        {
            hops.Add(next.Txid);
            (txid, index) = (next.Txid, hop.Continuing.Index);
        }

        return hops;
    }
}
