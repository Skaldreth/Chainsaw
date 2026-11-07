using System.Security.Cryptography;
using Chainsaw.Modules.Cases.Contracts;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// Sealed tables: the triggers that refuse UPDATE and DELETE, adding a record with its hash and the chain's head in
/// the same transaction, and walking a chain to find where it breaks.
/// </summary>
/// <remarks>
/// The chain's head (last sequence number and hash) is also kept in <c>chain_head</c>, so that records removed from
/// the end are noticed. The chain proves that the records are consistent with each other; someone with the file key
/// could rewrite a whole chain, so the head hash that <see cref="Verify"/> gives is what to write down outside the
/// case (for example in a report) to anchor it.
/// </remarks>
internal static class HashChain
{
    /// <summary>The column with a record's sequence number.</summary>
    public const string SequenceColumn = "sequence";

    /// <summary>The column with a record's hash.</summary>
    public const string HashColumn = "hash";

    /// <summary>The column types a content column may have in a STRICT table.</summary>
    private static readonly string[] AllowedTypes = ["INTEGER", "REAL", "TEXT", "BLOB", "ANY"];

    /// <summary>Gives the SQL of the triggers that protect a sealed table, exactly as they are stored.</summary>
    /// <param name="table">The table.</param>
    /// <returns>The trigger names and their SQL.</returns>
    public static IReadOnlyList<(string Name, string Sql)> Triggers(string table)
    {
        var noUpdate = $"{SqlGuard.SealTriggerPrefix}{table}_no_update";
        var noDelete = $"{SqlGuard.SealTriggerPrefix}{table}_no_delete";
        return
        [
            (noUpdate, $"CREATE TRIGGER {noUpdate} BEFORE UPDATE ON {table} "
                + "BEGIN SELECT RAISE(ABORT, 'sealed records cannot be changed'); END"),
            (noDelete, $"CREATE TRIGGER {noDelete} BEFORE DELETE ON {table} "
                + "BEGIN SELECT RAISE(ABORT, 'sealed records cannot be removed'); END"),
        ];
    }

    /// <summary>Creates the protecting triggers, as the Cases module, in the transaction that created the table.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="chain">The chain.</param>
    public static void InstallTriggers(CaseDatabase database, SqliteTransaction transaction, SealedChain chain)
    {
        foreach (var (_, sql) in Triggers(chain.Table))
        {
            using var command = database.Command(sql, transaction);
            command.ExecuteNonQuery();
        }
    }

    /// <summary>Checks whether a table exists in the case.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The current transaction.</param>
    /// <param name="table">The table.</param>
    /// <returns><see langword="true"/> when it exists.</returns>
    public static bool TableExists(CaseDatabase database, SqliteTransaction? transaction, string table)
    {
        using var command = database.Command(
            "SELECT count(*) FROM sqlite_schema WHERE type = 'table' AND name = $table", transaction);
        command.Parameters.AddWithValue("$table", table);
        return Convert.ToInt64(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>
    /// Reads the declared types of a chain's content columns and checks the table's shape: STRICT, with exactly
    /// <c>sequence INTEGER PRIMARY KEY</c>, <c>hash BLOB NOT NULL</c> and the content columns.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The current transaction, if any.</param>
    /// <param name="chain">The chain.</param>
    /// <returns>The types in the order of the content columns, or <see langword="null"/> when the shape is wrong.</returns>
    public static IReadOnlyList<string>? ReadShape(CaseDatabase database, SqliteTransaction? transaction, SealedChain chain)
    {
        using (var strict = database.Command(
            "SELECT strict FROM pragma_table_list($table) WHERE schema = 'main' AND type = 'table'", transaction))
        {
            strict.Parameters.AddWithValue("$table", chain.Table);
            if (strict.ExecuteScalar() is not long isStrict || isStrict != 1)
            {
                return null;
            }
        }

        var columns = new Dictionary<string, (string Type, bool NotNull, long Key)>(StringComparer.OrdinalIgnoreCase);
        using (var info = database.Command(
            "SELECT name, upper(type), \"notnull\", pk, hidden FROM pragma_table_xinfo($table)", transaction))
        {
            info.Parameters.AddWithValue("$table", chain.Table);
            using var reader = info.ExecuteReader();
            while (reader.Read())
            {
                if (reader.GetInt64(4) != 0)
                {
                    return null;
                }

                columns[reader.GetString(0)] = (reader.GetString(1), reader.GetInt64(2) == 1, reader.GetInt64(3));
            }
        }

        var shapeHolds = columns.Count == chain.ContentColumns.Count + 2
            && columns.TryGetValue(SequenceColumn, out var sequence) && sequence is ("INTEGER", _, 1)
            && columns.TryGetValue(HashColumn, out var hash) && hash is ("BLOB", true, 0)
            && chain.ContentColumns.All(column =>
                columns.TryGetValue(column, out var content) && content.Key == 0
                && AllowedTypes.Contains(content.Type));
        return shapeHolds ? [.. chain.ContentColumns.Select(column => columns[column].Type)] : null;
    }

    /// <summary>Adds a record, seals it and moves the chain's head, in the caller's transaction.</summary>
    /// <param name="database">The database, in <see cref="GuardMode.Internal"/>.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="types">The declared types of the content columns (from <see cref="ReadShape"/>).</param>
    /// <param name="values">The values, one per content column.</param>
    /// <returns>The sealed record.</returns>
    /// <exception cref="ArgumentException">The values do not match the columns.</exception>
    public static SealedRecord Append(
        CaseDatabase database,
        SqliteTransaction transaction,
        SealedChain chain,
        IReadOnlyList<string> types,
        IReadOnlyList<object?> values)
    {
        var normalized = Normalize(chain, types, values);
        var (headSequence, previous) = ReadHead(database, transaction, chain.Name);
        var sequence = headSequence + 1;
        var hash = SealCodec.Hash(previous ?? SealCodec.Genesis, chain.Name, sequence, normalized);

        var names = string.Join(", ", chain.ContentColumns);
        var parameters = string.Join(", ", chain.ContentColumns.Select((_, index) => $"$v{index}"));
        using (var insert = database.Command(
            $"INSERT INTO {chain.Table} ({SequenceColumn}, {names}, {HashColumn}) "
            + $"VALUES ($sequence, {parameters}, $hash)",
            transaction))
        {
            insert.Parameters.AddWithValue("$sequence", sequence);
            for (var index = 0; index < normalized.Length; index++)
            {
                insert.Parameters.AddWithValue($"$v{index}", normalized[index] ?? DBNull.Value);
            }

            insert.Parameters.AddWithValue("$hash", hash);
            insert.ExecuteNonQuery();
        }

        using (var head = database.Command(
            "INSERT INTO chain_head (chain, sequence, hash) VALUES ($chain, $sequence, $hash) "
            + "ON CONFLICT (chain) DO UPDATE SET sequence = excluded.sequence, hash = excluded.hash",
            transaction))
        {
            head.Parameters.AddWithValue("$chain", chain.Name);
            head.Parameters.AddWithValue("$sequence", sequence);
            head.Parameters.AddWithValue("$hash", hash);
            head.ExecuteNonQuery();
        }

        return new SealedRecord(chain.Name, sequence, Convert.ToHexStringLower(hash));
    }

    /// <summary>Reads and authenticates every page of the database and checks its structure.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">A read transaction.</param>
    /// <returns><see langword="true"/> when SQLite reports the database sound.</returns>
    /// <exception cref="SqliteException">A page could not be read or authenticated.</exception>
    public static bool QuickCheck(CaseDatabase database, SqliteTransaction transaction)
    {
        using var command = database.Command("PRAGMA quick_check", transaction);
        using var reader = command.ExecuteReader();
        return reader.Read() && reader.GetString(0) == "ok" && !reader.Read();
    }

    /// <summary>Walks a chain from its first record and says where it first breaks.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">A read transaction.</param>
    /// <param name="chain">The chain.</param>
    /// <returns>The result for the chain.</returns>
    public static ChainVerification Verify(CaseDatabase database, SqliteTransaction transaction, SealedChain chain)
    {
        if (!TableExists(database, transaction, chain.Table) || ReadShape(database, transaction, chain) is null)
        {
            return new ChainVerification(chain.Name, 0, 0, null, new ChainBreak(0, ChainBreakReason.TableChanged));
        }

        var count = 0L;
        var lastGood = 0L;
        byte[]? lastHash = null;
        ChainBreak? broken = null;
        using (var select = database.Command(
            $"SELECT {SequenceColumn}, {string.Join(", ", chain.ContentColumns)}, {HashColumn} "
            + $"FROM {chain.Table} ORDER BY {SequenceColumn}",
            transaction))
        using (var reader = select.ExecuteReader())
        {
            while (reader.Read())
            {
                count++;
                broken ??= CheckRecord(reader, chain, lastGood, ref lastHash);
                if (broken is null)
                {
                    lastGood++;
                }
            }
        }

        broken ??= CheckHead(database, transaction, chain.Name, lastGood, lastHash);
        broken ??= CheckProtection(database, transaction, chain.Table);
        return new ChainVerification(
            chain.Name, count, lastGood, lastHash is null ? null : Convert.ToHexStringLower(lastHash), broken);
    }

    /// <summary>Checks one record against the record before it.</summary>
    /// <param name="reader">The reader, at the record.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="lastGood">The sequence number of the last unbroken record.</param>
    /// <param name="lastHash">The last unbroken record's hash; replaced by this record's when it holds.</param>
    /// <returns>The break, or <see langword="null"/> when the record holds.</returns>
    private static ChainBreak? CheckRecord(SqliteDataReader reader, SealedChain chain, long lastGood, ref byte[]? lastHash)
    {
        var expected = lastGood + 1;
        var sequence = reader.GetInt64(0);
        if (sequence != expected)
        {
            return new ChainBreak(expected, ChainBreakReason.SequenceGap);
        }

        var values = new object?[chain.ContentColumns.Count];
        for (var index = 0; index < values.Length; index++)
        {
            values[index] = reader.GetValue(index + 1);
        }

        var stored = reader.GetValue(values.Length + 1) as byte[];
        var computed = SealCodec.Hash(lastHash ?? SealCodec.Genesis, chain.Name, sequence, values);
        if (stored is null || !CryptographicOperations.FixedTimeEquals(stored, computed))
        {
            return new ChainBreak(sequence, ChainBreakReason.RecordChanged);
        }

        lastHash = stored;
        return null;
    }

    /// <summary>Checks that the stored head is the last record.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="chain">The chain's name.</param>
    /// <param name="lastSequence">The last record's sequence number.</param>
    /// <param name="lastHash">The last record's hash.</param>
    /// <returns>The break, or <see langword="null"/> when the head matches.</returns>
    private static ChainBreak? CheckHead(
        CaseDatabase database, SqliteTransaction transaction, string chain, long lastSequence, byte[]? lastHash)
    {
        var (headSequence, headHash) = ReadHead(database, transaction, chain);
        var matches = headSequence == lastSequence
            && (headHash is null ? lastHash is null : lastHash is not null && headHash.SequenceEqual(lastHash));
        return matches ? null : new ChainBreak(Math.Max(headSequence, lastSequence), ChainBreakReason.HeadMismatch);
    }

    /// <summary>Checks that the table has exactly its two protecting triggers, unchanged.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="table">The table.</param>
    /// <returns>The break, or <see langword="null"/> when the protection is intact.</returns>
    private static ChainBreak? CheckProtection(CaseDatabase database, SqliteTransaction transaction, string table)
    {
        var expected = Triggers(table).ToDictionary(trigger => trigger.Name, trigger => trigger.Sql);
        var found = new Dictionary<string, string>(StringComparer.Ordinal);
        using var command = database.Command(
            "SELECT name, sql FROM sqlite_schema WHERE type = 'trigger' AND tbl_name = $table COLLATE NOCASE",
            transaction);
        command.Parameters.AddWithValue("$table", table);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            found[reader.GetString(0)] = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }

        var intact = found.Count == expected.Count
            && expected.All(trigger => found.TryGetValue(trigger.Key, out var sql) && sql == trigger.Value);
        return intact ? null : new ChainBreak(0, ChainBreakReason.ProtectionMissing);
    }

    /// <summary>Reads a chain's stored head.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="chain">The chain's name.</param>
    /// <returns>The head's sequence number and hash; 0 and <see langword="null"/> for an empty chain.</returns>
    private static (long Sequence, byte[]? Hash) ReadHead(CaseDatabase database, SqliteTransaction transaction, string chain)
    {
        using var command = database.Command("SELECT sequence, hash FROM chain_head WHERE chain = $chain", transaction);
        command.Parameters.AddWithValue("$chain", chain);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt64(0), reader.GetValue(1) as byte[]) : (0, null);
    }

    /// <summary>Checks the values against the columns' declared types and gives them in the sealed types.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="types">The declared types.</param>
    /// <param name="values">The values.</param>
    /// <returns>The values, with <see cref="int"/> widened to <see cref="long"/>.</returns>
    /// <exception cref="ArgumentException">A value is missing or has the wrong type.</exception>
    private static object?[] Normalize(SealedChain chain, IReadOnlyList<string> types, IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != chain.ContentColumns.Count)
        {
            throw new ArgumentException($"The chain {chain.Name} takes {chain.ContentColumns.Count} values.", nameof(values));
        }

        var normalized = new object?[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index] is int small ? (long)small : values[index];
            var fits = value switch
            {
                null => true,
                long => types[index] is "INTEGER" or "ANY",
                double => types[index] is "REAL" or "ANY",
                string => types[index] is "TEXT" or "ANY",
                byte[] => types[index] is "BLOB" or "ANY",
                _ => false,
            };
            if (!fits)
            {
                throw new ArgumentException(
                    $"The value for {chain.ContentColumns[index]} does not match its type {types[index]}.", nameof(values));
            }

            normalized[index] = value;
        }

        return normalized;
    }
}
