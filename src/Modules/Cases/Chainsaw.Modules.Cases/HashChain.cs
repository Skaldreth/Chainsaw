using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
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

    /// <summary>The Cases module's registry of sealed chains.</summary>
    public const string RegistryTable = "sealed_chain";

    /// <summary>What <c>PRAGMA quick_check</c> answers, alone, for a sound database.</summary>
    private const string QuickCheckSound = "ok";

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

    /// <summary>
    /// Creates the protecting triggers and registers the chain's definition, as the Cases module, in the transaction
    /// that created the table. The registry lets a build that does not know the module still guard and verify it.
    /// </summary>
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

        using var register = database.Command(
            "INSERT INTO sealed_chain (name, table_name, columns) VALUES ($name, $table, $columns)", transaction);
        register.Parameters.AddWithValue("$name", chain.Name);
        register.Parameters.AddWithValue("$table", chain.Table);
        register.Parameters.AddWithValue("$columns", JsonSerializer.Serialize(chain.ContentColumns));
        register.ExecuteNonQuery();
    }

    /// <summary>Reads the registered chains; a definition that does not hold is given by name only.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The current transaction, if any.</param>
    /// <returns>The valid definitions and the names of those that are not.</returns>
    public static (IReadOnlyList<SealedChain> Chains, IReadOnlyList<string> Invalid) ReadRegistry(
        CaseDatabase database, SqliteTransaction? transaction)
    {
        var chains = new List<SealedChain>();
        var invalid = new List<string>();
        using var select = database.Command("SELECT name, table_name, columns FROM sealed_chain", transaction);
        using var reader = select.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetString(0);
            try
            {
                var columns = JsonSerializer.Deserialize<List<string>>(reader.GetString(2)) ?? [];
                var chain = new SealedChain(name, reader.GetString(1), columns);
                SchemaCatalog.Validate(chain);
                chains.Add(chain);
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                invalid.Add(name);
            }
        }

        return (chains, invalid);
    }

    /// <summary>Reads the names of every chain that has a head.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The names.</returns>
    public static IReadOnlyList<string> ReadHeadNames(CaseDatabase database, SqliteTransaction transaction)
    {
        var names = new List<string>();
        using var select = database.Command("SELECT chain FROM chain_head", transaction);
        using var reader = select.ExecuteReader();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    /// <summary>Reads one record's stored hash.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="sequence">The record's sequence number.</param>
    /// <returns>The hash, or <see langword="null"/> when there is no such record.</returns>
    public static byte[]? ReadRecordHash(
        CaseDatabase database, SqliteTransaction transaction, SealedChain chain, long sequence)
    {
        using var select = database.Command(
            $"SELECT {HashColumn} FROM {chain.Table} WHERE {SequenceColumn} = $sequence", transaction);
        select.Parameters.AddWithValue("$sequence", sequence);
        return select.ExecuteScalar() as byte[];
    }

    /// <summary>Reads a chain's stored head.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="chain">The chain's name.</param>
    /// <returns>The head's sequence number and hash; 0 and <see langword="null"/> for an empty chain.</returns>
    public static (long Sequence, byte[]? Hash) ReadHead(
        CaseDatabase database, SqliteTransaction transaction, string chain)
    {
        using var command = database.Command("SELECT sequence, hash FROM chain_head WHERE chain = $chain", transaction);
        command.Parameters.AddWithValue("$chain", chain);
        using var reader = command.ExecuteReader();
        return reader.Read() ? (reader.GetInt64(0), reader.GetValue(1) as byte[]) : (0, null);
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
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) > 0;
    }

    /// <summary>
    /// Reads the declared types of a chain's content columns and checks the table's shape: STRICT, with exactly
    /// <c>sequence INTEGER PRIMARY KEY</c>, <c>hash BLOB NOT NULL</c> and the content columns.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The current transaction, if any.</param>
    /// <param name="chain">The chain.</param>
    /// <returns>
    /// The types in the order of the content columns, or <see langword="null"/> when the shape is wrong.
    /// </returns>
    public static IReadOnlyList<string>? ReadShape(
        CaseDatabase database, SqliteTransaction? transaction, SealedChain chain)
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

        // Indexes and foreign keys on a sealed table would let constraints refuse records or hold other tables.
        if (Count(database, transaction, "SELECT count(*) FROM pragma_index_list($table)", chain.Table) != 0
            || Count(database, transaction, "SELECT count(*) FROM pragma_foreign_key_list($table)", chain.Table) != 0)
        {
            return null;
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

    /// <summary>Runs a count with a table name as its parameter.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The current transaction, if any.</param>
    /// <param name="sql">The SQL, with <c>$table</c>.</param>
    /// <param name="table">The table.</param>
    /// <returns>The count.</returns>
    private static long Count(CaseDatabase database, SqliteTransaction? transaction, string sql, string table)
    {
        using var command = database.Command(sql, transaction);
        command.Parameters.AddWithValue("$table", table);
        return Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    /// <summary>Adds a record, seals it and moves the chain's head, in the caller's transaction.</summary>
    /// <param name="database">The database, in <see cref="GuardMode.Sealing"/> for the chain's table.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="normalized">The values from <see cref="Normalize"/>, one per content column.</param>
    /// <returns>The sealed record.</returns>
    public static SealedRecord Append(
        CaseDatabase database, SqliteTransaction transaction, SealedChain chain, object?[] normalized)
    {
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
        return reader.Read() && reader.GetString(0) == QuickCheckSound && !reader.Read();
    }

    /// <summary>Walks a chain from its first record and says where it first breaks.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">A read transaction.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="registered">
    /// The chain's registered definition, if any; it must be the same as <paramref name="chain"/>.
    /// </param>
    /// <returns>The result for the chain.</returns>
    public static ChainVerification Verify(
        CaseDatabase database, SqliteTransaction transaction, SealedChain chain, SealedChain? registered)
    {
        if (registered is null || registered.Table != chain.Table
            || !registered.ContentColumns.SequenceEqual(chain.ContentColumns)
            || !TableExists(database, transaction, chain.Table) || ReadShape(database, transaction, chain) is null)
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
    private static ChainBreak? CheckRecord(
        SqliteDataReader reader, SealedChain chain, long lastGood, ref byte[]? lastHash)
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

    /// <summary>Checks the values against the columns' declared types and gives them in the sealed types.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="types">The declared types.</param>
    /// <param name="values">The values.</param>
    /// <returns>The values, with <see cref="int"/> widened to <see cref="long"/>.</returns>
    /// <exception cref="ArgumentException">A value is missing or has the wrong type.</exception>
    public static object?[] Normalize(SealedChain chain, IReadOnlyList<string> types, IReadOnlyList<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        if (values.Count != chain.ContentColumns.Count)
        {
            throw new ArgumentException(
                $"The chain {chain.Name} takes {chain.ContentColumns.Count} values.", nameof(values));
        }

        var normalized = new object?[values.Count];
        for (var index = 0; index < values.Count; index++)
        {
            var value = values[index] switch
            {
                int small => (long)small,

                // Copied, so that the bytes sealed are the bytes stored whatever the caller does with its array.
                byte[] bytes => bytes.ToArray(),
                double real when !double.IsFinite(real) =>
                    throw new ArgumentException("Only finite numbers can be sealed.", nameof(values)),

                // SQLite stores -0.0 as the integer 0 and reads it back as +0.0, so it is sealed as +0.0.
                double real when real == 0 => 0.0,
                var other => other,
            };
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
                    $"The value for {chain.ContentColumns[index]} does not match its type {types[index]}.",
                    nameof(values));
            }

            normalized[index] = value;
        }

        return normalized;
    }
}
