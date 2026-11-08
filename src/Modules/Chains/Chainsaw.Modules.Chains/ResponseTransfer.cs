using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Reads sealed raw responses with all their values and seals them again in another case (see
/// <see cref="IResponseTransfer"/>). A response brought in without its index is evidence, and the lookups keep asking
/// the source as before; one brought in with the index it had where it was fetched answers the lookups as it did there.
/// </summary>
internal sealed class ResponseTransfer : IResponseTransfer
{
    /// <summary>The columns read, the sequence first and then the sealed ones in their order.</summary>
    private static readonly string SelectColumns = "sequence, " + string.Join(", ", ChainsSchema.RawResponseColumns);

    /// <inheritdoc/>
    public SealedResponse? Read(ICaseReader reader, long sequence)
    {
        ArgumentNullException.ThrowIfNull(reader);
        using var command = reader.CreateCommand(
            $"SELECT {SelectColumns} FROM {ChainsSchema.RawResponsesTable} WHERE sequence = $sequence");
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$sequence";
        parameter.Value = sequence;
        command.Parameters.Add(parameter);
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

        return new SealedResponse(
            row.GetInt64(row.GetOrdinal("sequence")),
            Text(row, "chain"),
            Text(row, "source"),
            Guid.Parse(Text(row, "endpoint_id")),
            Text(row, "endpoint_name"),
            Text(row, "url"),
            Text(row, "kind"),
            Text(row, "subject"),
            checked((int)row.GetInt64(row.GetOrdinal("status_code"))),
            OptionalText(row, "media_type"),
            Text(row, "requested_utc"),
            Text(row, "received_utc"),
            Text(row, "received_local"),
            Text(row, "time_zone"),
            OptionalText(row, "exit_id"),
            Text(row, "exit_route"),
            Text(row, "fetched_by"),
            Convert.ToHexStringLower(sha256),
            body);
    }

    /// <inheritdoc/>
    public ResponseOrigin Seal(ICaseTransaction transaction, SealedResponse response)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(response);
        var (url, fetchedBy, received, local) = Checked(response);
        var sha256 = Convert.FromHexString(response.Sha256);
        object?[] values =
        [
            response.Chain, response.Source, response.EndpointId.ToString("D"), response.EndpointName, response.Url,
            response.Kind, response.Subject, (long)response.StatusCode, response.MediaType, response.RequestedUtc,
            response.ReceivedUtc, response.ReceivedLocal, response.TimeZone, response.ExitId, response.ExitRoute,
            response.FetchedBy, sha256, response.Body.ToArray(),
        ];
        var record = transaction.AppendSealed(ChainsSchema.RawResponsesChain, values);
        return new ResponseOrigin(
            record.Sequence, response.Source, url, response.StatusCode, received, local, fetchedBy, response.Sha256)
        {
            EndpointId = response.EndpointId,
            Endpoint = response.EndpointName,
        };
    }

    /// <inheritdoc/>
    public SealedResponseIndex? ReadIndex(ICaseReader reader, long sequence)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (ChainOf(reader, sequence) is not { } chain)
        {
            return null;
        }

        var index = new RawResponseStore(TimeZoneInfo.Utc, chain).IndexOf(reader, sequence);
        return index is null
            ? null
            : new SealedResponseIndex(
                index.TipHeight,
                [.. index.Transactions.Select(each => new SealedResponseTransaction(each.Txid.Value, each.Height)
                {
                    BlockHash = each.Hash,
                })]);
    }

    /// <inheritdoc/>
    public ResponseOrigin Seal(ICaseTransaction transaction, SealedResponse response, SealedResponseIndex index)
    {
        ArgumentNullException.ThrowIfNull(index);
        var transactions = new List<(Txid Txid, long? Height, string? Hash)>();
        foreach (var each in index.Transactions)
        {
            if (!Txid.TryParse(each.Txid, out var txid))
            {
                throw new ArgumentException("A transaction in the index is not a transaction id.", nameof(index));
            }

            transactions.Add((txid, each.BlockHeight, each.BlockHash));
        }

        var origin = Seal(transaction, response);
        // The index holds nothing the response does not, and the lookups only take a response through it when the
        // response itself is of the chain, kind and subject asked for; so the response, not the index, names them.
        new RawResponseStore(TimeZoneInfo.Utc, response.Chain).Index(
            transaction, response.Kind, response.Subject, origin.Sequence, new ResponseIndex(transactions, index.Height));
        return origin;
    }

    /// <summary>Reads the chain a sealed response is of.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="sequence">The response's number.</param>
    /// <returns>The chain's name in the record, or <see langword="null"/> when there is no such response.</returns>
    private static string? ChainOf(ICaseReader reader, long sequence)
    {
        using var command = reader.CreateCommand(
            $"SELECT chain FROM {ChainsSchema.RawResponsesTable} WHERE sequence = $sequence");
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$sequence";
        parameter.Value = sequence;
        command.Parameters.Add(parameter);
        return command.ExecuteScalar() as string;
    }

    /// <summary>Checks a response before it is sealed again and reads the values its origin needs.</summary>
    /// <param name="response">The response.</param>
    /// <returns>Its address, who fetched it, and when it arrived (UTC and local).</returns>
    /// <exception cref="ArgumentException">It does not hold together.</exception>
    private static (Uri Url, OperatorProfile FetchedBy, DateTimeOffset Received, DateTimeOffset Local) Checked(
        SealedResponse response)
    {
        // The subject (the chain's height has none) and the endpoint's name may be empty, never missing.
        string[] required =
        [
            response.Chain, response.Source, response.Kind, response.RequestedUtc, response.TimeZone,
            response.ExitRoute,
        ];
        if (required.Any(string.IsNullOrEmpty) || response.Subject is null || response.EndpointName is null
            || response.Body is null || response.Sha256 is null)
        {
            throw new ArgumentException("The response lacks a value.", nameof(response));
        }

        byte[] sha256;
        try
        {
            sha256 = Convert.FromHexString(response.Sha256);
        }
        catch (FormatException exception)
        {
            throw new ArgumentException("The response's SHA-256 is not hex.", nameof(response), exception);
        }

        if (!SHA256.HashData(response.Body).AsSpan().SequenceEqual(sha256))
        {
            throw new ArgumentException("The response's body does not match its SHA-256.", nameof(response));
        }

        try
        {
            var url = new Uri(response.Url, UriKind.Absolute);
            var fetchedBy = JsonSerializer.Deserialize<OperatorProfile>(response.FetchedBy)
                ?? throw new ArgumentException("Who fetched the response is not said.", nameof(response));
            _ = Time(response.RequestedUtc);
            return (url, fetchedBy, Time(response.ReceivedUtc), Time(response.ReceivedLocal));
        }
        catch (Exception exception) when (exception is UriFormatException or JsonException or FormatException)
        {
            throw new ArgumentException("A value of the response cannot be read.", nameof(response), exception);
        }
    }

    /// <summary>Reads a sealed time.</summary>
    /// <param name="text">The round-trip text.</param>
    /// <returns>The time.</returns>
    /// <exception cref="FormatException">It is not one.</exception>
    private static DateTimeOffset Time(string text) =>
        DateTimeOffset.ParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    /// <summary>Reads a text column.</summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <returns>Its text.</returns>
    private static string Text(DbDataReader row, string column) => row.GetString(row.GetOrdinal(column));

    /// <summary>Reads a text column that may be empty.</summary>
    /// <param name="row">The row.</param>
    /// <param name="column">The column.</param>
    /// <returns>Its text, or <see langword="null"/>.</returns>
    private static string? OptionalText(DbDataReader row, string column)
    {
        var ordinal = row.GetOrdinal(column);
        return row.IsDBNull(ordinal) ? null : row.GetString(ordinal);
    }
}
