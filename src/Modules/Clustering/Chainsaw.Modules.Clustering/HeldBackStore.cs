using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Keeps what the latest run of the analyses held back, and seals the user's decisions about it (sketch HB1a). What a
/// run held back is working state: it is replaced by the next run that holds back something else, and can always be
/// worked out again from the sealed conclusions. The decisions are sealed and never change.
/// </summary>
internal static class HeldBackStore
{
    /// <summary>
    /// Replaces what the latest run held back, when it differs from what is kept, so that a run that holds back the
    /// same as the one before writes nothing.
    /// </summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="heldBack">The transactions held back, each with the transactions that were missing.</param>
    public static void Replace(
        ICaseTransaction transaction, IReadOnlyList<(Txid Txid, IReadOnlyList<Txid> Missing)> heldBack)
    {
        // Kept in one order, so that a run that only gives the same in another order writes nothing.
        heldBack = Normal(heldBack);
        var kept = Normal(Read(transaction));
        if (kept.Count == heldBack.Count && kept.Zip(heldBack).All(pair =>
                pair.First.Txid == pair.Second.Txid && pair.First.Missing.SequenceEqual(pair.Second.Missing)))
        {
            return;
        }

        using (var clear = transaction.CreateCommand("DELETE FROM clustering_held_back"))
        {
            clear.ExecuteNonQuery();
        }

        foreach (var (txid, missing) in heldBack)
        {
            using var insert = transaction.CreateCommand(
                "INSERT INTO clustering_held_back (txid, missing) VALUES ($txid, $missing)");
            insert.AddParameter("$txid", txid.Value).AddParameter("$missing", Serialize(missing));
            insert.ExecuteNonQuery();
        }
    }

    /// <summary>Puts what is held back in one order: by transaction, each with its missing transactions sorted.</summary>
    /// <param name="heldBack">What is held back.</param>
    /// <returns>The same, in order.</returns>
    private static List<(Txid Txid, IReadOnlyList<Txid> Missing)> Normal(
        IReadOnlyList<(Txid Txid, IReadOnlyList<Txid> Missing)> heldBack) =>
    [
        .. heldBack.OrderBy(entry => entry.Txid.Value, StringComparer.Ordinal).Select(entry =>
            (entry.Txid, (IReadOnlyList<Txid>)[.. entry.Missing.OrderBy(txid => txid.Value, StringComparer.Ordinal)])),
    ];

    /// <summary>Reads what the latest run held back, in transaction order.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The transactions held back, each with the transactions that were missing.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when a row cannot be read back.</exception>
    public static List<(Txid Txid, IReadOnlyList<Txid> Missing)> Read(ICaseReader reader)
    {
        using var command = reader.CreateCommand("SELECT txid, missing FROM clustering_held_back ORDER BY position");
        using var row = command.ExecuteReader();
        var all = new List<(Txid, IReadOnlyList<Txid>)>();
        while (row.Read())
        {
            all.Add((EvidenceStore.ParseTxid(row.GetString(0)), Deserialize(row.GetString(1))));
        }

        return all;
    }

    /// <summary>Seals a decision about a held back conclusion, under its journal entry.</summary>
    /// <param name="transaction">The case's write transaction.</param>
    /// <param name="txid">The transaction.</param>
    /// <param name="missing">The transactions that were missing.</param>
    /// <param name="addresses">The addresses the held back conclusions merged.</param>
    /// <param name="choice">What was decided.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <param name="journal">The journal entry.</param>
    /// <param name="undoes">For a taking back, the decision taken back.</param>
    /// <returns>The stored decision.</returns>
    public static HeldBackDecision AppendDecision(
        ICaseTransaction transaction,
        Txid txid,
        IReadOnlyList<Txid> missing,
        IReadOnlyList<BitcoinAddress> addresses,
        ConclusionChoice choice,
        string? reason,
        JournalEntry journal,
        long? undoes = null)
    {
        object?[] values =
        [
            txid.Value, Serialize(missing), JsonSerializer.Serialize(addresses.Select(address => address.Value)),
            choice.ToString(), reason, undoes, journal.Sequence, journal.Utc.ToString("O", CultureInfo.InvariantCulture),
        ];
        var record = transaction.AppendSealed(ClusteringSchema.HeldBackDecisionsChain, values);
        return new HeldBackDecision(
            record.Sequence, txid, missing, addresses, choice, reason, journal.Sequence, journal.Utc, undoes);
    }

    /// <summary>Reads every decision, oldest first.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The decisions.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when a record cannot be read back.</exception>
    public static List<HeldBackDecision> ReadDecisions(ICaseReader reader)
    {
        using var command = reader.CreateCommand(
            "SELECT sequence, txid, missing, addresses, choice, reason, undoes, journal_sequence, recorded_utc "
                + "FROM clustering_held_back_decisions ORDER BY sequence");
        using var row = command.ExecuteReader();
        var all = new List<HeldBackDecision>();
        while (row.Read())
        {
            var choice = Enum.TryParse<ConclusionChoice>(row.GetString(4), out var parsed)
                ? parsed
                : throw new CaseException(CaseError.Corrupt);
            all.Add(new HeldBackDecision(
                row.GetInt64(0),
                EvidenceStore.ParseTxid(row.GetString(1)),
                Deserialize(row.GetString(2)),
                [.. EvidenceStore.Deserialize<List<string>>(row.GetString(3)).Select(EvidenceStore.ParseAddress)],
                choice,
                row.IsDBNull(5) ? null : row.GetString(5),
                row.GetInt64(7),
                DateTimeOffset.Parse(row.GetString(8), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                row.IsDBNull(6) ? null : row.GetInt64(6)));
        }

        return all;
    }

    /// <summary>
    /// Gives, per transaction whose latest decision lets its conclusions go, the journal entry of that release: the
    /// conclusions stored before it stop counting. A release taken back no longer counts.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The journal entry of each release in force.</returns>
    public static Dictionary<Txid, long> Releases(ICaseReader reader) =>
        Latest(ReadDecisions(reader))
            .Where(decision => decision.Choice == ConclusionChoice.Release)
            .ToDictionary(decision => decision.Txid, decision => decision.JournalSequence);

    /// <summary>Gives each transaction's latest decision.</summary>
    /// <param name="decisions">Every decision, oldest first.</param>
    /// <returns>The latest per transaction.</returns>
    public static IEnumerable<HeldBackDecision> Latest(IEnumerable<HeldBackDecision> decisions) =>
        decisions.GroupBy(decision => decision.Txid).Select(group => group.MaxBy(decision => decision.Sequence)!);

    /// <summary>Writes transaction ids as a JSON array.</summary>
    /// <param name="txids">The transactions.</param>
    /// <returns>The JSON.</returns>
    private static string Serialize(IReadOnlyList<Txid> txids) =>
        JsonSerializer.Serialize(txids.Select(txid => txid.Value));

    /// <summary>Reads transaction ids from a JSON array.</summary>
    /// <param name="json">The JSON.</param>
    /// <returns>The transactions.</returns>
    private static IReadOnlyList<Txid> Deserialize(string json) =>
        [.. EvidenceStore.Deserialize<List<string>>(json).Select(EvidenceStore.ParseTxid)];
}
