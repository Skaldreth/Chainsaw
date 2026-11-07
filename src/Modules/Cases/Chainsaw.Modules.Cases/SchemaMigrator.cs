using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// Brings a case's schema up to date: every module's steps that have not run in this case run once, in order, each
/// in its own transaction together with the raised version and the triggers of the sealed tables it created.
/// </summary>
internal static class SchemaMigrator
{
    /// <summary>The table that holds each module's schema version in the case.</summary>
    private const string VersionTable = """
        CREATE TABLE IF NOT EXISTS schema_version (
            module TEXT PRIMARY KEY,
            version INTEGER NOT NULL
        ) STRICT;
        """;

    /// <summary>Runs the steps that have not run yet.</summary>
    /// <param name="database">The database.</param>
    /// <param name="catalog">The contributors.</param>
    /// <returns>The steps that ran, as <c>module version</c>, for the journal.</returns>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.UnsupportedVersion"/> when a module's version in the case is higher than this build knows.
    /// </exception>
    public static IReadOnlyList<string> Run(CaseDatabase database, SchemaCatalog catalog)
    {
        database.Mode = GuardMode.Internal;
        using (var create = database.Command(VersionTable))
        {
            create.ExecuteNonQuery();
        }

        var versions = ReadVersions(database);
        foreach (var contributor in catalog.Contributors)
        {
            var known = contributor.Steps.Count == 0 ? 0 : contributor.Steps[^1].Version;
            if (versions.GetValueOrDefault(contributor.Module) > known)
            {
                throw new CaseException(CaseError.UnsupportedVersion);
            }
        }

        var applied = new List<string>();
        foreach (var contributor in catalog.Contributors)
        {
            var current = versions.GetValueOrDefault(contributor.Module);
            foreach (var step in contributor.Steps.Where(step => step.Version > current))
            {
                RunStep(database, contributor, step);
                applied.Add(string.Create(CultureInfo.InvariantCulture, $"{contributor.Module} {step.Version}"));
            }
        }

        return applied;
    }

    /// <summary>Gives each module's schema version in the case.</summary>
    /// <param name="database">The database.</param>
    /// <returns>The versions by module.</returns>
    public static IReadOnlyDictionary<string, long> ReadVersions(CaseDatabase database)
    {
        var versions = new Dictionary<string, long>(StringComparer.Ordinal);
        using var select = database.Command("SELECT module, version FROM schema_version");
        using var reader = select.ExecuteReader();
        while (reader.Read())
        {
            versions[reader.GetString(0)] = reader.GetInt64(1);
        }

        return versions;
    }

    /// <summary>Runs one step in its own transaction.</summary>
    /// <param name="database">The database.</param>
    /// <param name="contributor">The step's module.</param>
    /// <param name="step">The step.</param>
    private static void RunStep(CaseDatabase database, ICaseSchemaContributor contributor, CaseSchemaStep step)
    {
        using var transaction = database.Begin();
        var newTables = contributor.SealedChains
            .Where(chain => !HashChain.TableExists(database, transaction, chain.Table))
            .ToList();
        try
        {
            database.Mode = contributor.Module == CasesSchema.ModuleName ? GuardMode.Internal : GuardMode.Migrating;
            using (var command = database.Command(step.Sql, transaction))
            {
                command.ExecuteNonQuery();
            }
        }
        catch (SqliteException exception)
        {
            throw new InvalidOperationException(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"The schema step {step.Version} of the module {contributor.Module} failed."),
                exception);
        }
        finally
        {
            database.Mode = GuardMode.Internal;
        }

        foreach (var chain in newTables.Where(chain => HashChain.TableExists(database, transaction, chain.Table)))
        {
            HashChain.InstallTriggers(database, transaction, chain);
        }

        using (var version = database.Command(
            "INSERT INTO schema_version (module, version) VALUES ($module, $version) "
            + "ON CONFLICT (module) DO UPDATE SET version = excluded.version",
            transaction))
        {
            version.Parameters.AddWithValue("$module", contributor.Module);
            version.Parameters.AddWithValue("$version", step.Version);
            version.ExecuteNonQuery();
        }

        transaction.Commit();
    }
}
