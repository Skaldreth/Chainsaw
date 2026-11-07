using System.Data.Common;
using System.Numerics;
using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring;

/// <summary>Reads and writes the watches, hits and their plain data in a case.</summary>
internal static class WatchStore
{
    /// <summary>Reads every record of the watches, oldest first, each with the watch as it left it.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The records.</returns>
    public static IReadOnlyList<WatchRecord> ReadRecords(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT sequence, action, watch_id, subject, filter, start_height, reason, set_by, recorded_utc
            FROM monitoring_watches ORDER BY sequence
            """);
        using var rows = command.ExecuteReader();
        var watches = new Dictionary<string, Watch>(StringComparer.Ordinal);
        var records = new List<WatchRecord>();
        while (rows.Read())
        {
            var action = Enum.Parse<WatchAction>(rows.GetString(1));
            var id = rows.GetString(2);
            var by = WatchCodec.DecodeOperator(rows.GetString(7));
            var utc = WatchCodec.Time(rows.GetString(8));
            var watch = action == WatchAction.Added
                ? new Watch(
                    id,
                    WatchCodec.DecodeSubject(rows.GetString(3)),
                    WatchCodec.DecodeFilter(rows.GetString(4)),
                    WatchState.Active,
                    null,
                    by,
                    utc,
                    utc)
                : Next(watches, id, action, rows, utc);
            if (watch is null)
            {
                continue;
            }

            watches[id] = watch;
            records.Add(new WatchRecord(
                rows.GetInt64(0), action, watch, rows.IsDBNull(6) ? null : rows.GetString(6), by, utc));
        }

        return records;
    }

    /// <summary>Reads the watches that are not taken back, oldest first.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The watches.</returns>
    public static IReadOnlyList<Watch> ReadWatches(ICaseReader reader)
    {
        var latest = new Dictionary<string, Watch>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var record in ReadRecords(reader))
        {
            if (!latest.ContainsKey(record.Watch.Id))
            {
                order.Add(record.Watch.Id);
            }

            latest[record.Watch.Id] = record.Watch;
        }

        return [.. order.Select(id => latest[id]).Where(watch => watch.State != WatchState.Removed)];
    }

    /// <summary>Seals a record of a watch.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="action">What it does.</param>
    /// <param name="watch">The watch as the record leaves it.</param>
    /// <param name="reason">Why, for a removal; otherwise <see langword="null"/>.</param>
    /// <param name="journal">The journal entry that records it.</param>
    /// <returns>The sealed record.</returns>
    public static SealedRecord Seal(
        ICaseTransaction transaction, WatchAction action, Watch watch, string? reason, JournalEntry journal) =>
        transaction.AppendSealed(
            MonitoringSchema.WatchesChain,
            [
                action.ToString(),
                watch.Id,
                action == WatchAction.Added ? WatchCodec.Encode(watch.Subject) : null,
                action is WatchAction.Added or WatchAction.Changed ? WatchCodec.Encode(watch.Filter) : null,
                action == WatchAction.Started ? watch.StartHeight : null,
                reason,
                WatchCodec.Encode(journal.Operator),
                journal.Sequence,
                WatchCodec.Text(journal.Utc),
            ]);

    /// <summary>Reads the hits, oldest first.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The hits.</returns>
    public static IReadOnlyList<WatchHit> ReadHits(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT sequence, watch_id, chain, txid, block_height, block_hash, block_time, direction, asset, amount,
                   counterparties, origins, journal_sequence, recorded_utc
            FROM monitoring_hits ORDER BY sequence
            """);
        using var rows = command.ExecuteReader();
        var hits = new List<WatchHit>();
        while (rows.Read())
        {
            var chain = Enum.Parse<Chain>(rows.GetString(2));
            var asset = WatchCodec.DecodeAsset(rows.GetString(8));
            var match = new WatchMatch(
                rows.GetString(3),
                new WatchBlock(rows.GetInt64(4), rows.GetString(5), WatchCodec.Time(rows.GetString(6))),
                Enum.Parse<WatchDirection>(rows.GetString(7)),
                new AssetAmount(asset, BigInteger.Parse(rows.GetString(9), CultureInfo.InvariantCulture)),
                WatchCodec.DecodeAddresses(chain, rows.GetString(10)));
            hits.Add(new WatchHit(
                rows.GetInt64(0),
                rows.GetString(1),
                chain,
                match,
                JsonSerializer.Deserialize<List<long>>(rows.GetString(11)) ?? [],
                rows.GetInt64(12),
                WatchCodec.Time(rows.GetString(13))));
        }

        return hits;
    }

    /// <summary>Seals a hit.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="watch">The watch.</param>
    /// <param name="match">What moved.</param>
    /// <param name="origins">The sealed raw responses it rests on.</param>
    /// <param name="journal">The journal entry of the check.</param>
    /// <returns>The sealed hit.</returns>
    public static WatchHit SealHit(
        ICaseTransaction transaction, Watch watch, WatchMatch match, IReadOnlyList<long> origins, JournalEntry journal)
    {
        var record = transaction.AppendSealed(
            MonitoringSchema.HitsChain,
            [
                watch.Id,
                watch.Chain.ToString(),
                match.Transaction,
                match.Block.Height,
                match.Block.Hash,
                WatchCodec.Text(match.Block.Time),
                match.Direction.ToString(),
                WatchCodec.Encode(match.Amount.Asset),
                WatchCodec.Units(match.Amount),
                WatchCodec.Encode(match.Counterparties),
                JsonSerializer.Serialize(origins),
                journal.Sequence,
                WatchCodec.Text(journal.Utc),
            ]);
        return new WatchHit(record.Sequence, watch.Id, watch.Chain, match, origins, journal.Sequence, journal.Utc);
    }

    /// <summary>Reads when each watch was last checked.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>Per watch.</returns>
    public static IReadOnlyDictionary<string, WatchChecked> ReadChecked(ICaseReader reader)
    {
        using var command = reader.CreateCommand(
            "SELECT watch_id, checked_utc, height, status, problem, tip FROM monitoring_checked");
        using var rows = command.ExecuteReader();
        var all = new Dictionary<string, WatchChecked>(StringComparer.Ordinal);
        while (rows.Read())
        {
            all[rows.GetString(0)] = new WatchChecked(
                WatchCodec.Time(rows.GetString(1)),
                rows.GetInt64(2),
                Enum.Parse<WatchFeedStatus>(rows.GetString(3)),
                rows.IsDBNull(4) ? null : rows.GetString(4))
            {
                Tip = rows.GetInt64(5),
            };
        }

        return all;
    }

    /// <summary>Notes when a watch was checked.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="watchId">The watch.</param>
    /// <param name="checkedAt">The check.</param>
    public static void WriteChecked(ICaseTransaction transaction, string watchId, WatchChecked checkedAt)
    {
        using var command = transaction.CreateCommand("""
            INSERT INTO monitoring_checked (watch_id, checked_utc, height, tip, status, problem)
            VALUES ($id, $utc, $height, $tip, $status, $problem)
            ON CONFLICT (watch_id) DO UPDATE SET checked_utc = $utc, height = $height, tip = $tip, status = $status,
                problem = $problem
            """);
        Add(command, "$id", watchId);
        Add(command, "$utc", WatchCodec.Text(checkedAt.CheckedUtc));
        Add(command, "$height", checkedAt.Height);
        Add(command, "$tip", checkedAt.Tip);
        Add(command, "$status", checkedAt.Status.ToString());
        Add(command, "$problem", checkedAt.Problem);
        command.ExecuteNonQuery();
    }

    /// <summary>Reads, per watch, the last hit seen.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>Per watch.</returns>
    public static IReadOnlyDictionary<string, long> ReadSeen(ICaseReader reader)
    {
        using var command = reader.CreateCommand("SELECT watch_id, through_sequence FROM monitoring_seen");
        using var rows = command.ExecuteReader();
        var all = new Dictionary<string, long>(StringComparer.Ordinal);
        while (rows.Read())
        {
            all[rows.GetString(0)] = rows.GetInt64(1);
        }

        return all;
    }

    /// <summary>Notes the last hit seen of a watch; an older one never moves it back.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="watchId">The watch.</param>
    /// <param name="through">The hit.</param>
    public static void WriteSeen(ICaseTransaction transaction, string watchId, long through)
    {
        using var command = transaction.CreateCommand("""
            INSERT INTO monitoring_seen (watch_id, through_sequence) VALUES ($id, $through)
            ON CONFLICT (watch_id) DO UPDATE SET through_sequence = MAX(through_sequence, $through)
            """);
        Add(command, "$id", watchId);
        Add(command, "$through", through);
        command.ExecuteNonQuery();
    }

    /// <summary>Gives the watch as a record other than the first leaves it.</summary>
    /// <param name="watches">The watches so far.</param>
    /// <param name="id">The watch.</param>
    /// <param name="action">What the record does.</param>
    /// <param name="rows">The row.</param>
    /// <param name="utc">When.</param>
    /// <returns>
    /// The watch, or <see langword="null"/> for a record of a watch never made (not written by Chainsaw).
    /// </returns>
    private static Watch? Next(
        Dictionary<string, Watch> watches, string id, WatchAction action, DbDataReader rows, DateTimeOffset utc)
    {
        if (!watches.TryGetValue(id, out var watch))
        {
            return null;
        }

        var changed = watch with { ChangedUtc = utc };
        return action switch
        {
            WatchAction.Changed => changed with { Filter = WatchCodec.DecodeFilter(rows.GetString(4)) },
            WatchAction.Started => changed with { StartHeight = rows.GetInt64(5) },
            WatchAction.Paused => changed with { State = WatchState.Paused },
            WatchAction.Resumed => changed with { State = WatchState.Active },
            WatchAction.Removed => changed with { State = WatchState.Removed },
            _ => throw new InvalidDataException("A watch's record has an unknown action."),
        };
    }

    /// <summary>Adds a parameter to a command.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter's name.</param>
    /// <param name="value">The value; <see langword="null"/> is stored as NULL.</param>
    private static void Add(DbCommand command, string name, object? value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        command.Parameters.Add(parameter);
    }
}
