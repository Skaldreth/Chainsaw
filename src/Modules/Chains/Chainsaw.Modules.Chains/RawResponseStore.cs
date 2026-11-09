using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Seals raw responses in the case and finds them again (principles 1 and 9). Every response is sealed with its
/// sort of API and the endpoint it came from (id and name), address, status, times (UTC and local with the time
/// zone), the exit it left through and the sort of way out (VPN or Windows), who fetched it, the SHA-256 of its bytes
/// and the bytes themselves; the indexes beside it only say where to look.
/// </summary>
/// <param name="timeZone">The time zone the local time is written in.</param>
/// <param name="chainName">
/// The chain and network in the tables, for example <c>bitcoin-mainnet</c>, so that answers from a test network or
/// another chain are never found as answers for this one, nor count towards its height.
/// </param>
internal sealed class RawResponseStore(TimeZoneInfo timeZone, string chainName)
{
    /// <summary>Creates the store of a Bitcoin network.</summary>
    /// <param name="timeZone">The time zone the local time is written in.</param>
    /// <param name="network">The network the source answers for.</param>
    public RawResponseStore(TimeZoneInfo timeZone, BitcoinNetwork network)
        : this(timeZone, NameOf(network))
    {
    }

    /// <summary>Gives a UTXO network's name in the tables: Bitcoin's networks, Litecoin's or Zcash's.</summary>
    /// <param name="network">The network.</param>
    /// <returns>For example <c>bitcoin-mainnet</c>.</returns>
    public static string NameOf(BitcoinNetwork network) => network switch
    {
        BitcoinNetwork.Litecoin => LitecoinChainName,
        BitcoinNetwork.Zcash => ZcashChainName,
        _ => "bitcoin-" + network.ToString().ToLowerInvariant(),
    };

    /// <summary>Litecoin's main network in the tables.</summary>
    public const string LitecoinChainName = "litecoin-mainnet";

    /// <summary>Zcash's main network in the tables.</summary>
    public const string ZcashChainName = "zcash-mainnet";

    /// <summary>Gets the chain's name in the tables, for example <c>bitcoin-mainnet</c>.</summary>
    public string ChainName { get; } = chainName;

    /// <summary>
    /// Gets the chain of unspent outputs whose transaction ids the indexes hold, so that an id read back carries it:
    /// Litecoin or Zcash by their names, otherwise Bitcoin (whose indexes are the only others with txids).
    /// </summary>
    private Chain UtxoChain { get; } = chainName switch
    {
        LitecoinChainName => Chain.Litecoin,
        ZcashChainName => Chain.Zcash,
        _ => Chain.Bitcoin,
    };

    /// <summary>The columns read back, in order.</summary>
    private const string SelectColumns =
        "sequence, source, endpoint_id, endpoint_name, url, kind, subject, status_code, received_utc, received_local, "
        + "fetched_by, sha256, body";

    /// <summary>Seals a response and adds it to the indexes, in the caller's transaction.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about (a txid, an address, a block hash).</param>
    /// <param name="response">The response.</param>
    /// <param name="endpoint">The endpoint it came from (principle 1).</param>
    /// <param name="fetchedBy">Who fetched it.</param>
    /// <param name="index">
    /// What the response adds to the indexes, or <see langword="null"/> when it is not a usable answer (an error
    /// status or a body that did not add up); it is sealed all the same, but never found again as an answer.
    /// </param>
    /// <returns>The sealed response.</returns>
    public StoredResponse Append(
        ICaseTransaction transaction, string kind, string subject, ExitResponse response, SourceEndpoint endpoint,
        OperatorProfile fetchedBy, ResponseIndex? index)
    {
        var body = response.Body.ToArray();
        var sha256 = SHA256.HashData(body);
        var local = TimeZoneInfo.ConvertTime(response.ReceivedAt, timeZone);
        object?[] values =
        [
            ChainName, endpoint.Api, endpoint.Id.ToString("D"), endpoint.Name, response.Uri.AbsoluteUri, kind, subject,
            (long)response.StatusCode,
            response.ContentType, Text(response.RequestedAt), Text(response.ReceivedAt), Text(local), timeZone.Id,
            response.ExitIdText, response.RouteText, JsonSerializer.Serialize(fetchedBy), sha256, body,
        ];
        var record = transaction.AppendSealed(ChainsSchema.RawResponsesChain, values);
        if (index is not null)
        {
            AddToIndexes(transaction, kind, subject, record.Sequence, index);
        }

        var origin = new ResponseOrigin(
            record.Sequence, endpoint.Api, response.Uri, response.StatusCode, response.ReceivedAt, local, fetchedBy,
            Convert.ToHexStringLower(sha256))
        {
            EndpointId = endpoint.Id,
            Endpoint = endpoint.Name,
        };
        return new StoredResponse(kind, subject, origin, body);
    }

    /// <summary>Finds the newest usable response to a request.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about.</param>
    /// <returns>The response, or <see langword="null"/> when the case has none.</returns>
    public StoredResponse? Latest(ICaseReader reader, string kind, string subject)
    {
        using var command = reader.CreateCommand(
            "SELECT MAX(sequence) FROM chains_lookup WHERE chain = $chain AND kind = $kind AND subject = $subject");
        Add(command, "$chain", ChainName);
        Add(command, "$kind", kind);
        Add(command, "$subject", subject);
        return command.ExecuteScalar() is long sequence ? Read(reader, sequence, kind, subject) : null;
    }

    /// <summary>
    /// Finds every usable response that has a transaction, newest first, so that a caller can pass over one that
    /// turns out not to be usable (for example a description that contradicts the transaction's bytes).
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="txid">The transaction.</param>
    /// <returns>The responses, read one at a time as they are asked for.</returns>
    public IEnumerable<StoredResponse> AllWithTransaction(ICaseReader reader, Txid txid)
    {
        var sequences = new List<long>();
        using (var command = reader.CreateCommand(
            """
            SELECT sequence FROM chains_bitcoin_seen WHERE chain = $chain AND txid = $txid ORDER BY sequence DESC
            """))
        {
            Add(command, "$chain", ChainName);
            Add(command, "$txid", txid.Value);
            using var rows = command.ExecuteReader();
            while (rows.Read())
            {
                sequences.Add(rows.GetInt64(0));
            }
        }

        foreach (var sequence in sequences)
        {
            if (Read(reader, sequence, null, null) is { } response)
            {
                yield return response;
            }
        }
    }

    /// <summary>
    /// Gives the transactions whose block may have changed since the case first saw them in one: a later answer that
    /// shows another block (another hash, or another height when the hash was not kept) or none, two or more answers to
    /// the transaction itself (a shallow one is asked for again), or a later answer the lookup took as the source not
    /// knowing it, after an answer that had it in a block or in the mempool. Only the indexes are read, so that the answers themselves are read only for these few.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The transactions, in txid order.</returns>
    public IReadOnlyList<Txid> BlockChangeCandidates(ICaseReader reader)
    {
        using var command = reader.CreateCommand(
            $"""
            SELECT a.txid FROM chains_bitcoin_seen a
                JOIN chains_bitcoin_seen b ON b.chain = a.chain AND b.txid = a.txid AND b.sequence > a.sequence
                WHERE a.chain = $chain AND a.block_height IS NOT NULL
                    AND (b.block_height IS NULL OR b.block_height <> a.block_height
                        OR COALESCE(b.block_hash <> a.block_hash, 0))
            UNION
            SELECT subject FROM chains_lookup WHERE chain = $chain AND kind = $kind
                GROUP BY subject HAVING COUNT(*) > 1
            UNION
            SELECT n.subject FROM chains_not_found n
                JOIN {ChainsSchema.RawResponsesTable} r ON r.sequence = n.sequence AND r.chain = n.chain
                    AND r.kind = n.kind AND r.subject = n.subject AND r.status_code = $gone
                JOIN chains_bitcoin_seen s ON s.chain = n.chain AND s.txid = n.subject AND s.sequence < n.sequence
                WHERE n.chain = $chain AND n.kind = $kind
            ORDER BY 1
            """);
        Add(command, "$chain", ChainName);
        Add(command, "$kind", ResponseKinds.Transaction);
        Add(command, "$gone", NotFoundStatus);
        var txids = new List<Txid>();
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            if (Txid.TryParse(UtxoChain, rows.GetString(0), out var txid))
            {
                txids.Add(txid);
            }
        }

        return txids;
    }

    /// <summary>
    /// Gives what may have left its block, on a chain whose answers name no transactions in the indexes (Ethereum):
    /// the subjects of a kind asked two or more times (a shallow answer is asked for again), and those the lookup took
    /// as the source not knowing them. Only the indexes are read.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="kind">What was asked, for example a transaction.</param>
    /// <returns>The subjects, in order.</returns>
    public IReadOnlyList<string> SubjectsAskedAgain(ICaseReader reader, string kind)
    {
        using var command = reader.CreateCommand(
            """
            SELECT subject FROM chains_lookup WHERE chain = $chain AND kind = $kind
                GROUP BY subject HAVING COUNT(*) > 1
            UNION
            SELECT subject FROM chains_not_found WHERE chain = $chain AND kind = $kind
            ORDER BY 1
            """);
        Add(command, "$chain", ChainName);
        Add(command, "$kind", kind);
        var subjects = new List<string>();
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            subjects.Add(rows.GetString(0));
        }

        return subjects;
    }

    /// <summary>Finds every usable response to a request, oldest first.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about.</param>
    /// <returns>The responses.</returns>
    public IReadOnlyList<StoredResponse> All(ICaseReader reader, string kind, string subject) =>
        Sequences(reader, kind, subject)
            .Select(sequence => Read(reader, sequence, kind, subject))
            .OfType<StoredResponse>()
            .ToList();

    /// <summary>
    /// Finds the usable response to a request that stands nearest after an answer: the first sealed after it, or else
    /// the last sealed before it, so that a transaction's block hash is read from the block fetched with it.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about.</param>
    /// <param name="sequence">The answer it is read with.</param>
    /// <returns>The response, or <see langword="null"/> when the case has none.</returns>
    public StoredResponse? Nearest(ICaseReader reader, string kind, string subject, long sequence)
    {
        var sequences = Sequences(reader, kind, subject);
        var nearest = sequences.Where(each => each > sequence).Cast<long?>().FirstOrDefault()
            ?? sequences.Where(each => each < sequence).Cast<long?>().LastOrDefault();
        return nearest is { } found ? Read(reader, found, kind, subject) : null;
    }

    /// <summary>
    /// Finds every answer that a lookup took as the source not knowing what was asked, whatever its status, so that the
    /// caller can check that it says so (an Etherscan-style source answers <c>200</c> with <c>status 0</c>).
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about.</param>
    /// <returns>The answers, oldest first.</returns>
    public IReadOnlyList<StoredResponse> NotFoundAnswers(ICaseReader reader, string kind, string subject)
    {
        var sequences = new List<long>();
        using (var command = reader.CreateCommand(
            """
            SELECT sequence FROM chains_not_found WHERE chain = $chain AND kind = $kind AND subject = $subject
            ORDER BY sequence
            """))
        {
            Add(command, "$chain", ChainName);
            Add(command, "$kind", kind);
            Add(command, "$subject", subject);
            using var rows = command.ExecuteReader();
            while (rows.Read())
            {
                sequences.Add(rows.GetInt64(0));
            }
        }

        return sequences
            .Select(sequence => Read(reader, sequence, kind, subject, anyStatus: true))
            .OfType<StoredResponse>()
            .ToList();
    }

    /// <summary>Gives the numbers of every usable response to a request, oldest first.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about.</param>
    /// <returns>The numbers.</returns>
    private List<long> Sequences(ICaseReader reader, string kind, string subject)
    {
        using var command = reader.CreateCommand(
            """
            SELECT sequence FROM chains_lookup WHERE chain = $chain AND kind = $kind AND subject = $subject
            ORDER BY sequence
            """);
        Add(command, "$chain", ChainName);
        Add(command, "$kind", kind);
        Add(command, "$subject", subject);
        var sequences = new List<long>();
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            sequences.Add(rows.GetInt64(0));
        }

        return sequences;
    }

    /// <summary>
    /// Notes that a lookup took a sealed answer as the source not knowing what was asked (an endpoint it trusts, or
    /// every endpoint asked, said so). The note is an index beside the sealed answer, which says nothing more than the
    /// answer and the lookup's decision; an answer from an endpoint the lookup did not believe is never noted.
    /// </summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about.</param>
    /// <param name="sequence">The sealed answer's number.</param>
    public void MarkNotFound(ICaseTransaction transaction, string kind, string subject, long sequence)
    {
        using var command = transaction.CreateCommand(
            """
            INSERT OR IGNORE INTO chains_not_found (chain, kind, subject, sequence)
            VALUES ($chain, $kind, $subject, $sequence)
            """);
        Add(command, "$chain", ChainName);
        Add(command, "$kind", kind);
        Add(command, "$subject", subject);
        Add(command, "$sequence", sequence);
        command.ExecuteNonQuery();
    }

    /// <summary>
    /// Finds every answer that a lookup took as the source not knowing a transaction (<c>404</c>, see
    /// <see cref="MarkNotFound"/>): sealed as every answer is, but never used as one. Only an answer that is a
    /// <c>404</c> to that very request counts, whatever the register says.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="txid">The transaction.</param>
    /// <returns>Each answer's number, when it came in local time, and its endpoint's id and name, oldest first.</returns>
    public IReadOnlyList<(long Sequence, DateTimeOffset ReceivedLocal, string EndpointId, string Endpoint)> NotFound(
        ICaseReader reader, Txid txid)
    {
        using var command = reader.CreateCommand(
            $"""
            SELECT r.sequence, r.received_local, r.endpoint_id, r.endpoint_name FROM chains_not_found n
                JOIN {ChainsSchema.RawResponsesTable} r ON r.sequence = n.sequence AND r.chain = n.chain
                    AND r.kind = n.kind AND r.subject = n.subject AND r.status_code = $gone
            WHERE n.chain = $chain AND n.kind = $kind AND n.subject = $txid
            ORDER BY r.sequence
            """);
        Add(command, "$chain", ChainName);
        Add(command, "$kind", ResponseKinds.Transaction);
        Add(command, "$txid", txid.Value);
        Add(command, "$gone", NotFoundStatus);
        var found = new List<(long, DateTimeOffset, string, string)>();
        using var row = command.ExecuteReader();
        while (row.Read())
        {
            found.Add((row.GetInt64(0), ParseTime(row.GetString(1)), row.GetString(2), row.GetString(3)));
        }

        return found;
    }

    /// <summary>
    /// Gives the hosts of the sealed answers the lookup took as the source not knowing a transaction, after an answer,
    /// read from the sealed addresses, so that what the case knew then is what counts.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="txid">The transaction.</param>
    /// <param name="after">The answer they came after.</param>
    /// <returns>The hosts, each once, in lower case.</returns>
    public IReadOnlyList<string> NotFoundHosts(ICaseReader reader, Txid txid, long after)
    {
        using var command = reader.CreateCommand(
            $"""
            SELECT DISTINCT r.url FROM chains_not_found n
                JOIN {ChainsSchema.RawResponsesTable} r ON r.sequence = n.sequence AND r.chain = n.chain
                    AND r.kind = n.kind AND r.subject = n.subject AND r.status_code = $gone
            WHERE n.chain = $chain AND n.kind = $kind AND n.subject = $txid AND n.sequence > $after
            """);
        Add(command, "$chain", ChainName);
        Add(command, "$kind", ResponseKinds.Transaction);
        Add(command, "$txid", txid.Value);
        Add(command, "$gone", NotFoundStatus);
        Add(command, "$after", after);
        return HostsOf(command);
    }

    /// <summary>
    /// Gives the hosts every sealed answer on the chain came from, read from the sealed addresses: the sources the case
    /// has heard from, whatever is switched on now.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The hosts, each once, in lower case.</returns>
    public IReadOnlyList<string> AnsweringHosts(ICaseReader reader)
    {
        using var command = reader.CreateCommand(
            $"SELECT MIN(url) FROM {ChainsSchema.RawResponsesTable} WHERE chain = $chain GROUP BY endpoint_id");
        Add(command, "$chain", ChainName);
        return HostsOf(command);
    }

    /// <summary>Reads the hosts of the addresses a command gives in its first column.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The hosts, each once, in lower case.</returns>
    private static List<string> HostsOf(DbCommand command)
    {
        var hosts = new HashSet<string>(StringComparer.Ordinal);
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            if (Uri.TryCreate(rows.GetString(0), UriKind.Absolute, out var url))
            {
                hosts.Add(url.Host.ToLowerInvariant());
            }
        }

        return [.. hosts.Order(StringComparer.Ordinal)];
    }

    /// <summary>Says whether a lookup took a sealed answer as the source not knowing what was asked.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="sequence">The answer's number.</param>
    /// <returns><see langword="true"/> when it is noted.</returns>
    public bool IsNotFound(ICaseReader reader, long sequence)
    {
        using var command = reader.CreateCommand(
            "SELECT 1 FROM chains_not_found WHERE chain = $chain AND sequence = $sequence LIMIT 1");
        Add(command, "$chain", ChainName);
        Add(command, "$sequence", sequence);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>The status with which a source says that it does not know what was asked about.</summary>
    private const long NotFoundStatus = 404;

    /// <summary>
    /// The blocks more than its pace a chain may make between two times when a lower bound is counted from a later
    /// height (<see cref="LowestAt"/>): blocks come at random, and over a short time many may come close together.
    /// </summary>
    private const long Burst = 24;

    /// <summary>
    /// Gives the highest block an endpoint had shown up to an answer: the chain's heights it answered and the blocks of
    /// the transactions in its answers. A newer answer from an endpoint that had not come as far as a block says
    /// nothing about that block, since the endpoint may only lag behind (finding C1 of GR9a). The heights are read as
    /// <see cref="Heights"/> describes.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="endpointId">The endpoint's id as sealed.</param>
    /// <param name="upTo">The last answer to count.</param>
    /// <returns>The height, or <see langword="null"/> when its answers show none.</returns>
    public long? EndpointHeight(ICaseReader reader, string endpointId, long upTo) =>
        Heights(reader, endpointId, upTo);

    /// <summary>
    /// Gives the highest block the case knows of: the newest height fetched, or the highest block a fetched
    /// transaction is in, whichever is higher. The chain is at least this high. The heights are read as
    /// <see cref="Heights"/> describes.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="upTo">
    /// The last response to count, so that the height the case knew when a response was sealed can be read; every
    /// response when <see langword="null"/>.
    /// </param>
    /// <param name="receivedBy">
    /// Only responses received at or before this time, so that the height the case knew at a time can be read; every
    /// response when <see langword="null"/>.
    /// </param>
    /// <returns>The height, or <see langword="null"/> when the case knows of no block.</returns>
    public long? KnownHeight(ICaseReader reader, long? upTo = null, DateTimeOffset? receivedBy = null) =>
        Heights(reader, null, upTo, receivedBy);

    /// <summary>
    /// Gives the lowest the chain can have been at a time, from the heights fetched after it: each, as
    /// <see cref="Heights"/> counts it, less the most blocks the chain can have made between (twice as fast as it has
    /// ever made them on average, and a burst more for the chance of many blocks close together). It gives a bound
    /// when the case had fetched no height by then; it is lower than the chain was, never higher.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="at">The time.</param>
    /// <returns>
    /// The height, or <see langword="null"/> when no counted answer came after it or the chain's pace is not known.
    /// </returns>
    public long? LowestAt(ICaseReader reader, DateTimeOffset at)
    {
        if (ChainLimits.Pace(ChainName) is not { } pace)
        {
            return null;
        }

        using var command = reader.CreateCommand(
            $"""
            SELECT l.height, unixepoch(r.received_utc) FROM chains_lookup l
                JOIN {ChainsSchema.RawResponsesTable} r ON r.sequence = l.sequence AND r.chain = l.chain
                    AND r.kind = l.kind AND r.subject = l.subject AND r.status_code BETWEEN 200 AND 299
                WHERE l.chain = $chain AND l.height IS NOT NULL AND unixepoch(r.received_utc) > $at
            """);
        Add(command, "$chain", ChainName);
        Add(command, "$at", at.ToUnixTimeSeconds());
        long? lowest = null;
        using var rows = command.ExecuteReader();
        while (rows.Read())
        {
            if (rows.IsDBNull(1))
            {
                continue;
            }

            var received = rows.GetInt64(1);
            var height = Math.Min(rows.GetInt64(0), BlocksIn(received - pace.Made.ToUnixTimeSeconds(), pace.Fastest));
            var made = BlocksIn(received - at.ToUnixTimeSeconds(), pace.Fastest) + 1 + Burst;
            lowest = Math.Max(lowest ?? 0, Math.Max(0, height - made));
        }

        return lowest;
    }

    /// <summary>
    /// Gives how many blocks a chain can make at most in some seconds at its fastest pace, counted in ticks, so that a
    /// pace under a second (BSC's) is never rounded to zero.
    /// </summary>
    /// <param name="seconds">The seconds.</param>
    /// <param name="fastest">The shortest time between blocks.</param>
    /// <returns>The blocks.</returns>
    private static long BlocksIn(long seconds, TimeSpan fastest) => seconds * TimeSpan.TicksPerSecond / fastest.Ticks;

    /// <summary>
    /// Gives the highest block the indexes show. The indexes are not sealed, so a height only counts when its row
    /// points at a sealed successful answer of this chain (of the kind and subject the lookup row names), and never
    /// above the highest the chain can have come by when that answer was received (<see cref="ChainLimits"/>). A row
    /// written by hand into the file can then move the height no further than the chain's plausible bound. The rows
    /// are read highest first and the reading stops at the first row no higher than the height found, so that a case
    /// with many answers is not read through on every lookup.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="endpointId">Only answers of this endpoint, or <see langword="null"/> for every answer.</param>
    /// <param name="upTo">The last answer to count, or <see langword="null"/> for every answer.</param>
    /// <param name="receivedBy">
    /// Only answers received at or before this time, or <see langword="null"/> for every answer.
    /// </param>
    /// <returns>The height, or <see langword="null"/> when no counted answer shows one.</returns>
    private long? Heights(ICaseReader reader, string? endpointId, long? upTo, DateTimeOffset? receivedBy = null)
    {
        const string Filter = """
            AND r.status_code BETWEEN 200 AND 299
            AND ($endpoint IS NULL OR r.endpoint_id = $endpoint) AND ($upTo IS NULL OR r.sequence <= $upTo)
            AND ($by IS NULL OR unixepoch(r.received_utc) <= $by)
            """;
        var pace = ChainLimits.Pace(ChainName);
        long? highest = null;
        foreach (var query in (string[])[
            $"""
            SELECT l.height, unixepoch(r.received_utc) FROM chains_lookup l
                JOIN {ChainsSchema.RawResponsesTable} r ON r.sequence = l.sequence AND r.chain = l.chain
                    AND r.kind = l.kind AND r.subject = l.subject {Filter}
                WHERE l.chain = $chain AND l.height IS NOT NULL ORDER BY l.height DESC
            """,
            $"""
            SELECT s.block_height, unixepoch(r.received_utc) FROM chains_bitcoin_seen s
                JOIN {ChainsSchema.RawResponsesTable} r ON r.sequence = s.sequence AND r.chain = s.chain {Filter}
                WHERE s.chain = $chain AND s.block_height IS NOT NULL ORDER BY s.block_height DESC
            """])
        {
            using var command = reader.CreateCommand(query);
            Add(command, "$chain", ChainName);
            Add(command, "$endpoint", endpointId);
            Add(command, "$upTo", upTo);
            Add(command, "$by", receivedBy?.ToUnixTimeSeconds());
            using var rows = command.ExecuteReader();
            while (rows.Read() && !(rows.GetInt64(0) <= highest))
            {
                var height = rows.GetInt64(0);
                if (pace is { } known)
                {
                    if (rows.IsDBNull(1))
                    {
                        // A received time SQLite cannot read gives no bound to hold the height to: not counted.
                        continue;
                    }

                    var most = BlocksIn(rows.GetInt64(1) - known.Made.ToUnixTimeSeconds(), known.Fastest);
                    height = Math.Min(height, Math.Max(0, most));
                }

                highest = Math.Max(highest ?? height, height);
            }
        }

        return highest;
    }

    /// <summary>
    /// Reads one sealed response and checks its bytes against their SHA-256. The indexes are not sealed, so the
    /// response must also be on this chain and, when given, of the kind and subject the index named; otherwise it
    /// counts as no answer.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="sequence">The response's number.</param>
    /// <param name="kind">The kind it must have, or <see langword="null"/> for any.</param>
    /// <param name="subject">The subject it must have, or <see langword="null"/> for any.</param>
    /// <param name="anyStatus">
    /// Whether an answer with any status is read, for an answer that said the source does not know what was asked;
    /// otherwise only a successful one.
    /// </param>
    /// <returns>The response, or <see langword="null"/> when there is no such response.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Corrupt"/> when the bytes do not match their SHA-256.
    /// </exception>
    private StoredResponse? Read(
        ICaseReader reader, long sequence, string? kind, string? subject, bool anyStatus = false)
    {
        using var command = reader.CreateCommand(
            $"""
            SELECT {SelectColumns} FROM {ChainsSchema.RawResponsesTable}
            WHERE sequence = $sequence AND chain = $chain AND ($any = 1 OR status_code BETWEEN 200 AND 299)
                AND ($kind IS NULL OR kind = $kind) AND ($subject IS NULL OR subject = $subject)
            """);
        Add(command, "$any", anyStatus ? 1L : 0L);
        Add(command, "$sequence", sequence);
        Add(command, "$chain", ChainName);
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

        var origin = new ResponseOrigin(
            row.GetInt64(row.GetOrdinal("sequence")),
            row.GetString(row.GetOrdinal("source")),
            new Uri(row.GetString(row.GetOrdinal("url"))),
            checked((int)row.GetInt64(row.GetOrdinal("status_code"))),
            ParseTime(row.GetString(row.GetOrdinal("received_utc"))),
            ParseTime(row.GetString(row.GetOrdinal("received_local"))),
            JsonSerializer.Deserialize<OperatorProfile>(row.GetString(row.GetOrdinal("fetched_by")))
                ?? throw new CaseException(CaseError.Corrupt),
            Convert.ToHexStringLower(sha256))
        {
            EndpointId = Guid.Parse(row.GetString(row.GetOrdinal("endpoint_id"))),
            Endpoint = row.GetString(row.GetOrdinal("endpoint_name")),
        };
        return new StoredResponse(
            row.GetString(row.GetOrdinal("kind")), row.GetString(row.GetOrdinal("subject")), origin, body);
    }

    /// <summary>
    /// Reads what the indexes hold of one sealed response of this chain: the chain's height it shows and the
    /// transactions in it.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="sequence">The response's number.</param>
    /// <returns>The index entry, or <see langword="null"/> when the response is not in the indexes.</returns>
    public ResponseIndex? IndexOf(ICaseReader reader, long sequence)
    {
        long? height;
        using (var command = reader.CreateCommand(
            "SELECT height FROM chains_lookup WHERE chain = $chain AND sequence = $sequence"))
        {
            Add(command, "$chain", ChainName);
            Add(command, "$sequence", sequence);
            using var row = command.ExecuteReader();
            if (!row.Read())
            {
                return null;
            }

            height = row.IsDBNull(0) ? null : row.GetInt64(0);
        }

        var transactions = new List<(Txid Txid, long? Height, string? Hash)>();
        using (var command = reader.CreateCommand(
            """
            SELECT txid, block_height, block_hash FROM chains_bitcoin_seen WHERE chain = $chain AND sequence = $sequence
            ORDER BY txid
            """))
        {
            Add(command, "$chain", ChainName);
            Add(command, "$sequence", sequence);
            using var rows = command.ExecuteReader();
            while (rows.Read())
            {
                transactions.Add((
                    Txid.Parse(UtxoChain, rows.GetString(0)), rows.IsDBNull(1) ? null : rows.GetInt64(1),
                    rows.IsDBNull(2) ? null : rows.GetString(2)));
            }
        }

        return new ResponseIndex(transactions, height);
    }

    /// <summary>
    /// Adds a response that was sealed by other means (sealed again from another case) to the indexes, so that the
    /// lookups find it as they find one they fetched themselves.
    /// </summary>
    /// <param name="transaction">The case's write transaction, the one the response was sealed in.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about.</param>
    /// <param name="sequence">The response's number in this case.</param>
    /// <param name="index">What it adds.</param>
    public void Index(ICaseTransaction transaction, string kind, string subject, long sequence, ResponseIndex index) =>
        AddToIndexes(transaction, kind, subject, sequence, index);

    /// <summary>Adds a sealed response to the indexes.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="kind">What was asked.</param>
    /// <param name="subject">What it was asked about.</param>
    /// <param name="sequence">The response's number.</param>
    /// <param name="index">What it adds.</param>
    private void AddToIndexes(
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

        foreach (var (txid, height, hash) in index.Transactions)
        {
            using var command = transaction.CreateCommand(
                """
                INSERT OR IGNORE INTO chains_bitcoin_seen (chain, txid, sequence, block_height, block_hash)
                VALUES ($chain, $txid, $sequence, $height, $hash)
                """);
            Add(command, "$hash", hash);
            Add(command, "$chain", ChainName);
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
