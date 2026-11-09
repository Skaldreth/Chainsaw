using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Money that reached a bridge or a swap service: the payment into it, the actor the trail ends at, the memo the
/// payment wrote when it has one, and the link an approval of <c>cross-chain</c> made from it, when one did.
/// </summary>
/// <param name="Payment">The payment into the bridge or swap.</param>
/// <param name="Service">What the end of the trail says the actor is, for example its label.</param>
/// <param name="Memo">The swap's memo in the payment's transaction, or <see langword="null"/>.</param>
/// <param name="Link">The link in force that an approved suggestion made from it, or <see langword="null"/>.</param>
internal sealed record CrossChainDeparture(
    ChainMovement Payment, string Service, SwapMemo? Memo, CrossChainLink? Link);

/// <summary>
/// Reads the payments a graph holds, on every chain, as <see cref="ChainMovement"/>s, and which of them went into a
/// bridge or a swap: an actor the trail ends at with <see cref="TrailEndReason.OtherChain"/> (a label
/// <em>Swap eller brygga</em> or a known bridge's contract). Everything here is read from the chain; that the actor is
/// a bridge is the label's judgement, which the end of the trail names.
/// </summary>
internal static class CrossChainPayments
{
    /// <summary>Gives every payment in the graph: each Bitcoin output with an address, each account movement.</summary>
    /// <param name="graph">The graph.</param>
    /// <returns>The payments: Bitcoin's by block and txid, then the account chains' in the graph's order.</returns>
    public static IReadOnlyList<ChainMovement> All(TraceGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var coins = graph.Transactions.Values
            .Select(known => known.Transaction)
            .OrderBy(transaction => transaction.Block?.Height ?? long.MaxValue)
            .ThenBy(transaction => transaction.Txid.Value, StringComparer.Ordinal)
            .SelectMany(CoinsOf);
        var movements = graph.Account.Transfers.Select(Movement);
        return [.. coins.Concat(movements)];
    }

    /// <summary>
    /// Gives the payments into a bridge or a swap that still wait for a continuation: those not linked to another
    /// chain, and those whose link an approval of the cross-chain analysis made (so that its suggestion stays
    /// decided).
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="payments">The graph's payments (<see cref="All"/>).</param>
    /// <param name="analysis">The cross-chain analysis's name and version, as an approval's link names it.</param>
    /// <returns>The departures, in the payments' order.</returns>
    public static IReadOnlyList<CrossChainDeparture> DeparturesOf(
        TraceGraph graph, IReadOnlyList<ChainMovement> payments, string analysis)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(payments);
        var departures = new List<CrossChainDeparture>();
        foreach (var payment in payments)
        {
            // A link from a coin is a Bitcoin coin's (Tracing's links name no other UTXO chain yet), so a payment in
            // on Litecoin is not suggested for until a link can say it left Litecoin.
            if ((payment.Coin is not null && payment.Transaction.Chain != Chain.Bitcoin)
                || graph.NodeOf(payment.To) is not { } node
                || node.Ends.FirstOrDefault(end => end.Reason == TrailEndReason.OtherChain) is not { } end
                || (payment.From is { } from && graph.NodeOf(from)?.Key == node.Key))
            {
                continue;
            }

            var link = graph.CrossChainLinks.FirstOrDefault(made => LeavesFrom(made, payment));
            if (link is not null && link.Origin?.Analysis != analysis)
            {
                // Linked by hand or by something else: the user has already said where the money continues.
                continue;
            }

            departures.Add(new CrossChainDeparture(payment, end.Explanation, MemoOf(graph, payment), link));
        }

        return departures;
    }

    /// <summary>Says whether a link leaves from a payment: its coin or its movement.</summary>
    /// <param name="link">The link.</param>
    /// <param name="payment">The payment.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    public static bool LeavesFrom(CrossChainLink link, ChainMovement payment)
    {
        ArgumentNullException.ThrowIfNull(link);
        ArgumentNullException.ThrowIfNull(payment);
        return (payment.Coin is { } coin && link.From == coin)
            || (payment.Movement is { } movement && link.FromTransfer == movement);
    }

    /// <summary>
    /// Gives the swap's memo in a Bitcoin payment's transaction: an <c>OP_RETURN</c> output in THORChain's form.
    /// </summary>
    /// <param name="graph">The graph.</param>
    /// <param name="payment">The payment.</param>
    /// <returns>The memo, or <see langword="null"/> when the transaction has none, or is not on Bitcoin.</returns>
    private static SwapMemo? MemoOf(TraceGraph graph, ChainMovement payment)
    {
        if (payment.Coin is not { } coin || !graph.Transactions.TryGetValue(coin.Txid, out var known))
        {
            return null;
        }

        return known.Transaction.Outputs
            .Where(output => output.ScriptType == BitcoinScriptType.OpReturn)
            .Select(output => SwapMemo.FromScript(output.Script))
            .FirstOrDefault(memo => memo is not null);
    }

    /// <summary>
    /// Gives a UTXO transaction's outputs with an address as payments, on the chain its addresses are on (Bitcoin or
    /// Litecoin), in that chain's coin.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The payments, by output.</returns>
    public static IEnumerable<ChainMovement> CoinsOf(BitcoinTransaction transaction)
    {
        var payer = transaction.Inputs.Select(input => input.Address).OfType<BitcoinAddress>().FirstOrDefault();
        var chain = transaction.Outputs.Select(output => output.Address).OfType<BitcoinAddress>()
            .Concat(payer is null ? [] : [payer])
            .Select(address => address.Chain)
            .FirstOrDefault(Chain.Bitcoin);
        var reference = new ChainTransactionRef(chain, transaction.Txid.Value);
        foreach (var output in transaction.Outputs)
        {
            if (output.Address is not { } address)
            {
                continue;
            }

            yield return new ChainMovement(
                reference,
                payer?.ToChainAddress(),
                address.ToChainAddress(),
                new AssetAmount(chain.NativeAsset(), output.Amount.Value),
                transaction.Block?.Height,
                transaction.Block?.Hash.Value,
                transaction.Block?.Time)
            {
                Coin = new Outpoint(transaction.Txid, output.Index),
            };
        }
    }

    /// <summary>Gives an account chain's movement as a payment.</summary>
    /// <param name="transfer">The movement.</param>
    /// <returns>The payment.</returns>
    public static ChainMovement Movement(EthereumTransfer transfer) =>
        new(
            new ChainTransactionRef(transfer.Transaction.Chain, transfer.Transaction.Value),
            transfer.From.ToChainAddress(),
            transfer.To.ToChainAddress(),
            transfer.Amount,
            transfer.Block?.Height,
            transfer.Block?.Hash,
            transfer.Block?.Time)
        {
            Movement = AccountSeed.Of(transfer),
        };
}
