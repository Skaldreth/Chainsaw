using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Reads and writes the user's choices not to follow a swap (sketch SW1a) in the sealed chain
/// <see cref="TracingSchema.SwapStopsChain"/>: a choice and its taking back are each a record of their own, and no
/// record is ever changed (principle 1).
/// </summary>
internal static class SwapStopStore
{
    /// <summary>The action of a record that chooses that the value does not follow.</summary>
    public const string StopAction = "stop";

    /// <summary>The action of a record that takes such a choice back, so that the value follows again.</summary>
    public const string FollowAction = "follow";

    /// <summary>Seals a choice not to follow a swap.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="swap">The swap.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <param name="journal">The journal entry that records it.</param>
    /// <returns>The choice.</returns>
    public static SwapStop Seal(ICaseTransaction transaction, TokenSwap swap, string? reason, JournalEntry journal)
    {
        var record = transaction.AppendSealed(
            TracingSchema.SwapStopsChain,
            [StopAction, null, swap.Account.Chain.ToString(), swap.Transaction.Value, swap.Account.Value,
                reason ?? string.Empty, journal.Sequence, Text(journal.Utc)]);
        return new SwapStop(record.Sequence, swap.Transaction, swap.Account, reason, journal.Sequence, journal.Utc);
    }

    /// <summary>Seals that a choice is taken back, as a new record; the choice's own record is never touched.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="stop">The choice.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <param name="journal">The journal entry that records it.</param>
    public static void SealUndo(ICaseTransaction transaction, SwapStop stop, string? reason, JournalEntry journal) =>
        transaction.AppendSealed(
            TracingSchema.SwapStopsChain,
            [FollowAction, stop.Sequence, stop.Account.Chain.ToString(), stop.Transaction.Value, stop.Account.Value,
                reason ?? string.Empty, journal.Sequence, Text(journal.Utc)]);

    /// <summary>Reads the choices in force: those not taken back.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The choices, oldest first.</returns>
    public static List<SwapStop> ReadInForce(ICaseReader reader) =>
        [.. ReadHistory(reader).Where(entry => !entry.IsTakenBack).Select(entry => entry.Stop)];

    /// <summary>Reads every choice, also those taken back, each with its taking back; the first taking back counts.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The choices, oldest first.</returns>
    public static List<SwapStopHistoryEntry> ReadHistory(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT sequence, action, stop_sequence, chain, transaction_hash, account, reason, journal_sequence,
                   recorded_utc
            FROM tracing_swap_stops ORDER BY sequence
            """);
        using var rows = command.ExecuteReader();
        var stops = new List<SwapStop>();
        var undone = new Dictionary<long, SwapStopUndo>();
        while (rows.Read())
        {
            var reason = rows.GetString(6);
            var given = reason.Length == 0 ? null : reason;
            var utc = DateTimeOffset.Parse(
                rows.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
            if (rows.GetString(1) == FollowAction)
            {
                undone.TryAdd(rows.GetInt64(2), new SwapStopUndo(rows.GetInt64(0), given, rows.GetInt64(7), utc));
                continue;
            }

            stops.Add(new SwapStop(
                rows.GetInt64(0),
                EthereumTxHash.Parse(rows.GetString(4)),
                new ChainAddress(Enum.Parse<Chain>(rows.GetString(3)), rows.GetString(5)),
                given,
                rows.GetInt64(7),
                utc));
        }

        return [.. stops.Select(stop => new SwapStopHistoryEntry(stop, undone.GetValueOrDefault(stop.Sequence)))];
    }

    /// <summary>Writes a time the way the module stores times: round-trip ISO 8601.</summary>
    /// <param name="utc">The time.</param>
    /// <returns>The text.</returns>
    private static string Text(DateTimeOffset utc) => utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
