using System.Security.Cryptography;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The checks behind <c>/verify</c> once the database is known to be sound: the case information, every chain and
/// this computer's anchors.
/// </summary>
internal static class CaseChecks
{
    /// <summary>Runs the checks.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">A read transaction.</param>
    /// <param name="info">The case information read when the case was opened.</param>
    /// <param name="chains">The chains in the case.</param>
    /// <param name="registered">The registered definitions by name.</param>
    /// <param name="invalidRegistrations">The names of registered definitions that do not hold.</param>
    /// <param name="anchors">This computer's anchors, or <see langword="null"/> when they are unreadable.</param>
    /// <returns>The problems with the case as a whole, and one result per chain, ordered by name.</returns>
    public static (IReadOnlyList<CaseProblem> Problems, IReadOnlyList<ChainVerification> Chains) Run(
        CaseDatabase database,
        SqliteTransaction transaction,
        CaseInfo info,
        IReadOnlyList<SealedChain> chains,
        IReadOnlyDictionary<string, SealedChain> registered,
        IReadOnlyList<string> invalidRegistrations,
        IReadOnlyList<ChainAnchor>? anchors)
    {
        var problems = new List<CaseProblem>();
        if (!CaseInfoHolds(database, transaction, info))
        {
            problems.Add(CaseProblem.CaseInfoChanged);
        }

        if (anchors is null)
        {
            problems.Add(CaseProblem.AnchorsUnreadable);
        }

        if (HasReplacedAnchors(database, transaction))
        {
            problems.Add(CaseProblem.AnchorsReplaced);
        }

        if (HasForeignTrigger(database, transaction))
        {
            problems.Add(CaseProblem.TriggerAdded);
        }

        var results = new List<ChainVerification>();
        foreach (var chain in chains)
        {
            var result = HashChain.Verify(database, transaction, chain, registered.GetValueOrDefault(chain.Name));
            if (result.Break is null && !BodiesMatchTheirHash(database, transaction, chain)
                && !problems.Contains(CaseProblem.BodyHashMismatch))
            {
                problems.Add(CaseProblem.BodyHashMismatch);
            }

            var anchor = anchors?.FirstOrDefault(item => item.Chain == chain.Name);
            if (result.Break is null && anchor is not null && !Reaches(database, transaction, chain, anchor))
            {
                result = result with { Break = new ChainBreak(anchor.Sequence, ChainBreakReason.RolledBack) };
            }

            results.Add(result);
        }

        var known = chains.Select(chain => chain.Name).ToHashSet(StringComparer.Ordinal);
        var strays = HashChain.ReadHeadNames(database, transaction).Concat(invalidRegistrations)
            .Where(name => !known.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .Select(name => new ChainVerification(name, 0, 0, null, new ChainBreak(0, ChainBreakReason.Unregistered)));
        var lost = (anchors ?? [])
            .Where(anchor => !known.Contains(anchor.Chain))
            .Select(anchor => new ChainVerification(
                anchor.Chain, 0, 0, null, new ChainBreak(anchor.Sequence, ChainBreakReason.RolledBack)));
        results.AddRange(strays.Concat(lost).DistinctBy(result => result.Chain));
        return (problems, [.. results.OrderBy(result => result.Chain, StringComparer.Ordinal)]);
    }

    /// <summary>Checks whether a chain still has the anchored record with the anchored hash.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="anchor">The anchor.</param>
    /// <returns><see langword="true"/> when the chain reaches its anchor.</returns>
    public static bool Reaches(
        CaseDatabase database, SqliteTransaction transaction, SealedChain chain, ChainAnchor anchor)
    {
        if (!HashChain.TableExists(database, transaction, chain.Table))
        {
            return false;
        }

        var hash = HashChain.ReadRecordHash(database, transaction, chain, anchor.Sequence);
        return hash is not null && CryptographicOperations.FixedTimeEquals(hash, anchor.Hash);
    }

    /// <summary>
    /// Checks that every record of a chain with a <c>sha256</c> and a <c>body</c> column (the sealed raw responses)
    /// holds the SHA-256 of its body. The rows are read one at a time, so a large case is not held in memory.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="chain">The chain, whose table is known to hold its columns.</param>
    /// <returns><see langword="true"/> when every body matches, or the chain has no such columns.</returns>
    private static bool BodiesMatchTheirHash(CaseDatabase database, SqliteTransaction transaction, SealedChain chain)
    {
        if (!chain.ContentColumns.Contains("sha256") || !chain.ContentColumns.Contains("body"))
        {
            return true;
        }

        using var command = database.Command($"SELECT sha256, body FROM {chain.Table}", transaction);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.GetValue(0) is not byte[] stored || reader.GetValue(1) is not byte[] body
                || !SHA256.HashData(body).AsSpan().SequenceEqual(stored))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Checks the case information: it is what the case was opened with, its triggers are in place and the journal
    /// begins with the entry that created this case.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="info">The case information read when the case was opened.</param>
    /// <returns><see langword="true"/> when it holds.</returns>
    private static bool CaseInfoHolds(CaseDatabase database, SqliteTransaction transaction, CaseInfo info)
    {
        if (!CasesSchema.ProtectingTriggers.All(trigger => HasTrigger(database, transaction, trigger)))
        {
            return false;
        }

        string createdUtc;
        using (var row = database.Command("SELECT case_id, case_number, created_utc FROM case_info", transaction))
        using (var reader = row.ExecuteReader())
        {
            if (!reader.Read() || reader.GetValue(0) as string != info.Id.ToString("D")
                || reader.GetValue(1) as string != info.CaseNumber || reader.GetValue(2) is not string created)
            {
                return false;
            }

            createdUtc = created;
            if (reader.Read())
            {
                return false;
            }
        }

        // The first entry is read as raw values, so that a record changed into something unreadable (a time that
        // does not parse) is reported as a change rather than failing the verification.
        (string Id, string Number) identity;
        using (var first = database.Command("SELECT kind, details FROM journal WHERE sequence = 1", transaction))
        using (var entry = first.ExecuteReader())
        {
            if (!entry.Read() || entry.GetValue(0) as string != CaseJournalKinds.Created
                || CreatedIdentity(entry.GetValue(1) as string, createdUtc) is not { } created)
            {
                return false;
            }

            identity = created;
        }

        // A working copy saved as a case of its own got a new id and number with a sealed line that names the case
        // it came from; the identity follows those lines in order (sketch ÄR1b).
        foreach (var details in BranchDetailsOf(database, transaction))
        {
            if (BranchedIdentity(details, identity) is { } branched)
            {
                identity = branched;
            }
        }

        return identity.Id == info.Id.ToString("D") && identity.Number == info.CaseNumber;
    }

    /// <summary>Reads the details of every line that says a working copy became a case of its own, in order.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <returns>The details.</returns>
    private static List<string> BranchDetailsOf(CaseDatabase database, SqliteTransaction transaction)
    {
        using var command = database.Command(
            "SELECT details FROM journal WHERE kind = $kind ORDER BY sequence", transaction);
        command.Parameters.AddWithValue("$kind", CaseJournalKinds.BranchSaved);
        using var reader = command.ExecuteReader();
        var details = new List<string>();
        while (reader.Read())
        {
            details.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        }

        return details;
    }

    /// <summary>
    /// Gives the identity a branch line moves the case to, when the line comes from the identity the case has so far.
    /// </summary>
    /// <param name="details">The line's details.</param>
    /// <param name="identity">The identity so far.</param>
    /// <returns>The new identity, or <see langword="null"/> when the line does not follow from it.</returns>
    private static (string Id, string Number)? BranchedIdentity(string details, (string Id, string Number) identity)
    {
        try
        {
            using var document = JsonDocument.Parse(details);
            var root = document.RootElement;
            return root.TryGetProperty(BranchDetails.FromCaseId, out var from) && from.GetString() == identity.Id
                && root.TryGetProperty(BranchDetails.CaseId, out var id) && id.GetString() is { } newId
                && root.TryGetProperty(BranchDetails.CaseNumber, out var number)
                && number.GetString() is { } newNumber
                    ? (newId, newNumber)
                    : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Checks whether the journal records that the anchors were ever replaced.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <returns><see langword="true"/> when it does.</returns>
    private static bool HasReplacedAnchors(CaseDatabase database, SqliteTransaction transaction)
    {
        using var command = database.Command("SELECT count(*) FROM journal WHERE kind = $kind", transaction);
        command.Parameters.AddWithValue("$kind", CaseJournalKinds.AnchorsReplaced);
        return command.ExecuteScalar() is long count && count > 0;
    }

    /// <summary>
    /// Checks for a trigger that Chainsaw does not make: every trigger must be one of the Cases module's protecting
    /// triggers or a sealed table's, exactly as created. A trigger on a table that is not sealed (for example one that
    /// drops a module's index rows with <c>RAISE(IGNORE)</c>) is otherwise seen by no check, and runs on every write.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <returns><see langword="true"/> when there is such a trigger.</returns>
    private static bool HasForeignTrigger(CaseDatabase database, SqliteTransaction transaction)
    {
        using var command = database.Command(
            "SELECT name, tbl_name, sql FROM sqlite_schema WHERE type = 'trigger'", transaction);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var name = reader.GetValue(0) as string ?? string.Empty;
            var table = reader.GetValue(1) as string ?? string.Empty;
            var sql = reader.GetValue(2) as string;
            var known = CasesSchema.ProtectingTriggers.Any(trigger => trigger.Name == name && trigger.Table == table)
                || HashChain.Triggers(table).Any(trigger => trigger.Name == name && trigger.Sql == sql);
            if (!known)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks that a protecting trigger is in place as it was created, so that one changed to never abort (for
    /// example with <c>WHEN 0</c>) is found as the sealed tables' are; only the spacing may differ.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="trigger">The table, the trigger's name and its SQL.</param>
    /// <returns><see langword="true"/> when the trigger is there, unchanged.</returns>
    private static bool HasTrigger(
        CaseDatabase database, SqliteTransaction transaction, (string Table, string Name, string Sql) trigger)
    {
        using var command = database.Command(
            "SELECT sql FROM sqlite_schema WHERE type = 'trigger' AND tbl_name = $table AND name = $name",
            transaction);
        command.Parameters.AddWithValue("$table", trigger.Table);
        command.Parameters.AddWithValue("$name", trigger.Name);
        return command.ExecuteScalar() is string sql
            && string.Equals(Normalized(sql), Normalized(trigger.Sql), StringComparison.Ordinal);
    }

    /// <summary>Gives SQL with every run of white space as one space and without a closing semicolon.</summary>
    /// <param name="sql">The SQL.</param>
    /// <returns>The normalized SQL.</returns>
    private static string Normalized(string sql) =>
        string.Join(' ', sql.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd(';');

    /// <summary>
    /// Reads the id and number the first journal entry created the case with, when its creation time is the one in
    /// the case information.
    /// </summary>
    /// <param name="details">The details.</param>
    /// <param name="createdUtc">The creation time as written in the case information.</param>
    /// <returns>The id and number, or <see langword="null"/> when they cannot be read or the time differs.</returns>
    private static (string Id, string Number)? CreatedIdentity(string? details, string createdUtc)
    {
        try
        {
            using var document = JsonDocument.Parse(details ?? string.Empty);
            var root = document.RootElement;
            return root.TryGetProperty(CreatedDetails.CaseId, out var id) && id.GetString() is { } caseId
                && root.TryGetProperty(CreatedDetails.CaseNumber, out var number)
                && number.GetString() is { } caseNumber
                && root.TryGetProperty(CreatedDetails.CreatedUtc, out var created) && created.GetString() == createdUtc
                    ? (caseId, caseNumber)
                    : null;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            // Not JSON, not an object, or a property that is not text.
            return null;
        }
    }
}
