using System.Globalization;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Peel chain v2: recognises a chain of transactions where each one has two outputs, one that is peeled off (a
/// payment) and one that goes on to the next hop of the same shape (the change). When the case knows of at least
/// <see cref="MinimumChainLength"/> hops in a row, it is a peel chain, and the continuing output is proposed as the
/// hop's change. It only proposes; it never merges, and the peeled-off outputs are never part of a proposal.
/// </summary>
/// <remarks>
/// <para>
/// Version 2 follows a hop that is topped up with more inputs besides the coin from the hop before, and a chain where
/// the change is not the larger output: when the case knows that just one of a hop's outputs is spent by another
/// transaction of a hop's shape, that output is the one that goes on, whatever its size. When it knows of none (or of
/// both), the larger output goes on, and only when it is at least <see cref="MinimumContinuingRatio"/> times the
/// smaller, as in version 1.
/// </para>
/// <para>
/// Many ordinary payments have the same shape (one input, a small payment, the bulk back as change), so the shape
/// alone cannot tell where the owner changes: a victim's own earlier payments make a payment into a scam look like an
/// inner hop, and the last hop may send the bulk to an exchange. Until the tracer has tried it, a peel chain is only a
/// suggestion (TODO, *Väntar på beslut*).
/// </para>
/// </remarks>
internal sealed class PeelChainHeuristic : IUtxoHeuristic
{
    /// <summary>The heuristic's name and version.</summary>
    public static readonly HeuristicId Identity = new(HeuristicNames.PeelChain, 2);

    /// <summary>How many hops in a row the case must know of before it is a peel chain.</summary>
    public const int MinimumChainLength = 3;

    /// <summary>
    /// How many times larger the continuing output must be than the peeled-off one when the case does not know which
    /// output the next hop spends: with 4 a hop peels off at most a fifth of what it holds.
    /// </summary>
    public const long MinimumContinuingRatio = 4;

    /// <inheritdoc/>
    public HeuristicId Id => Identity;

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.Only(Chain.Bitcoin);

    /// <inheritdoc/>
    public HeuristicResult Analyse(BitcoinTransaction transaction, IHeuristicContext context)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(context);
        if (Hop(transaction, context) is not { } hop)
        {
            return HeuristicResult.Nothing(Identity, transaction.Txid);
        }

        var (before, from) = HopsBefore(transaction, context);
        var after = HopsAfter(transaction, hop.Continuing, context);
        var length = before.Count + 1 + after.Count;
        if (length < MinimumChainLength)
        {
            return HeuristicResult.Nothing(Identity, transaction.Txid);
        }

        var extra = transaction.Inputs.Count - 1;
        var reason = Reason(before.Count + 1, length, hop, extra);
        var basis = before.Concat(after).ToList();
        var finding = new PeelChainFinding(
            reason, hop.Continuing.Index, hop.Peel.Index, before.Count, after.Count,
            [.. basis.Select(txid => txid.Value)], extra);
        var sender = from ?? transaction.InputAddresses().FirstOrDefault();
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

    /// <summary>Writes the reason in plain Swedish.</summary>
    /// <param name="position">The hop's place in the chain, from 1.</param>
    /// <param name="length">How many hops the case knows of.</param>
    /// <param name="hop">The hop's outputs.</param>
    /// <param name="extra">How many inputs topped the hop up.</param>
    /// <returns>The reason.</returns>
    private static string Reason(
        int position, int length, (BitcoinOutput Continuing, BitcoinOutput Peel) hop, int extra)
    {
        var smaller = hop.Continuing.Amount.CompareTo(hop.Peel.Amount) < 0
            ? " Den som går vidare är den mindre, men det är den nästa hopp spenderar."
            : string.Empty;
        var toppedUp = extra > 0
            ? string.Format(CultureInfo.InvariantCulture, " Hoppet fylls på med {0} inputs till.", extra)
            : string.Empty;
        return string.Format(
            CultureInfo.InvariantCulture,
            "Hopp {0} av {1} kända i en peel chain: output {2} skalas av och output {3} går vidare till nästa hopp "
                + "av samma form, så den är trolig växel.{4}{5} Formen räcker inte för att slå ihop, så det är ett "
                + "förslag.",
            position,
            length,
            hop.Peel.Index,
            hop.Continuing.Index,
            smaller,
            toppedUp);
    }

    /// <summary>
    /// Reads a transaction as a peel hop (<see cref="Shape"/>). The output that goes on is the only one the case knows
    /// another transaction of that shape spends; otherwise the larger, when it is at least
    /// <see cref="MinimumContinuingRatio"/> times the smaller.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="context">What the case knows, to see which output the next hop spends.</param>
    /// <returns>The continuing and peeled-off outputs, or <see langword="null"/> when it is not a hop.</returns>
    private static (BitcoinOutput Continuing, BitcoinOutput Peel)? Hop(
        BitcoinTransaction transaction, IHeuristicContext context)
    {
        if (Shape(transaction) is not { } outputs)
        {
            return null;
        }

        var spentOn = outputs.Where(output =>
                context.FindSpender(transaction.Txid, output.Index) is { } spender && Shape(spender) is not null)
            .ToList();
        if (spentOn.Count == 1)
        {
            var continuing = spentOn[0];
            return (continuing, outputs.Single(output => output.Index != continuing.Index));
        }

        var ordered = outputs.OrderByDescending(output => output.Amount.Value).ToList();
        var (large, small) = (ordered[0], ordered[1]);
        return small.Amount.Value > 0 && large.Amount.Value / MinimumContinuingRatio >= small.Amount.Value
            ? (large, small)
            : null;
    }

    /// <summary>
    /// Reads a transaction's outputs when it has a hop's shape: at least one input and two outputs, both with
    /// addresses (and nothing else).
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The two outputs, or <see langword="null"/>.</returns>
    private static IReadOnlyList<BitcoinOutput>? Shape(BitcoinTransaction transaction)
    {
        var outputs = transaction.PayingOutputs();
        var isHop = !transaction.IsCoinbase && transaction.Inputs.Count > 0 && outputs.Count == 2
            && transaction.Outputs.Count == 2;
        return isHop ? outputs : null;
    }

    /// <summary>
    /// Finds the hops before a transaction: one of each hop's inputs spends the hop before's continuing output.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="context">What the case knows.</param>
    /// <returns>
    /// The hops in a row before it, nearest first, and the address of the transaction's coin from the nearest, or
    /// <see langword="null"/> when there is none.
    /// </returns>
    private static (List<Txid> Hops, BitcoinAddress? From) HopsBefore(
        BitcoinTransaction transaction, IHeuristicContext context)
    {
        var hops = new List<Txid>();
        BitcoinAddress? from = null;
        var current = transaction;
        while (hops.Count < context.Count && Previous(current, context) is { } previous)
        {
            from ??= previous.Coin.Address;
            hops.Add(previous.Hop.Txid);
            current = previous.Hop;
        }

        return (hops, from);
    }

    /// <summary>Finds the hop before a transaction: by the first input that spends a hop's continuing output.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="context">What the case knows.</param>
    /// <returns>The hop before and the input that spends it, or <see langword="null"/>.</returns>
    private static (BitcoinTransaction Hop, BitcoinInput Coin)? Previous(
        BitcoinTransaction transaction, IHeuristicContext context)
    {
        foreach (var input in transaction.Inputs.OrderBy(input => input.Index))
        {
            if (input is { PreviousTxid: { } previousTxid, PreviousIndex: { } previousIndex }
                && context.Find(previousTxid) is { } previous
                && Hop(previous, context) is { } hop
                && hop.Continuing.Index == previousIndex)
            {
                return (previous, input);
            }
        }

        return null;
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
            && Hop(next, context) is { } hop)
        {
            hops.Add(next.Txid);
            (txid, index) = (next.Txid, hop.Continuing.Index);
        }

        return hops;
    }
}
