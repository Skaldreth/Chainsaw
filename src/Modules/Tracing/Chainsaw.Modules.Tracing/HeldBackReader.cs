using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Reads a transaction from an address's history as a held-back transaction (sketch EX1): which side of the address it
/// lies on, what moved and every counterparty, the one that took or gave the most first. Without I/O, so that it is
/// tested against made-up transactions.
/// </summary>
/// <remarks>
/// What moved is a fact only when the address is the only one paying in. When others pay in too (a CoinJoin, a batch),
/// the outputs to other addresses are everyone's, so counting them all would show other people's coins as the
/// address's; the amount is then the address's share, what it put in less what came back to it, marked as a
/// judgement (principle 2).
/// </remarks>
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

        var held = pays ? OutOf(transaction, address) : InOf(transaction, address, Received(transaction, address));
        return held with { Address = address, Fee = transaction.Fee };
    }

    /// <summary>
    /// Reads a transaction the address pays in: what went to other addresses (its share when others paid in too), or,
    /// when nothing left it, a transfer to itself.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="address">The address.</param>
    /// <returns>The held-back transaction.</returns>
    private static HeldBackTransaction OutOf(BitcoinTransaction transaction, BitcoinAddress address)
    {
        var back = Received(transaction, address);
        var away = Sum(transaction.Outputs.Where(output => output.Address != address).Select(output => output.Amount));
        var shared = transaction.Inputs.Any(input => input.Address != address);
        if (shared)
        {
            return SharedOf(transaction, address, back, away);
        }

        if (away == Satoshi.Zero)
        {
            return new HeldBackTransaction(transaction.Txid, transaction.Block, PileSide.Out, back, null, 0)
            {
                IsSelf = true,
            };
        }

        return Paid(transaction, address, away);
    }

    /// <summary>
    /// Reads a transaction the address pays into together with others: its share is what it put in less what came
    /// back to it, never more than what went to other addresses; when it got back more than it put in, the
    /// transaction paid it, on the in side. Without its inputs' amounts, the share is what went away in proportion to
    /// how many of the inputs are its.
    /// </summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="address">The address.</param>
    /// <param name="back">What came back to the address.</param>
    /// <param name="away">What went to other addresses or scripts.</param>
    /// <returns>The held-back transaction, marked as shared.</returns>
    private static HeldBackTransaction SharedOf(
        BitcoinTransaction transaction, BitcoinAddress address, Satoshi back, Satoshi away)
    {
        var own = transaction.Inputs.Where(input => input.Address == address).ToList();
        if (own.Any(input => input.Amount is null))
        {
            var share = away.Value * own.Count / transaction.Inputs.Count;
            return Paid(transaction, address, new Satoshi(share)) with { IsShared = true };
        }

        var put = Sum(own.Select(input => input.Amount!.Value));
        if (back.Value > put.Value)
        {
            return InOf(transaction, address, new Satoshi(back.Value - put.Value)) with { IsShared = true };
        }

        var net = Math.Min(put.Value - back.Value, away.Value);
        return Paid(transaction, address, new Satoshi(net)) with { IsShared = true };
    }

    /// <summary>Makes a payment out: the amount and every address that took part of it, the most first.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="address">The address.</param>
    /// <param name="amount">What the address paid away.</param>
    /// <returns>The held-back transaction.</returns>
    private static HeldBackTransaction Paid(BitcoinTransaction transaction, BitcoinAddress address, Satoshi amount)
    {
        var takers = Ranked(transaction.Outputs.Where(output => output.Address is not null && output.Address != address)
            .Select(output => (output.Address!, output.Amount)));
        return Made(transaction, PileSide.Out, amount, takers);
    }

    /// <summary>Reads a transaction that pays the address: what it was paid, and every address that paid in.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="address">The address.</param>
    /// <param name="amount">What the address was paid.</param>
    /// <returns>The held-back transaction.</returns>
    private static HeldBackTransaction InOf(BitcoinTransaction transaction, BitcoinAddress address, Satoshi amount)
    {
        var givers = Ranked(transaction.Inputs.Where(input => input.Address is not null && input.Address != address)
            .Select(input => (input.Address!, input.Amount ?? Satoshi.Zero)));
        return Made(transaction, PileSide.In, amount, givers);
    }

    /// <summary>Makes the held-back transaction with its counterparties.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="side">The side.</param>
    /// <param name="amount">What moved.</param>
    /// <param name="parties">The counterparties, the most first.</param>
    /// <returns>The held-back transaction.</returns>
    private static HeldBackTransaction Made(
        BitcoinTransaction transaction, PileSide side, Satoshi amount, IReadOnlyList<BitcoinAddress> parties) =>
        new(transaction.Txid, transaction.Block, side, amount, parties.FirstOrDefault(), parties.Count)
        {
            Parties = parties,
        };

    /// <summary>Ranks addresses by what each took or gave in all, the most first, then by the address.</summary>
    /// <param name="parts">Each part with its address.</param>
    /// <returns>The addresses, each once.</returns>
    private static List<BitcoinAddress> Ranked(IEnumerable<(BitcoinAddress Address, Satoshi Amount)> parts) =>
    [
        .. parts.GroupBy(part => part.Address)
            .Select(group => (Address: group.Key, Amount: Sum(group.Select(part => part.Amount))))
            .OrderByDescending(party => party.Amount.Value)
            .ThenBy(party => party.Address.Value, StringComparer.Ordinal)
            .Select(party => party.Address),
    ];

    /// <summary>Gives what a transaction paid an address.</summary>
    /// <param name="transaction">The transaction.</param>
    /// <param name="address">The address.</param>
    /// <returns>The sum of its outputs to the address.</returns>
    private static Satoshi Received(BitcoinTransaction transaction, BitcoinAddress address) =>
        Sum(transaction.Outputs.Where(output => output.Address == address).Select(output => output.Amount));

    /// <summary>Adds amounts.</summary>
    /// <param name="amounts">The amounts.</param>
    /// <returns>The sum.</returns>
    private static Satoshi Sum(IEnumerable<Satoshi> amounts) =>
        amounts.Aggregate(Satoshi.Zero, (sum, amount) => sum + amount);
}
