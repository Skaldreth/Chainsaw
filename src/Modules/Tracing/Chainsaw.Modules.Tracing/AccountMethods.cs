using System.Numerics;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// FIFO v1 for the account model: the account's balance is a queue of what came in, in time order. The opening
/// balance (untraced) stands first; each incoming transfer joins the end with its traced part before its untraced
/// part; each outgoing transfer and fee takes from the front. Traced money therefore leaves with the first transfers
/// after it came, which is how a wallet that is emptied as it is filled behaves.
/// </summary>
internal sealed class AccountFifoMethod : IAccountTracingMethod
{
    /// <inheritdoc/>
    public TracingMethodId Id { get; } = new(TracingMethodKind.Fifo, 1);

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.AllOf(LedgerModel.Account);

    /// <inheritdoc/>
    public AccountTaint Propagate(AccountLedger ledger)
    {
        AccountChecks.Check(ledger);
        var queue = new LinkedList<(BigInteger Units, bool Traced)>();
        Push(queue, ledger.Opening.Units, traced: false);
        var outgoing = new List<AssetAmount>();
        var fees = BigInteger.Zero;
        foreach (var entry in ledger.Entries)
        {
            var units = entry.Transfer.Amount.Units;
            switch (entry.Direction)
            {
                case TransferDirection.In:
                    Push(queue, entry.Traced.Units, traced: true);
                    Push(queue, units - entry.Traced.Units, traced: false);
                    break;
                case TransferDirection.Out:
                    outgoing.Add(new AssetAmount(ledger.Asset, Take(queue, units)));
                    break;
                default:
                    fees += Take(queue, units);
                    break;
            }
        }

        var remaining = queue.Where(lot => lot.Traced).Aggregate(BigInteger.Zero, (sum, lot) => sum + lot.Units);
        return new AccountTaint(
            outgoing, new AssetAmount(ledger.Asset, fees), new AssetAmount(ledger.Asset, remaining));
    }

    /// <summary>Adds a lot at the end of the queue, unless it is empty.</summary>
    /// <param name="queue">The queue.</param>
    /// <param name="units">The lot's units.</param>
    /// <param name="traced">Whether it is traced.</param>
    private static void Push(LinkedList<(BigInteger Units, bool Traced)> queue, BigInteger units, bool traced)
    {
        if (units > 0)
        {
            queue.AddLast((units, traced));
        }
    }

    /// <summary>
    /// Takes units from the front of the queue and counts the traced ones. When the queue runs dry (the history read
    /// does not hold everything that came in), the rest is untraced: a method never creates traced money.
    /// </summary>
    /// <param name="queue">The queue.</param>
    /// <param name="units">How many units leave.</param>
    /// <returns>How many of them are traced.</returns>
    private static BigInteger Take(LinkedList<(BigInteger Units, bool Traced)> queue, BigInteger units)
    {
        var traced = BigInteger.Zero;
        while (units > 0 && queue.First is { } first)
        {
            var taken = BigInteger.Min(units, first.Value.Units);
            traced += first.Value.Traced ? taken : BigInteger.Zero;
            units -= taken;
            if (taken == first.Value.Units)
            {
                queue.RemoveFirst();
            }
            else
            {
                first.Value = (first.Value.Units - taken, first.Value.Traced);
            }
        }

        return traced;
    }
}

/// <summary>
/// Haircut v1 for the account model: every outgoing transfer and fee takes the traced share of the balance at that
/// moment, in proportion to its amount (rounded down to whole units; what rounding leaves stays in the account). The
/// traced money is thinned out over everything that leaves after it came.
/// </summary>
internal sealed class AccountHaircutMethod : IAccountTracingMethod
{
    /// <inheritdoc/>
    public TracingMethodId Id { get; } = new(TracingMethodKind.Haircut, 1);

    /// <inheritdoc/>
    public ChainScope Scope { get; } = ChainScope.AllOf(LedgerModel.Account);

    /// <inheritdoc/>
    public AccountTaint Propagate(AccountLedger ledger)
    {
        AccountChecks.Check(ledger);
        var balance = ledger.Opening.Units;
        var traced = BigInteger.Zero;
        var fees = BigInteger.Zero;
        var outgoing = new List<AssetAmount>();
        foreach (var entry in ledger.Entries)
        {
            var units = entry.Transfer.Amount.Units;
            if (entry.Direction == TransferDirection.In)
            {
                balance += units;
                traced += entry.Traced.Units;
                continue;
            }

            // More can leave than the history read shows came in; only what is there can carry traced money.
            var leaving = BigInteger.Min(units, balance);
            var share = balance.IsZero ? BigInteger.Zero : traced * leaving / balance;
            balance -= leaving;
            traced -= share;
            if (entry.Direction == TransferDirection.Out)
            {
                outgoing.Add(new AssetAmount(ledger.Asset, share));
            }
            else
            {
                fees += share;
            }
        }

        return new AccountTaint(outgoing, new AssetAmount(ledger.Asset, fees), new AssetAmount(ledger.Asset, traced));
    }
}

/// <summary>
/// The rules an account's ledger keeps and a method's answer must keep, written once: every entry is in the ledger's
/// asset, an incoming transfer's traced part is at most its amount, nothing else is traced on the way in, only the
/// chain's own coin pays fees, and a method neither creates nor loses traced money.
/// </summary>
internal static class AccountChecks
{
    /// <summary>Checks a ledger.</summary>
    /// <param name="ledger">The ledger.</param>
    /// <exception cref="ArgumentException">It breaks a rule.</exception>
    public static void Check(AccountLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        if (ledger.Opening.Asset != ledger.Asset || ledger.Opening.Units < 0)
        {
            throw new ArgumentException(
                "The opening balance is in the ledger's asset and not negative.", nameof(ledger));
        }

        foreach (var entry in ledger.Entries)
        {
            var amount = entry.Transfer.Amount;
            if (amount.Asset != ledger.Asset || entry.Traced.Asset != ledger.Asset || amount.Units < 0)
            {
                throw new ArgumentException("Every entry is in the ledger's asset.", nameof(ledger));
            }

            if (entry.Traced.Units < 0 || entry.Traced.Units > amount.Units
                || (entry.Direction != TransferDirection.In && !entry.Traced.IsZero))
            {
                throw new ArgumentException(
                    "Only an incoming transfer is traced, and at most its amount.", nameof(ledger));
            }

            if (entry.Direction == TransferDirection.Fee && ledger.Asset.Kind != AssetKind.Native)
            {
                throw new ArgumentException("Only the chain's own coin pays fees.", nameof(ledger));
            }
        }
    }

    /// <summary>
    /// Runs a method on a ledger and checks its answer: one share per outgoing entry, each within its transfer, and
    /// what came in traced is exactly what left, went to fees and remains.
    /// </summary>
    /// <param name="method">The method.</param>
    /// <param name="ledger">The ledger.</param>
    /// <returns>The method's answer.</returns>
    /// <exception cref="InvalidOperationException">The method created or lost money, or broke its contract.</exception>
    public static AccountTaint Run(IAccountTracingMethod method, AccountLedger ledger)
    {
        ArgumentNullException.ThrowIfNull(method);
        Check(ledger);
        var taint = method.Propagate(ledger);
        var outgoing = ledger.Entries.Where(entry => entry.Direction == TransferDirection.Out).ToList();
        if (taint.Outgoing.Count != outgoing.Count
            || taint.Outgoing.Zip(outgoing).Any(pair => pair.First.Asset != ledger.Asset || pair.First.Units < 0
                || pair.First.Units > pair.Second.Transfer.Amount.Units))
        {
            throw new InvalidOperationException($"{method.Id} gave a share that does not fit its transfer.");
        }

        var tracedIn = ledger.Entries.Aggregate(BigInteger.Zero, (sum, entry) => sum + entry.Traced.Units);
        var tracedOut = taint.Outgoing.Aggregate(BigInteger.Zero, (sum, share) => sum + share.Units)
            + taint.Fees.Units + taint.Remaining.Units;
        if (tracedIn != tracedOut || taint.Fees.Units < 0 || taint.Remaining.Units < 0)
        {
            throw new InvalidOperationException($"{method.Id} created or lost traced money; it is refused.");
        }

        return taint;
    }
}
