using System.Data.Common;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Security.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>Reads and writes the findings and decisions sealed in a case.</summary>
internal static class CheckStore
{
    /// <summary>Reads the latest sealed record of every finding, by analysis, version and key.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The records.</returns>
    public static Dictionary<ResultKey, SealedResult> ReadResults(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT sequence, analysis, analysis_version, finding_key, content, fingerprint, journal_sequence,
                   recorded_utc
            FROM checks_results ORDER BY sequence
            """);
        using var rows = command.ExecuteReader();
        var results = new Dictionary<ResultKey, SealedResult>();
        while (rows.Read())
        {
            var key = new ResultKey(rows.GetString(1), rows.GetInt64(2), rows.GetString(3));
            results[key] = new SealedResult(
                rows.GetInt64(0), rows.GetString(4), rows.GetString(5), rows.GetInt64(6), Time(rows, 7));
        }

        return results;
    }

    /// <summary>Reads every decision, oldest first, by the finding it is about.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The decisions.</returns>
    public static Dictionary<ResultKey, List<CheckDecision>> ReadDecisions(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT sequence, analysis, analysis_version, finding_key, candidate_key, verdict, reason, decided_by,
                   journal_sequence, recorded_utc
            FROM checks_decisions ORDER BY sequence
            """);
        using var rows = command.ExecuteReader();
        var decisions = new Dictionary<ResultKey, List<CheckDecision>>();
        while (rows.Read())
        {
            var key = new ResultKey(rows.GetString(1), rows.GetInt64(2), rows.GetString(3));
            var decision = new CheckDecision(
                rows.GetInt64(0),
                new HeuristicId(key.Analysis, (int)key.Version),
                key.FindingKey,
                rows.IsDBNull(4) ? null : rows.GetString(4),
                Enum.Parse<CheckVerdict>(rows.GetString(5)),
                rows.IsDBNull(6) ? null : rows.GetString(6),
                Operator(rows.GetString(7)),
                Time(rows, 9),
                rows.GetInt64(8));
            if (!decisions.TryGetValue(key, out var list))
            {
                decisions[key] = list = [];
            }

            list.Add(decision);
        }

        return decisions;
    }

    /// <summary>Seals a finding.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="key">The finding's analysis, version and key.</param>
    /// <param name="finding">The finding.</param>
    /// <param name="content">Its JSON.</param>
    /// <param name="journal">The run's journal entry.</param>
    /// <returns>The sealed record.</returns>
    public static SealedResult Seal(
        ICaseTransaction transaction, ResultKey key, CheckFinding finding, string content, JournalEntry journal)
    {
        var fingerprint = FindingCodec.Fingerprint(content);
        var record = transaction.AppendSealed(
            CheckSchema.ResultsChain,
            [
                key.Analysis, key.Version, key.FindingKey, finding.Kind.ToString(), content, fingerprint,
                journal.Sequence, FindingCodec.Text(journal.Utc),
            ]);
        return new SealedResult(record.Sequence, content, fingerprint, journal.Sequence, journal.Utc);
    }

    /// <summary>Seals a decision.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="request">The decision.</param>
    /// <param name="reason">The checked reason, or <see langword="null"/>.</param>
    /// <param name="result">The sealed finding it is about.</param>
    /// <param name="journal">The decision's journal entry.</param>
    /// <returns>The sealed decision.</returns>
    public static CheckDecision SealDecision(
        ICaseTransaction transaction,
        DecisionRequest request,
        string? reason,
        SealedResult result,
        JournalEntry journal)
    {
        var record = transaction.AppendSealed(
            CheckSchema.DecisionsChain,
            [
                request.Analysis.Name, (long)request.Analysis.Version, request.FindingKey, request.CandidateKey,
                request.Verdict.ToString(), reason, JsonSerializer.Serialize(journal.Operator), result.Sequence,
                journal.Sequence, FindingCodec.Text(journal.Utc),
            ]);
        return new CheckDecision(
            record.Sequence,
            request.Analysis,
            request.FindingKey,
            request.CandidateKey,
            request.Verdict,
            reason,
            journal.Operator,
            journal.Utc,
            journal.Sequence);
    }

    /// <summary>Reads an operator profile as sealed.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="InvalidDataException">The JSON is empty.</exception>
    public static OperatorProfile Operator(string json) =>
        JsonSerializer.Deserialize<OperatorProfile>(json) ?? throw new InvalidDataException("A record has no operator.");

    /// <summary>Reads a stored time from a column.</summary>
    /// <param name="rows">The rows.</param>
    /// <param name="column">The column.</param>
    /// <returns>The time.</returns>
    private static DateTimeOffset Time(DbDataReader rows, int column) => FindingCodec.Time(rows.GetString(column));
}

/// <summary>What a finding is sealed by: its analysis, the analysis's version and the finding's key.</summary>
/// <param name="Analysis">The analysis's name.</param>
/// <param name="Version">The version.</param>
/// <param name="FindingKey">The finding's key.</param>
internal sealed record ResultKey(string Analysis, long Version, string FindingKey);

/// <summary>The latest sealed record of a finding.</summary>
/// <param name="Sequence">The record's number.</param>
/// <param name="Content">The finding as JSON.</param>
/// <param name="Fingerprint">The JSON's fingerprint.</param>
/// <param name="JournalSequence">The run's journal entry.</param>
/// <param name="RecordedUtc">When.</param>
internal sealed record SealedResult(
    long Sequence, string Content, string Fingerprint, long JournalSequence, DateTimeOffset RecordedUtc);
