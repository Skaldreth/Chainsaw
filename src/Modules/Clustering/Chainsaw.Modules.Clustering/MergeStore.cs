using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Seals the user's own merges (<c>user v1</c>) and their undoing in the case and reads them back. A merge's record is
/// never touched; undoing it is a record of its own that points at it, as for the splits.
/// </summary>
internal static class MergeStore
{
    /// <summary>Seals a merge under its journal entry.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="request">The merge, checked.</param>
    /// <param name="reason">The user's reason, or <see langword="null"/>.</param>
    /// <param name="origin">The record the merge came from, or <see langword="null"/>.</param>
    /// <param name="journal">The merge's journal entry.</param>
    /// <returns>The stored merge.</returns>
    public static ClusterMerge Append(
        ICaseTransaction transaction, MergeRequest request, string? reason, MergeOrigin? origin, JournalEntry journal)
    {
        object?[] values =
        [
            JsonSerializer.Serialize(request.Addresses.Select(address => address.Value)),
            request.Txid.Stored,
            request.Proposal.Name,
            (long)request.Proposal.Version,
            request.Grounds,
            reason,
            origin?.Chain,
            origin?.Sequence,
            journal.Sequence,
            journal.Utc.ToString("O", CultureInfo.InvariantCulture),
        ];
        var record = transaction.AppendSealed(ClusteringSchema.MergesChain, values);
        return new ClusterMerge(
            record.Sequence, [.. request.Addresses], request.Txid, request.Proposal, request.Grounds, reason, origin,
            journal.Sequence, journal.Utc);
    }

    /// <summary>Seals that a merge is undone under its journal entry; the merge's record is never touched.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="merge">The merge.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <param name="journal">The journal entry.</param>
    /// <returns>The stored undoing.</returns>
    public static ClusterMergeUndo AppendUndo(
        ICaseTransaction transaction, ClusterMerge merge, string? reason, JournalEntry journal)
    {
        object?[] values =
        [
            merge.Sequence, reason, journal.Sequence, journal.Utc.ToString("O", CultureInfo.InvariantCulture),
        ];
        var record = transaction.AppendSealed(ClusteringSchema.MergeUndosChain, values);
        return new ClusterMergeUndo(record.Sequence, merge.Sequence, reason, journal.Sequence, journal.Utc);
    }

    /// <summary>Reads the merges in force (those not undone), in sequence order.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The merges.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when a record cannot be read back.</exception>
    public static List<ClusterMerge> ReadInForce(ICaseReader reader) =>
        [.. ReadHistory(reader).Where(entry => !entry.IsUndone).Select(entry => entry.Merge)];

    /// <summary>Reads every merge, also those undone, each with its undoing, in sequence order.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The merges and their undoing.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when a record cannot be read back.</exception>
    public static List<MergeHistoryEntry> ReadHistory(ICaseReader reader)
    {
        var undos = ReadUndos(reader);
        using var command = reader.CreateCommand(
            """
            SELECT sequence, addresses, txid, proposal, proposal_version, grounds, reason, origin_chain,
                   origin_sequence, journal_sequence, recorded_utc
            FROM clustering_merges ORDER BY sequence
            """);
        using var row = command.ExecuteReader();
        var merges = new List<MergeHistoryEntry>();
        while (row.Read())
        {
            var origin = row.IsDBNull(7) || row.IsDBNull(8) ? null : new MergeOrigin(row.GetString(7), row.GetInt64(8));
            var addresses = EvidenceStore.Deserialize<List<string>>(row.GetString(1))
                .Select(EvidenceStore.ParseAddress)
                .ToList();
            if (addresses.Count < 2 || addresses.Distinct().Count() != addresses.Count)
            {
                throw new CaseException(CaseError.Corrupt);
            }

            var merge = new ClusterMerge(
                row.GetInt64(0),
                addresses,
                EvidenceStore.ParseTxid(row.GetString(2)),
                EvidenceStore.Heuristic(row.GetString(3), row.GetInt64(4)),
                row.GetString(5),
                row.IsDBNull(6) ? null : row.GetString(6),
                origin,
                row.GetInt64(9),
                EvidenceStore.ParseTime(row.GetString(10)));
            merges.Add(new MergeHistoryEntry(merge, undos.GetValueOrDefault(merge.Sequence)));
        }

        return merges;
    }

    /// <summary>
    /// Gives a merge as the clusters count it: a proposal of <c>user v1</c> that merges, resting on the merge's
    /// transaction, with why in plain Swedish.
    /// </summary>
    /// <param name="merge">The merge.</param>
    /// <returns>The evidence; its sequence is the merge's, in the chain <c>clustering.merges</c>.</returns>
    public static ClusterEvidence AsEvidence(ClusterMerge merge) => new(
        merge.Sequence,
        UserMerges.Id,
        merge.Txid,
        merge.Addresses,
        EvidenceStrength.Merge,
        UserMerges.Explain(merge),
        null,
        null,
        merge.JournalSequence,
        merge.RecordedUtc);

    /// <summary>Reads the undoing of merges; the first record for a merge counts.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The undoing of each undone merge, by the merge's sequence.</returns>
    private static Dictionary<long, ClusterMergeUndo> ReadUndos(ICaseReader reader)
    {
        using var command = reader.CreateCommand(
            """
            SELECT sequence, merge_sequence, reason, journal_sequence, recorded_utc
            FROM clustering_merge_undos ORDER BY sequence
            """);
        using var row = command.ExecuteReader();
        var undos = new Dictionary<long, ClusterMergeUndo>();
        while (row.Read())
        {
            var undo = new ClusterMergeUndo(
                row.GetInt64(0), row.GetInt64(1), row.IsDBNull(2) ? null : row.GetString(2), row.GetInt64(3),
                EvidenceStore.ParseTime(row.GetString(4)));
            undos.TryAdd(undo.MergeSequence, undo);
        }

        return undos;
    }
}
