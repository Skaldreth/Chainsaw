using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases;

/// <summary>Turns journal drafts into sealed records and journal rows back into entries.</summary>
internal static class JournalRecords
{
    /// <summary>The longest kind code.</summary>
    private const int MaximumKindLength = 64;

    /// <summary>The longest summary.</summary>
    private const int MaximumSummaryLength = 1_000;

    /// <summary>The longest details text.</summary>
    private const int MaximumDetailsLength = 1024 * 1024;

    /// <summary>The round-trip format of times in the journal.</summary>
    private const string TimeFormat = "O";

    /// <summary>Checks a draft.</summary>
    /// <param name="draft">The draft.</param>
    /// <exception cref="ArgumentException">The kind, summary or details are not allowed.</exception>
    public static void Validate(JournalDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var kind = draft.Kind;
        if (string.IsNullOrEmpty(kind) || kind.Length > MaximumKindLength || !char.IsAsciiLetterLower(kind[0])
            || kind.Any(character => !char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character)
                && character is not ('.' or '-' or '_')))
        {
            throw new ArgumentException("The kind must be lower-case ASCII letters, digits and . - _.", nameof(draft));
        }

        if (string.IsNullOrWhiteSpace(draft.Summary) || draft.Summary.Length > MaximumSummaryLength)
        {
            throw new ArgumentException("The summary must be one line of at most 1 000 characters.", nameof(draft));
        }

        if (draft.Details?.Length > MaximumDetailsLength)
        {
            throw new ArgumentException("The details may be at most 1 MiB of text.", nameof(draft));
        }
    }

    /// <summary>Gives the sealed values of a journal record, in the order of the journal's content columns.</summary>
    /// <param name="draft">What happened.</param>
    /// <param name="profile">Who did it.</param>
    /// <param name="utc">When, in UTC.</param>
    /// <param name="timeZone">The local time zone.</param>
    /// <returns>The values.</returns>
    public static object?[] Values(JournalDraft draft, OperatorProfile profile, DateTimeOffset utc, TimeZoneInfo timeZone)
    {
        var instant = utc.ToUniversalTime();
        var local = TimeZoneInfo.ConvertTime(instant, timeZone);
        return
        [
            instant.ToString(TimeFormat, CultureInfo.InvariantCulture),
            local.ToString(TimeFormat, CultureInfo.InvariantCulture),
            timeZone.Id,
            profile.Name,
            profile.Title,
            profile.Organization,
            draft.Kind,
            draft.Summary,
            draft.Details,
        ];
    }

    /// <summary>Reads journal entries in order.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The current transaction, if any.</param>
    /// <param name="fromSequence">The first sequence number.</param>
    /// <param name="maximumCount">The most entries.</param>
    /// <returns>The entries.</returns>
    public static IReadOnlyList<JournalEntry> Read(
        CaseDatabase database, SqliteTransaction? transaction, long fromSequence, int maximumCount)
    {
        var entries = new List<JournalEntry>();
        using var select = database.Command(
            "SELECT sequence, utc, local, time_zone, operator_name, operator_title, operator_organization, kind, "
            + "summary, details, hash FROM journal WHERE sequence >= $from ORDER BY sequence LIMIT $count",
            transaction);
        select.Parameters.AddWithValue("$from", fromSequence);
        select.Parameters.AddWithValue("$count", maximumCount);
        using var reader = select.ExecuteReader();
        while (reader.Read())
        {
            entries.Add(new JournalEntry(
                reader.GetInt64(0),
                ParseTime(reader.GetString(1)),
                ParseTime(reader.GetString(2)),
                reader.GetString(3),
                new OperatorProfile(reader.GetString(4), reader.GetString(5), reader.GetString(6)),
                reader.GetString(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                Convert.ToHexStringLower((byte[])reader.GetValue(10))));
        }

        return entries;
    }

    /// <summary>Gives the entry that was just sealed.</summary>
    /// <param name="record">The sealed record.</param>
    /// <param name="values">The values that were sealed.</param>
    /// <returns>The entry.</returns>
    public static JournalEntry ToEntry(SealedRecord record, object?[] values) =>
        new(
            record.Sequence,
            ParseTime((string)values[0]!),
            ParseTime((string)values[1]!),
            (string)values[2]!,
            new OperatorProfile((string)values[3]!, (string)values[4]!, (string)values[5]!),
            (string)values[6]!,
            (string)values[7]!,
            (string?)values[8],
            record.Hash);

    /// <summary>Parses a time written by <see cref="Values"/>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The time with its offset.</returns>
    private static DateTimeOffset ParseTime(string text) =>
        DateTimeOffset.ParseExact(text, TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None);
}
