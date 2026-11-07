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
    /// <summary>The Cases module's own tables that are protected by triggers, and those triggers.</summary>
    private static readonly (string Table, string[] Triggers)[] ProtectedCaseTables =
    [
        ("case_info", ["seal_case_info_no_update", "seal_case_info_no_delete"]),
        (HashChain.RegistryTable, ["seal_sealed_chain_no_update", "seal_sealed_chain_no_delete"]),
    ];

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

        var results = new List<ChainVerification>();
        foreach (var chain in chains)
        {
            var result = HashChain.Verify(database, transaction, chain, registered.GetValueOrDefault(chain.Name));
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
    /// Checks the case information: it is what the case was opened with, its triggers are in place and the journal
    /// begins with the entry that created this case.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="info">The case information read when the case was opened.</param>
    /// <returns><see langword="true"/> when it holds.</returns>
    private static bool CaseInfoHolds(CaseDatabase database, SqliteTransaction transaction, CaseInfo info)
    {
        if (!ProtectedCaseTables.All(item => HasTriggers(database, transaction, item.Table, item.Triggers)))
        {
            return false;
        }

        string createdUtc;
        using (var row = database.Command("SELECT case_id, case_number, created_utc FROM case_info", transaction))
        using (var reader = row.ExecuteReader())
        {
            if (!reader.Read() || reader.GetString(0) != info.Id.ToString("D") || reader.GetString(1) != info.CaseNumber)
            {
                return false;
            }

            createdUtc = reader.GetString(2);
            if (reader.Read())
            {
                return false;
            }
        }

        var first = JournalRecords.Read(database, transaction, 1, 1).FirstOrDefault();
        return first is { Sequence: 1, Kind: CaseJournalKinds.Created } && CreatedFor(first.Details, info, createdUtc);
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

    /// <summary>Checks that a table has its protecting triggers and that they abort.</summary>
    /// <param name="database">The database.</param>
    /// <param name="transaction">The transaction.</param>
    /// <param name="table">The table.</param>
    /// <param name="triggers">The trigger names.</param>
    /// <returns><see langword="true"/> when every trigger is there.</returns>
    private static bool HasTriggers(
        CaseDatabase database, SqliteTransaction transaction, string table, IReadOnlyList<string> triggers)
    {
        var found = 0;
        foreach (var trigger in triggers)
        {
            using var command = database.Command(
                "SELECT count(*) FROM sqlite_schema WHERE type = 'trigger' AND tbl_name = $table AND name = $name "
                + "AND sql LIKE '%RAISE(ABORT%'",
                transaction);
            command.Parameters.AddWithValue("$table", table);
            command.Parameters.AddWithValue("$name", trigger);
            found += command.ExecuteScalar() is long count ? (int)count : 0;
        }

        return found == triggers.Count;
    }

    /// <summary>Checks that the details of the first journal entry name this case.</summary>
    /// <param name="details">The details.</param>
    /// <param name="info">The case information.</param>
    /// <param name="createdUtc">The creation time as written in the case information.</param>
    /// <returns><see langword="true"/> when the id, number and creation time match.</returns>
    private static bool CreatedFor(string? details, CaseInfo info, string createdUtc)
    {
        try
        {
            using var document = JsonDocument.Parse(details ?? string.Empty);
            var root = document.RootElement;
            return root.TryGetProperty(CreatedDetails.CaseId, out var id) && id.GetString() == info.Id.ToString("D")
                && root.TryGetProperty(CreatedDetails.CaseNumber, out var number)
                && number.GetString() == info.CaseNumber
                && root.TryGetProperty(CreatedDetails.CreatedUtc, out var created) && created.GetString() == createdUtc;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
