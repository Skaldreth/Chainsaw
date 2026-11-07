using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Pricing;

/// <summary>
/// Seals the rate sources' raw responses in the case and finds them again (principles 1 and 9). Every response is
/// sealed with its source, address, status, times (UTC and local with the time zone), the exit it left through, who
/// fetched it, the SHA-256 of its bytes and the bytes themselves; the indexes beside it only say where to look.
/// </summary>
/// <param name="timeZone">The time zone the local time is written in.</param>
internal sealed class RateResponseStore(TimeZoneInfo timeZone)
{
    /// <summary>The columns read back, in order.</summary>
    private const string SelectColumns =
        "sequence, source, url, kind, subject, status_code, received_utc, received_local, fetched_by, sha256, body";

    /// <summary>The format of a day in the tables and subjects.</summary>
    public const string DayFormat = "yyyy-MM-dd";

    /// <summary>Seals a response, in the caller's transaction.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="source">The source's name.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about.</param>
    /// <param name="response">The response.</param>
    /// <param name="fetchedBy">Who fetched it.</param>
    /// <returns>The sealed response.</returns>
    public StoredRateResponse Append(
        ICaseTransaction transaction, string source, string kind, string subject, ExitResponse response,
        OperatorProfile fetchedBy)
    {
        var body = response.Body.ToArray();
        var sha256 = SHA256.HashData(body);
        var local = TimeZoneInfo.ConvertTime(response.ReceivedAt, timeZone);
        object?[] values =
        [
            source, response.Uri.AbsoluteUri, kind, subject, (long)response.StatusCode, response.ContentType,
            Text(response.RequestedAt), Text(response.ReceivedAt), Text(local), timeZone.Id,
            response.ExitId.ToString("D"), JsonSerializer.Serialize(fetchedBy), sha256, body,
        ];
        var record = transaction.AppendSealed(PricingSchema.RawResponsesChain, values);
        var origin = new RateOrigin(
            record.Sequence, source, response.Uri, response.StatusCode, response.ReceivedAt, local, fetchedBy,
            Convert.ToHexStringLower(sha256));
        return new StoredRateResponse(kind, subject, origin, body);
    }

    /// <summary>Records that a sealed response is the final answer to a request.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="response">The sealed response.</param>
    public static void AddLookup(ICaseTransaction transaction, StoredRateResponse response)
    {
        using var command = transaction.CreateCommand(
            """
            INSERT INTO pricing_lookup (source, kind, subject, sequence)
            VALUES ($source, $kind, $subject, $sequence)
            """);
        Add(command, "$source", response.Origin.Source);
        Add(command, "$kind", response.Kind);
        Add(command, "$subject", response.Subject);
        Add(command, "$sequence", response.Origin.Sequence);
        command.ExecuteNonQuery();
    }

    /// <summary>Records that a sealed response holds a fiat source's final rates over some days.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="series">The series.</param>
    /// <param name="range">The response and its days.</param>
    public static void AddFiatRange(ICaseTransaction transaction, string series, StoredFiatRange range)
    {
        using var command = transaction.CreateCommand(
            """
            INSERT INTO pricing_fiat_ranges (source, series, date_from, date_to, sequence)
            VALUES ($source, $series, $from, $to, $sequence)
            """);
        Add(command, "$source", range.Response.Origin.Source);
        Add(command, "$series", series);
        Add(command, "$from", Day(range.From));
        Add(command, "$to", Day(range.To));
        Add(command, "$sequence", range.Response.Origin.Sequence);
        command.ExecuteNonQuery();
    }

    /// <summary>Finds the newest final answer to a request.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="source">The source's name.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about.</param>
    /// <returns>The response, or <see langword="null"/> when the case has none.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Corrupt"/> when the bytes do not match their SHA-256.
    /// </exception>
    public StoredRateResponse? Latest(ICaseReader reader, string source, string kind, string subject)
    {
        using var command = reader.CreateCommand(
            """
            SELECT MAX(sequence) FROM pricing_lookup
            WHERE source = $source AND kind = $kind AND subject = $subject
            """);
        Add(command, "$source", source);
        Add(command, "$kind", kind);
        Add(command, "$subject", subject);
        return command.ExecuteScalar() is long sequence ? Read(reader, sequence, source, kind, subject) : null;
    }

    /// <summary>Finds the final fiat rates that cover a day, newest first.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="source">The source's name.</param>
    /// <param name="series">The series.</param>
    /// <param name="day">The day.</param>
    /// <returns>The responses and their days.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Corrupt"/> when the bytes do not match their SHA-256.
    /// </exception>
    public List<StoredFiatRange> FiatRangesCovering(ICaseReader reader, string source, string series, DateOnly day)
    {
        var found = new List<(long Sequence, DateOnly From, DateOnly To)>();
        using (var command = reader.CreateCommand(
            """
            SELECT sequence, date_from, date_to FROM pricing_fiat_ranges
            WHERE source = $source AND series = $series AND date_from <= $day AND date_to >= $day
            ORDER BY sequence DESC
            """))
        {
            Add(command, "$source", source);
            Add(command, "$series", series);
            Add(command, "$day", Day(day));
            using var row = command.ExecuteReader();
            while (row.Read())
            {
                if (TryDay(row.GetString(1), out var from) && TryDay(row.GetString(2), out var to))
                {
                    found.Add((row.GetInt64(0), from, to));
                }
            }
        }

        var ranges = new List<StoredFiatRange>();
        foreach (var (sequence, from, to) in found)
        {
            // The index is not sealed: the sealed subject must name the same series and days.
            var response = Read(reader, sequence, source, RateKinds.FiatObservations, FiatSubject(series, from, to));
            if (response is not null)
            {
                ranges.Add(new StoredFiatRange(response, from, to));
            }
        }

        return ranges;
    }

    /// <summary>Gives the subject of a request for a fiat series over some days.</summary>
    /// <param name="series">The series.</param>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <returns>For example <c>SEKUSDPMI/2023-11-01/2023-12-15</c>.</returns>
    public static string FiatSubject(string series, DateOnly from, DateOnly to) => $"{series}/{Day(from)}/{Day(to)}";

    /// <summary>
    /// Reads one sealed response and checks its bytes against their SHA-256. The indexes are not sealed, so the
    /// response must also be a success from the source, of the kind and subject the index named; otherwise it counts
    /// as no answer.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="sequence">The response's number.</param>
    /// <param name="source">The source it must come from.</param>
    /// <param name="kind">The kind it must have.</param>
    /// <param name="subject">The subject it must have.</param>
    /// <returns>The response, or <see langword="null"/> when there is no such response.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Corrupt"/> when the bytes do not match their SHA-256.
    /// </exception>
    private static StoredRateResponse? Read(
        ICaseReader reader, long sequence, string source, string kind, string subject)
    {
        using var command = reader.CreateCommand(
            $"""
            SELECT {SelectColumns} FROM {PricingSchema.RawResponsesTable}
            WHERE sequence = $sequence AND source = $source AND kind = $kind AND subject = $subject
                AND status_code BETWEEN 200 AND 299
            """);
        Add(command, "$sequence", sequence);
        Add(command, "$source", source);
        Add(command, "$kind", kind);
        Add(command, "$subject", subject);
        using var row = command.ExecuteReader();
        if (!row.Read())
        {
            return null;
        }

        var sha256 = row.GetFieldValue<byte[]>(row.GetOrdinal("sha256"));
        var body = row.GetFieldValue<byte[]>(row.GetOrdinal("body"));
        if (!SHA256.HashData(body).AsSpan().SequenceEqual(sha256))
        {
            throw new CaseException(CaseError.Corrupt);
        }

        var origin = new RateOrigin(
            row.GetInt64(row.GetOrdinal("sequence")),
            row.GetString(row.GetOrdinal("source")),
            new Uri(row.GetString(row.GetOrdinal("url"))),
            checked((int)row.GetInt64(row.GetOrdinal("status_code"))),
            ParseTime(row.GetString(row.GetOrdinal("received_utc"))),
            ParseTime(row.GetString(row.GetOrdinal("received_local"))),
            JsonSerializer.Deserialize<OperatorProfile>(row.GetString(row.GetOrdinal("fetched_by")))
                ?? throw new CaseException(CaseError.Corrupt),
            Convert.ToHexStringLower(sha256));
        return new StoredRateResponse(kind, subject, origin, body);
    }

    /// <summary>Adds a parameter to a command.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter's name.</param>
    /// <param name="value">The value; <see langword="null"/> becomes SQL NULL.</param>
    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }

    /// <summary>Writes a day.</summary>
    /// <param name="day">The day.</param>
    /// <returns>For example <c>2023-11-14</c>.</returns>
    private static string Day(DateOnly day) => day.ToString(DayFormat, CultureInfo.InvariantCulture);

    /// <summary>Reads a day written by <see cref="Day"/>.</summary>
    /// <param name="text">The text.</param>
    /// <param name="day">The day.</param>
    /// <returns><see langword="true"/> when it could be read.</returns>
    private static bool TryDay(string text, out DateOnly day) =>
        DateOnly.TryParseExact(text, DayFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out day);

    /// <summary>Writes a time so that it reads back exactly, offset included.</summary>
    /// <param name="time">The time.</param>
    /// <returns>The round-trip text.</returns>
    private static string Text(DateTimeOffset time) => time.ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Reads a time written by <see cref="Text"/>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The time.</returns>
    private static DateTimeOffset ParseTime(string text) =>
        DateTimeOffset.ParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
