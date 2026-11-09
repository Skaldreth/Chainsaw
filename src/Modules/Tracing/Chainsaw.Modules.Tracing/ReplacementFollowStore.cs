using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Reads and writes the user's choices to follow the transaction that replaced a gone one (sketch GR9a) in the sealed
/// chain <see cref="TracingSchema.ReplacementFollowsChain"/>: a choice and its taking back are each a record of their
/// own, and no record is ever changed (principle 1).
/// </summary>
internal static class ReplacementFollowStore
{
    /// <summary>The action of a record that chooses to follow a replacer.</summary>
    public const string FollowAction = "follow";

    /// <summary>The action of a record that takes such a choice back.</summary>
    public const string TakeBackAction = "take-back";

    /// <summary>Seals a choice to follow a replacer.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="gone">The gone transaction.</param>
    /// <param name="replacer">The replacer.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <param name="journal">The journal entry that records it.</param>
    /// <returns>The choice.</returns>
    public static ReplacementFollow Seal(
        ICaseTransaction transaction, Txid gone, Txid replacer, string? reason, JournalEntry journal)
    {
        var record = transaction.AppendSealed(
            TracingSchema.ReplacementFollowsChain,
            [FollowAction, null, gone.Value, replacer.Value, reason ?? string.Empty, journal.Sequence,
                Text(journal.Utc)]);
        return new ReplacementFollow(record.Sequence, gone, replacer, reason, journal.Sequence, journal.Utc);
    }

    /// <summary>Seals that a choice is taken back, as a new record; the choice's own record is never touched.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="follow">The choice.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <param name="journal">The journal entry that records it.</param>
    public static void SealTakeBack(
        ICaseTransaction transaction, ReplacementFollow follow, string? reason, JournalEntry journal) =>
        transaction.AppendSealed(
            TracingSchema.ReplacementFollowsChain,
            [TakeBackAction, follow.Sequence, follow.Gone.Value, follow.Replacer.Value, reason ?? string.Empty,
                journal.Sequence, Text(journal.Utc)]);

    /// <summary>Reads the choices in force: those not taken back, oldest first.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The choices.</returns>
    public static List<ReplacementFollow> ReadInForce(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT sequence, action, follow_sequence, gone_txid, replacer_txid, reason, journal_sequence, recorded_utc
            FROM tracing_replacement_follows ORDER BY sequence
            """);
        using var rows = command.ExecuteReader();
        var follows = new List<ReplacementFollow>();
        var takenBack = new HashSet<long>();
        while (rows.Read())
        {
            if (rows.GetString(1) == TakeBackAction)
            {
                takenBack.Add(rows.GetInt64(2));
                continue;
            }

            var reason = rows.GetString(5);
            follows.Add(new ReplacementFollow(
                rows.GetInt64(0),
                Txid.Parse(rows.GetString(3)),
                Txid.Parse(rows.GetString(4)),
                reason.Length == 0 ? null : reason,
                rows.GetInt64(6),
                DateTimeOffset.Parse(rows.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)));
        }

        return [.. follows.Where(follow => !takenBack.Contains(follow.Sequence))];
    }

    /// <summary>Writes a time the way the module stores times: round-trip ISO 8601.</summary>
    /// <param name="utc">The time.</param>
    /// <returns>The text.</returns>
    private static string Text(DateTimeOffset utc) => utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
