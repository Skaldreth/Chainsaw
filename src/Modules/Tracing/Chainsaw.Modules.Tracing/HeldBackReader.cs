using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Reads a transaction from an address's history as a held-back transaction (sketch EX1): which side of the address it
/// lies on, what moved and the counterparty that took or gave the most. Only facts from the transaction; without I/O,
/// so that it is tested against made-up transactions.
/// </summary>
internal static class HeldBackReader
{
    /// <summary>Reads a transaction seen from an address.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="address">The address.</param>
    /// <returns>The held-back transaction, or <see langword="null"/> when the address takes no part in it.</returns>
    public static HeldBackTransaction? Read(BitcoinTransaction transaction, BitcoinAddress address)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(address);
        var pays = transaction.Inputs.Any(input => input.Address == address);
        var receives = transaction.Outputs.Any(output => output.Address == address);
        if (!pays && !receives)
        {
            return null;
        }

        return pays ? OutOf(transaction, address) : InOf(transaction, address);
    }

    /// <summary>
    /// Reads a transaction the address pays in: what went to other addresses and outputs, and the address that took
    /// the most.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="address">The address.</param>
    /// <returns>The held-back transaction.</returns>
    private static HeldBackTransaction OutOf(BitcoinTransaction transaction, BitcoinAddress address)
    {
        var away = transaction.Outputs.Where(output => output.Address != address).ToList();
        var amount = away.Aggregate(Satoshi.Zero, (sum, output) => sum + output.Amount);
        var takers = away.Where(output => output.Address is not null)
            .GroupBy(output => output.Address!)
            .Select(group => (Address: group.Key, Amount: group.Aggregate(Satoshi.Zero, (sum, o) => sum + o.Amount)))
            .OrderByDescending(taker => taker.Amount.Value)
            .ThenBy(taker => taker.Address.Value, StringComparer.Ordinal)
            .ToList();
        return new HeldBackTransaction(
            transaction.Txid, transaction.Block, PileSide.Out, amount, takers.FirstOrDefault().Address, takers.Count);
    }

    /// <summary>
    /// Reads a transaction the address only receives in: what it was paid, and the address that gave the most.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="address">The address.</param>
    /// <returns>The held-back transaction.</returns>
    private static HeldBackTransaction InOf(BitcoinTransaction transaction, BitcoinAddress address)
    {
        var amount = transaction.Outputs.Where(output => output.Address == address)
            .Aggregate(Satoshi.Zero, (sum, output) => sum + output.Amount);
        var givers = transaction.Inputs.Where(input => input.Address is not null)
            .GroupBy(input => input.Address!)
            .Select(group => (
                Address: group.Key,
                Amount: group.Aggregate(Satoshi.Zero, (sum, input) => sum + (input.Amount ?? Satoshi.Zero))))
            .OrderByDescending(giver => giver.Amount.Value)
            .ThenBy(giver => giver.Address.Value, StringComparer.Ordinal)
            .ToList();
        return new HeldBackTransaction(
            transaction.Txid, transaction.Block, PileSide.In, amount, givers.FirstOrDefault().Address, givers.Count);
    }
}
