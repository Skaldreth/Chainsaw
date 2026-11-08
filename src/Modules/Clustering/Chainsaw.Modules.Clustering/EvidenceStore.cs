using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Seals the analyses' conclusions and the user's splits in the case and reads them back. Each result (one analysis
/// of one transaction) is stored as one or more records that share a fingerprint and the run's journal entry; the
/// newest run's records of a result replace the older ones only by being newer, and the older stay sealed. A newer
/// version of an analysis replaces an older version's results of the same transaction in the same way.
/// </summary>
/// <remarks>
/// Everything is read from the sealed tables themselves, without indexes of its own, so that what counts can always
/// be checked against the hash chain. That costs a scan of the table per question; see TODO for large cases.
/// </remarks>
internal static class EvidenceStore
{
    /// <summary>The record kind of an ownership proposal.</summary>
    public const string ProposalKind = "proposal";

    /// <summary>The record kind of a finding.</summary>
    public const string FindingKind = "finding";

    /// <summary>
    /// The record kind of a result where nothing was concluded; it replaces an earlier result that had.
    /// </summary>
    public const string NothingKind = "nothing";

    /// <summary>The columns read back from the conclusions, in order.</summary>
    private const string EvidenceSelect = """
        SELECT sequence, record_kind, heuristic, heuristic_version, txid, addresses, strength, change_output, reason,
               adjustment, finding, basis, fingerprint, journal_sequence, recorded_utc
        FROM clustering_evidence
        """;

    /// <summary>
    /// How findings and address lists are written as JSON; the same everywhere so fingerprints are stable.
    /// </summary>
    /// <remarks>Enums are written by name, so that reordering an enum never changes a sealed record.</remarks>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.General)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Gives the fingerprint of a weighed result: the SHA-256 of everything it would store.</summary>
    /// <param name="result">The weighed result.</param>
    /// <returns>The fingerprint in lower-case hex.</returns>
    public static string Fingerprint(WeighedResult result)
    {
        var content = new
        {
            heuristic = result.Result.Heuristic.Name,
            version = result.Result.Heuristic.Version,
            txid = result.Result.Txid.Value,
            proposals = result.Proposals.Select(weighed => new
            {
                addresses = weighed.Proposal.Addresses.Select(address => address.Value),
                strength = weighed.Strength.ToString(),
                reason = weighed.Proposal.Reason,
                adjustment = weighed.Adjustment,
                change = weighed.Proposal.ChangeOutput,
            }),
            findings = result.Result.Findings.Select(finding => JsonSerializer.Serialize(finding, Json)),
            basis = result.Result.Basis.Select(txid => txid.Value),
        };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(content, Json));
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    /// <summary>
    /// Reads the fingerprint and basis of the newest stored result for every analysis (name and version) of the given
    /// transactions.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="txids">The transactions.</param>
    /// <returns>The stored results by transaction and analysis.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when a record cannot be read back.</exception>
    public static Dictionary<(Txid Txid, HeuristicId Heuristic), StoredResult> LatestResults(
        ICaseReader reader, IReadOnlySet<Txid> txids)
    {
        var latest = new Dictionary<(Txid, HeuristicId), (long Journal, StoredResult Result)>();
        using var command = reader.CreateCommand(
            "SELECT txid, heuristic, heuristic_version, journal_sequence, fingerprint, basis FROM clustering_evidence");
        using var row = command.ExecuteReader();
        while (row.Read())
        {
            var txid = ParseTxid(row.GetString(0));
            if (!txids.Contains(txid))
            {
                continue;
            }

            var key = (txid, Heuristic(row.GetString(1), row.GetInt64(2)));
            var journal = row.GetInt64(3);
            if (!latest.TryGetValue(key, out var known) || journal > known.Journal)
            {
                var basis = Deserialize<List<string>>(row.GetString(5)).Select(ParseTxid).ToList();
                latest[key] = (journal, new StoredResult(row.GetString(4), basis));
            }
        }

        return latest.ToDictionary(entry => entry.Key, entry => entry.Value.Result);
    }

    /// <summary>Seals one weighed result as records under a run's journal entry.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="result">The weighed result.</param>
    /// <param name="fingerprint">Its fingerprint.</param>
    /// <param name="journal">The run's journal entry.</param>
    public static void Append(
        ICaseTransaction transaction, WeighedResult result, string fingerprint, JournalEntry journal)
    {
        var heuristic = result.Result.Heuristic;
        var txid = result.Result.Txid.Value;
        var recorded = journal.Utc.ToString("O", CultureInfo.InvariantCulture);
        var basis = JsonSerializer.Serialize(result.Result.Basis.Select(txid => txid.Value), Json);
        object?[] Row(string kind, string? addresses, string? strength, int? change, string? reason,
            string? adjustment, string? finding) =>
        [
            kind, heuristic.Name, (long)heuristic.Version, txid, addresses, strength, (long?)change, reason,
            adjustment, finding, basis, fingerprint, journal.Sequence, recorded,
        ];

        if (result.Proposals.Count == 0 && result.Result.Findings.Count == 0)
        {
            transaction.AppendSealed(
                ClusteringSchema.EvidenceChain, Row(NothingKind, null, null, null, null, null, null));
            return;
        }

        foreach (var weighed in result.Proposals)
        {
            var addresses = JsonSerializer.Serialize(weighed.Proposal.Addresses.Select(address => address.Value), Json);
            transaction.AppendSealed(ClusteringSchema.EvidenceChain, Row(
                ProposalKind, addresses, weighed.Strength.ToString(), weighed.Proposal.ChangeOutput,
                weighed.Proposal.Reason, weighed.Adjustment, null));
        }

        foreach (var finding in result.Result.Findings)
        {
            transaction.AppendSealed(ClusteringSchema.EvidenceChain, Row(
                FindingKind, null, null, null, finding.Reason, null, JsonSerializer.Serialize(finding, Json)));
        }
    }

    /// <summary>
    /// Reads the records that count now: for every transaction and analysis name, the newest result of the highest
    /// version stored. Each record keeps the order of its result's first appearance, so that analysing again never
    /// changes which of two proposals came first.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="txid">The transaction, or <see langword="null"/> for all.</param>
    /// <param name="withFindings">Whether to read the findings too (the clusters need only the proposals).</param>
    /// <returns>The proposals and findings that count now, in the order of first appearance.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when a record cannot be read back.</exception>
    public static (List<ClusterEvidence> Evidence, List<StoredFinding> Findings) ReadLatest(
        ICaseReader reader, Txid? txid = null, bool withFindings = true)
    {
        using var command = reader.CreateCommand(
            EvidenceSelect + " WHERE $txid IS NULL OR txid = $txid ORDER BY sequence");
        command.AddParameter("$txid", txid?.Value);
        var rows = new List<EvidenceRow>();
        using (var row = command.ExecuteReader())
        {
            while (row.Read())
            {
                rows.Add(ReadRow(row));
            }
        }

        var byName = rows.GroupBy(row => (row.Txid, row.Heuristic.Name)).ToDictionary(
            group => group.Key,
            group =>
            {
                var version = group.Max(row => row.Heuristic.Version);
                var journal = group.Where(row => row.Heuristic.Version == version).Max(row => row.JournalSequence);
                return (Version: version, Journal: journal, First: group.Min(row => row.Sequence));
            });
        var current = rows.Where(row => byName[(row.Txid, row.Heuristic.Name)] is var counting
                && row.Heuristic.Version == counting.Version
                && row.JournalSequence == counting.Journal)
            .OrderBy(row => byName[(row.Txid, row.Heuristic.Name)].First)
            .ThenBy(row => row.Sequence)
            .ToList();
        var evidence = current.Where(row => row.Kind == ProposalKind).Select(ToEvidence).ToList();
        var findings = withFindings
            ? current.Where(row => row.Kind == FindingKind).Select(ToFinding).ToList()
            : [];
        return (evidence, findings);
    }

    /// <summary>Seals a split under its journal entry.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="request">The split.</param>
    /// <param name="journal">The split's journal entry.</param>
    /// <returns>The stored split.</returns>
    public static ClusterSplit AppendSplit(ICaseTransaction transaction, SplitRequest request, JournalEntry journal)
    {
        object?[] values =
        [
            request.First.Value, request.Second.Value, request.Heuristic, request.Reason, journal.Sequence,
            journal.Utc.ToString("O", CultureInfo.InvariantCulture),
        ];
        var record = transaction.AppendSealed(ClusteringSchema.SplitsChain, values);
        return new ClusterSplit(
            record.Sequence, request.First, request.Second, request.Heuristic, request.Reason, journal.Sequence,
            journal.Utc);
    }

    /// <summary>Reads the splits in force (those not undone), in sequence order.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The splits.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when a record cannot be read back.</exception>
    public static List<ClusterSplit> ReadSplits(ICaseReader reader) =>
        [.. ReadSplitHistory(reader).Where(entry => !entry.IsUndone).Select(entry => entry.Split)];

    /// <summary>Reads every split, also those undone, each with its undoing, in sequence order.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The splits and their undoing.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when a record cannot be read back.</exception>
    public static List<SplitHistoryEntry> ReadSplitHistory(ICaseReader reader)
    {
        var undos = ReadUndos(reader);
        using var command = reader.CreateCommand(
            """
            SELECT sequence, first_address, second_address, heuristic, reason, journal_sequence, recorded_utc
            FROM clustering_splits ORDER BY sequence
            """);
        using var row = command.ExecuteReader();
        var splits = new List<SplitHistoryEntry>();
        while (row.Read())
        {
            var split = new ClusterSplit(
                row.GetInt64(0), ParseAddress(row.GetString(1)), ParseAddress(row.GetString(2)), row.GetString(3),
                row.GetString(4), row.GetInt64(5), ParseTime(row.GetString(6)));
            splits.Add(new SplitHistoryEntry(split, undos.GetValueOrDefault(split.Sequence)));
        }

        return splits;
    }

    /// <summary>
    /// Seals that a split is undone under its journal entry; the split's own record is never touched.
    /// </summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="split">The split.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <param name="journal">The journal entry.</param>
    /// <returns>The stored undoing.</returns>
    public static ClusterSplitUndo AppendUndo(
        ICaseTransaction transaction, ClusterSplit split, string? reason, JournalEntry journal)
    {
        object?[] values =
        [
            split.Sequence, reason, journal.Sequence, journal.Utc.ToString("O", CultureInfo.InvariantCulture),
        ];
        var record = transaction.AppendSealed(ClusteringSchema.SplitUndosChain, values);
        return new ClusterSplitUndo(record.Sequence, split.Sequence, reason, journal.Sequence, journal.Utc);
    }

    /// <summary>Reads the undoing of splits; the first record for a split counts.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The undoing of each undone split, by the split's sequence.</returns>
    private static Dictionary<long, ClusterSplitUndo> ReadUndos(ICaseReader reader)
    {
        using var command = reader.CreateCommand(
            """
            SELECT sequence, split_sequence, reason, journal_sequence, recorded_utc
            FROM clustering_split_undos ORDER BY sequence
            """);
        using var row = command.ExecuteReader();
        var undos = new Dictionary<long, ClusterSplitUndo>();
        while (row.Read())
        {
            var undo = new ClusterSplitUndo(
                row.GetInt64(0), row.GetInt64(1), row.IsDBNull(2) ? null : row.GetString(2), row.GetInt64(3),
                ParseTime(row.GetString(4)));
            undos.TryAdd(undo.SplitSequence, undo);
        }

        return undos;
    }

    /// <summary>Reads one record of the conclusions.</summary>
    /// <param name="row">The reader, on the row.</param>
    /// <returns>The record.</returns>
    private static EvidenceRow ReadRow(DbDataReader row) => new(
        row.GetInt64(0),
        row.GetString(1),
        Heuristic(row.GetString(2), row.GetInt64(3)),
        ParseTxid(row.GetString(4)),
        row.IsDBNull(5) ? null : row.GetString(5),
        row.IsDBNull(6) ? null : row.GetString(6),
        row.IsDBNull(7) ? null : checked((int)row.GetInt64(7)),
        row.IsDBNull(8) ? null : row.GetString(8),
        row.IsDBNull(9) ? null : row.GetString(9),
        row.IsDBNull(10) ? null : row.GetString(10),
        row.GetInt64(13),
        ParseTime(row.GetString(14)));

    /// <summary>Turns a proposal record into evidence.</summary>
    /// <param name="row">The record.</param>
    /// <returns>The evidence.</returns>
    private static ClusterEvidence ToEvidence(EvidenceRow row)
    {
        var addresses = Deserialize<List<string>>(row.Addresses).Select(ParseAddress).ToList();
        var strength = Enum.TryParse<EvidenceStrength>(row.Strength, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new CaseException(CaseError.Corrupt);
        return new ClusterEvidence(
            row.Sequence, row.Heuristic, row.Txid, addresses, strength, row.Reason ?? string.Empty, row.Adjustment,
            row.ChangeOutput, row.JournalSequence, row.RecordedUtc);
    }

    /// <summary>Turns a finding record into a stored finding.</summary>
    /// <param name="row">The record.</param>
    /// <returns>The stored finding.</returns>
    private static StoredFinding ToFinding(EvidenceRow row) =>
        new(row.Sequence, row.Heuristic, Deserialize<HeuristicFinding>(row.Finding), row.JournalSequence,
            row.RecordedUtc);

    /// <summary>Reads JSON that Chainsaw wrote; anything else means the case is corrupt.</summary>
    /// <typeparam name="T">What the JSON holds.</typeparam>
    /// <param name="json">The JSON.</param>
    /// <returns>The value.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/>.</exception>
    internal static T Deserialize<T>(string? json)
    {
        try
        {
            return json is null
                ? throw new CaseException(CaseError.Corrupt)
                : JsonSerializer.Deserialize<T>(json, Json) ?? throw new CaseException(CaseError.Corrupt);
        }
        catch (JsonException exception)
        {
            throw new CaseException(CaseError.Corrupt, exception);
        }
    }

    /// <summary>Reads a heuristic's name and version back.</summary>
    /// <param name="name">The name.</param>
    /// <param name="version">The version.</param>
    /// <returns>The heuristic.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when they break the rules.</exception>
    internal static HeuristicId Heuristic(string name, long version)
    {
        try
        {
            return new HeuristicId(name, checked((int)version));
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException)
        {
            throw new CaseException(CaseError.Corrupt, exception);
        }
    }

    /// <summary>Reads a txid back.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The txid.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when it is not one.</exception>
    internal static Txid ParseTxid(string text) =>
        Txid.TryParse(text, out var txid) ? txid : throw new CaseException(CaseError.Corrupt);

    /// <summary>Reads an address back.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The address.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when it is not one.</exception>
    internal static BitcoinAddress ParseAddress(string text) =>
        BitcoinAddress.TryParse(text, out var address) ? address : throw new CaseException(CaseError.Corrupt);

    /// <summary>Reads a time written with the round-trip format back.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The time.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when it is not one.</exception>
    internal static DateTimeOffset ParseTime(string text) =>
        DateTimeOffset.TryParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time)
            ? time
            : throw new CaseException(CaseError.Corrupt);

    /// <summary>One record of the conclusions as read.</summary>
    /// <param name="Sequence">Its number in the chain.</param>
    /// <param name="Kind">Proposal, finding or nothing.</param>
    /// <param name="Heuristic">The analysis.</param>
    /// <param name="Txid">The transaction.</param>
    /// <param name="Addresses">The proposal's addresses as JSON.</param>
    /// <param name="Strength">The proposal's strength.</param>
    /// <param name="ChangeOutput">The proposal's change output.</param>
    /// <param name="Reason">Why.</param>
    /// <param name="Adjustment">Why the strength was lowered.</param>
    /// <param name="Finding">The finding as JSON.</param>
    /// <param name="JournalSequence">The run's journal entry.</param>
    /// <param name="RecordedUtc">When it was stored.</param>
    private sealed record EvidenceRow(
        long Sequence,
        string Kind,
        HeuristicId Heuristic,
        Txid Txid,
        string? Addresses,
        string? Strength,
        int? ChangeOutput,
        string? Reason,
        string? Adjustment,
        string? Finding,
        long JournalSequence,
        DateTimeOffset RecordedUtc);
}

/// <summary>What is stored of a result: enough to tell whether a new result differs, and what it rests on.</summary>
/// <param name="Fingerprint">The result's fingerprint.</param>
/// <param name="Basis">The other transactions the result rests on.</param>
internal sealed record StoredResult(string Fingerprint, IReadOnlyList<Txid> Basis);
