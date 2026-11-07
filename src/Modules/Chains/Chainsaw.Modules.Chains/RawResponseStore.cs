using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Seals raw responses in the case and finds them again (principles 1 and 9). Every response is sealed with its
/// source, address, status, times (UTC and local with the time zone), the exit it left through, who fetched it, the
/// SHA-256 of its bytes and the bytes themselves; the indexes beside it only say where to look.
/// </summary>
/// <param name="timeZone">The time zone the local time is written in.</param>
internal sealed class RawResponseStore(TimeZoneInfo timeZone)
{
    /// <summary>The chain's name in the tables.</summary>
    public const string ChainName = "bitcoin";

    /// <summary>The source's name in the tables.</summary>
    public const string SourceName = "esplora";

    /// <summary>The columns read back, in order.</summary>
    private const string SelectColumns =
        "sequence, url, kind, subject, status_code, received_utc, received_local, fetched_by, sha256, body";

    /// <summary>Seals a response and adds it to the indexes, in the caller's transaction.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about (a txid, an address, a block hash).</param>
    /// <param name="response">The response.</param>
    /// <param name="fetchedBy">Who fetched it.</param>
    /// <param name="index">
    /// What the response adds to the indexes, or <see langword="null"/> when it is not a usable answer (an error
    /// status or a body that did not add up); it is sealed all the same, but never found again as an answer.
    /// </param>
    /// <returns>The sealed response.</returns>
    public StoredResponse Append(
        ICaseTransaction transaction, string kind, string subject, ExitResponse response, OperatorProfile fetchedBy,
        ResponseIndex? index)
    {
        var body = response.Body.ToArray();
        var sha256 = SHA256.HashData(body);
        var local = TimeZoneInfo.ConvertTime(response.ReceivedAt, timeZone);
        object?[] values =
        [
            ChainName, SourceName, response.Uri.AbsoluteUri, kind, subject, (long)response.StatusCode,
            response.ContentType, Text(response.RequestedAt), Text(response.ReceivedAt), Text(local), timeZone.Id,
            response.ExitId.ToString("D"), JsonSerializer.Serialize(fetchedBy), sha256, body,
        ];
        var record = transaction.AppendSealed(ChainsSchema.RawResponsesChain, values);
        if (index is not null)
        {
            AddToIndexes(transaction, kind, subject, record.Sequence, index);
        }

        var origin = new ResponseOrigin(
            record.Sequence, SourceName, response.Uri, response.StatusCode, response.ReceivedAt, local, fetchedBy,
            Convert.ToHexStringLower(sha256));
        return new StoredResponse(kind, subject, origin, body);
    }

    /// <summary>Finds the newest usable response to a request.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about.</param>
    /// <returns>The response, or <see langword="null"/> when the case has none.</returns>
    public static StoredResponse? Latest(ICaseReader reader, string kind, string subject)
    {
        using var command = reader.CreateCommand(
            "SELECT MAX(sequence) FROM chains_lookup WHERE chain = $chain AND kind = $kind AND subject = $subject");
        Add(command, "$chain", ChainName);
        Add(command, "$kind", kind);
        Add(command, "$subject", subject);
        return command.ExecuteScalar() is long sequence ? Read(reader, sequence) : null;
    }

    /// <summary>
    /// Finds the newest usable response that has a transaction, whether it was asked for alone or came on a page of
    /// an address's transactions.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="txid">The transaction.</param>
    /// <returns>The response, or <see langword="null"/> when the case has none.</returns>
    public static StoredResponse? LatestWithTransaction(ICaseReader reader, Txid txid)
    {
        using var command = reader.CreateCommand("SELECT MAX(sequence) FROM chains_bitcoin_seen WHERE txid = $txid");
        Add(command, "$txid", txid.Value);
        return command.ExecuteScalar() is long sequence ? Read(reader, sequence) : null;
    }

    /// <summary>
    /// Gives the highest block the case knows of: the newest height fetched, or the highest block a fetched
    /// transaction is in, whichever is higher. The chain is at least this high.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The height, or <see langword="null"/> when the case knows of no block.</returns>
    public static long? KnownHeight(ICaseReader reader)
    {
        using var command = reader.CreateCommand(
            """
            SELECT MAX(height) FROM (
                SELECT MAX(height) AS height FROM chains_lookup WHERE chain = $chain
                UNION ALL SELECT MAX(block_height) FROM chains_bitcoin_seen)
            """);
        Add(command, "$chain", ChainName);
        return command.ExecuteScalar() as long?;
    }

    /// <summary>Reads one sealed response and checks its bytes against their SHA-256.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="sequence">The response's number.</param>
    /// <returns>The response, or <see langword="null"/> when there is none with that number.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Corrupt"/> when the bytes do not match their SHA-256.
    /// </exception>
    private static StoredResponse? Read(ICaseReader reader, long sequence)
    {
        using var command = reader.CreateCommand(
            $"SELECT {SelectColumns} FROM {ChainsSchema.RawResponsesTable} WHERE sequence = $sequence");
        Add(command, "$sequence", sequence);
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

        var origin = new ResponseOrigin(
            row.GetInt64(row.GetOrdinal("sequence")),
            SourceName,
            new Uri(row.GetString(row.GetOrdinal("url"))),
            checked((int)row.GetInt64(row.GetOrdinal("status_code"))),
            ParseTime(row.GetString(row.GetOrdinal("received_utc"))),
            ParseTime(row.GetString(row.GetOrdinal("received_local"))),
            JsonSerializer.Deserialize<OperatorProfile>(row.GetString(row.GetOrdinal("fetched_by")))
                ?? throw new CaseException(CaseError.Corrupt),
            Convert.ToHexStringLower(sha256));
        return new StoredResponse(
            row.GetString(row.GetOrdinal("kind")), row.GetString(row.GetOrdinal("subject")), origin, body);
    }

    /// <summary>Adds a sealed response to the indexes.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about.</param>
    /// <param name="sequence">The response's number.</param>
    /// <param name="index">What it adds.</param>
    private static void AddToIndexes(
        ICaseTransaction transaction, string kind, string subject, long sequence, ResponseIndex index)
    {
        using (var command = transaction.CreateCommand(
            """
            INSERT INTO chains_lookup (chain, kind, subject, sequence, height)
            VALUES ($chain, $kind, $subject, $sequence, $height)
            """))
        {
            Add(command, "$chain", ChainName);
            Add(command, "$kind", kind);
            Add(command, "$subject", subject);
            Add(command, "$sequence", sequence);
            Add(command, "$height", index.TipHeight);
            command.ExecuteNonQuery();
        }

        foreach (var (txid, height) in index.Transactions)
        {
            using var command = transaction.CreateCommand(
                """
                INSERT OR IGNORE INTO chains_bitcoin_seen (txid, sequence, block_height)
                VALUES ($txid, $sequence, $height)
                """);
            Add(command, "$txid", txid.Value);
            Add(command, "$sequence", sequence);
            Add(command, "$height", height);
            command.ExecuteNonQuery();
        }
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
