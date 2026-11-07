using System.Collections.Frozen;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The authorizer that SQLite asks before it prepares every statement on a case's connection. It is a second line of
/// defence behind the triggers: whatever SQL another module gives, sealed tables cannot be changed or emptied, their
/// triggers cannot be dropped or bypassed with triggers of its own, and no database can be attached (which would let
/// case data be copied out unencrypted).
/// </summary>
/// <remarks>
/// The guard protects against mistakes and misuse inside Chainsaw. Someone who has the file key and opens the
/// database with another program is not stopped by it; the hash chain shows what they changed.
/// </remarks>
internal sealed class SqlGuard
{
    /// <summary>SQLite's answer that allows an action.</summary>
    public const int Allow = 0;

    /// <summary>SQLite's answer that refuses an action and fails the statement.</summary>
    public const int Deny = 1;

    /// <summary>The prefix of the triggers that protect sealed tables; such a trigger can never be dropped.</summary>
    public const string SealTriggerPrefix = "seal_";

    /// <summary>Settings that only read and take an argument, for example <c>table_info(journal)</c>.</summary>
    private static readonly FrozenSet<string> ReadPragmasWithArgument = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "table_info", "table_xinfo", "table_list", "index_list", "index_info", "index_xinfo",
        "foreign_key_list", "foreign_key_check", "integrity_check", "quick_check");

    /// <summary>Settings that only read when they are given no argument.</summary>
    private static readonly FrozenSet<string> ReadPragmasWithoutArgument = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "user_version", "schema_version", "page_count", "page_size", "freelist_count", "data_version",
        "compile_options", "database_list", "function_list", "pragma_list", "collation_list", "module_list",
        "encoding", "foreign_keys");

    /// <summary>Functions that read or write files, load code or reach the cipher; never allowed outside Cases.</summary>
    private static readonly FrozenSet<string> DeniedFunctions = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "load_extension", "readfile", "writefile", "edit", "fts3_tokenizer");

    /// <summary>The tables that only the Cases module writes: the sealed tables and the case's own tables.</summary>
    private readonly FrozenSet<string> _protectedTables;

    /// <summary>The sealed tables, whose records can never be changed or removed.</summary>
    private readonly FrozenSet<string> _sealedTables;

    /// <summary>Creates the guard for a set of tables.</summary>
    /// <param name="protectedTables">The Cases module's own tables.</param>
    /// <param name="sealedTables">Every sealed table.</param>
    public SqlGuard(IEnumerable<string> protectedTables, IEnumerable<string> sealedTables)
    {
        _sealedTables = sealedTables.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _protectedTables = protectedTables.Concat(_sealedTables).ToFrozenSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets or sets who runs SQL now.</summary>
    public GuardMode Mode { get; set; } = GuardMode.Internal;

    /// <summary>Decides one action, as SQLite's authorizer callback.</summary>
    /// <param name="action">SQLite's action code.</param>
    /// <param name="first">The first detail (a table, index, trigger, setting or function name).</param>
    /// <param name="second">
    /// The second detail (a table or column name, a setting's argument or a function's name).
    /// </param>
    /// <returns><see cref="Allow"/> or <see cref="Deny"/>.</returns>
    public int Authorize(int action, string? first, string? second)
    {
        if (IsAlwaysDenied((SqliteAction)action, first, second))
        {
            return Deny;
        }

        var allowed = Mode switch
        {
            GuardMode.Internal => true,
            GuardMode.Migrating => AllowsMigrating((SqliteAction)action, first, second),
            GuardMode.Writing => AllowsWriting((SqliteAction)action, first, second),
            _ => AllowsReading((SqliteAction)action, first, second),
        };
        return allowed ? Allow : Deny;
    }

    /// <summary>What nobody may do, the Cases module included.</summary>
    /// <param name="action">The action.</param>
    /// <param name="first">The first detail.</param>
    /// <param name="second">The second detail.</param>
    /// <returns><see langword="true"/> when the action is refused in every mode.</returns>
    private bool IsAlwaysDenied(SqliteAction action, string? first, string? second) => action switch
    {
        SqliteAction.Update or SqliteAction.Delete or SqliteAction.DropTable => IsSealed(first),
        SqliteAction.AlterTable => IsSealed(second),
        SqliteAction.DropTrigger or SqliteAction.DropTempTrigger =>
            first?.StartsWith(SealTriggerPrefix, StringComparison.OrdinalIgnoreCase) == true,
        SqliteAction.Pragma => string.Equals(first, "writable_schema", StringComparison.OrdinalIgnoreCase)
            && second is not null,
        _ => false,
    };

    /// <summary>What another module's schema step may do.</summary>
    /// <param name="action">The action.</param>
    /// <param name="first">The first detail.</param>
    /// <param name="second">The second detail.</param>
    /// <returns><see langword="true"/> when allowed.</returns>
    private bool AllowsMigrating(SqliteAction action, string? first, string? second) => action switch
    {
        SqliteAction.CreateTable or SqliteAction.CreateIndex or SqliteAction.CreateView => true,
        SqliteAction.DropIndex or SqliteAction.DropView or SqliteAction.Reindex or SqliteAction.Analyze => true,
        SqliteAction.DropTable => !IsProtected(first),
        SqliteAction.AlterTable => !IsProtected(second),
        SqliteAction.CreateTrigger => !IsProtected(second) && !IsSealTrigger(first),
        SqliteAction.DropTrigger => true,

        // DDL writes SQLite's own schema table; SQLite itself refuses direct changes to it.
        SqliteAction.Insert or SqliteAction.Update or SqliteAction.Delete => !IsProtected(first),
        _ => AllowsWriting(action, first, second),
    };

    /// <summary>What another module's work in a write transaction may do.</summary>
    /// <param name="action">The action.</param>
    /// <param name="first">The first detail.</param>
    /// <param name="second">The second detail.</param>
    /// <returns><see langword="true"/> when allowed.</returns>
    private bool AllowsWriting(SqliteAction action, string? first, string? second) => action switch
    {
        SqliteAction.Insert or SqliteAction.Update or SqliteAction.Delete =>
            !IsProtected(first) && !IsSchemaTable(first),
        SqliteAction.Savepoint => true,
        _ => AllowsReading(action, first, second),
    };

    /// <summary>What another module's work in a read transaction may do.</summary>
    /// <param name="action">The action.</param>
    /// <param name="first">The first detail.</param>
    /// <param name="second">The second detail.</param>
    /// <returns><see langword="true"/> when allowed.</returns>
    private static bool AllowsReading(SqliteAction action, string? first, string? second) => action switch
    {
        SqliteAction.Select or SqliteAction.Read or SqliteAction.Recursive => true,
        SqliteAction.Function => second is null || !DeniedFunctions.Contains(second),
        SqliteAction.Pragma => IsReadPragma(first, second),
        _ => false,
    };

    /// <summary>Checks whether a setting only reads.</summary>
    /// <param name="name">The setting's name.</param>
    /// <param name="argument">Its argument, if any.</param>
    /// <returns><see langword="true"/> when the setting only reads.</returns>
    private static bool IsReadPragma(string? name, string? argument) =>
        name is not null
        && (ReadPragmasWithArgument.Contains(name)
            || (argument is null && ReadPragmasWithoutArgument.Contains(name)));

    /// <summary>Checks whether a name is one of SQLite's own schema tables.</summary>
    /// <param name="table">The table name.</param>
    /// <returns><see langword="true"/> for <c>sqlite_schema</c>, <c>sqlite_master</c> and their temp twins.</returns>
    private static bool IsSchemaTable(string? table) =>
        table is not null && table.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(table, "sqlite_sequence", StringComparison.OrdinalIgnoreCase);

    /// <summary>Checks whether a name is a trigger that protects a sealed table.</summary>
    /// <param name="trigger">The trigger name.</param>
    /// <returns><see langword="true"/> when it has the reserved prefix.</returns>
    private static bool IsSealTrigger(string? trigger) =>
        trigger?.StartsWith(SealTriggerPrefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Checks whether a table is protected.</summary>
    /// <param name="table">The table name.</param>
    /// <returns><see langword="true"/> when only Cases may write it.</returns>
    private bool IsProtected(string? table) => table is not null && _protectedTables.Contains(table);

    /// <summary>Checks whether a table is sealed.</summary>
    /// <param name="table">The table name.</param>
    /// <returns><see langword="true"/> when its records can never be changed or removed.</returns>
    private bool IsSealed(string? table) => table is not null && _sealedTables.Contains(table);
}
