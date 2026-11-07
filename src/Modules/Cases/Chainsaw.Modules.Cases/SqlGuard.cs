using System.Collections.Frozen;
using System.Collections.Immutable;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The authorizer that SQLite asks before it prepares every statement on a case's connection. It is a second line of
/// defence behind the triggers: whatever SQL another module gives, sealed tables cannot be changed or emptied, their
/// triggers cannot be dropped or bypassed with triggers or indexes of its own, the key and cipher cannot be changed,
/// and no database can be attached (which would let case data be copied out unencrypted).
/// </summary>
/// <remarks>
/// The guard protects against mistakes and misuse inside Chainsaw. Someone who has the file key and opens the
/// database with another program is not stopped by it; the hash chain and the anchors show what they changed.
/// </remarks>
internal sealed class SqlGuard
{
    /// <summary>SQLite's answer that allows an action.</summary>
    public const int Allow = 0;

    /// <summary>SQLite's answer that refuses an action and fails the statement.</summary>
    public const int Deny = 1;

    /// <summary>The prefix of the triggers that protect sealed tables; such a trigger can never be dropped.</summary>
    public const string SealTriggerPrefix = "seal_";

    /// <summary>The prefix of SQLite's own tables.</summary>
    private const string SqlitePrefix = "sqlite_";

    /// <summary>SQLite's table for AUTOINCREMENT counters, which an insert into a module's table may write.</summary>
    private const string SequenceTable = "sqlite_sequence";

    /// <summary>
    /// The prefix of SQLite3 Multiple Ciphers' functions, which change the cipher for the whole process.
    /// </summary>
    private const string CipherFunctionPrefix = "sqlite3mc";

    /// <summary>
    /// Settings that only read, with or without an argument, for example <c>table_info(journal)</c>. Every other
    /// setting is refused once the connection is open, the key, cipher and <c>writable_schema</c> included.
    /// </summary>
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

    /// <summary>Functions that read or write files or load code; never allowed.</summary>
    private static readonly FrozenSet<string> DeniedFunctions = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "load_extension", "readfile", "writefile", "edit", "fts3_tokenizer");

    /// <summary>The Cases module's own tables, which only it writes.</summary>
    private readonly FrozenSet<string> _caseTables;

    /// <summary>
    /// The sealed tables, whose records can never be changed or removed; grows when the registry is read.
    /// </summary>
    private ImmutableHashSet<string> _sealedTables;

    /// <summary>Creates the guard for a set of tables, in <see cref="GuardMode.Idle"/>.</summary>
    /// <param name="caseTables">The Cases module's own tables.</param>
    /// <param name="sealedTables">The sealed tables known so far.</param>
    public SqlGuard(IEnumerable<string> caseTables, IEnumerable<string> sealedTables)
    {
        _caseTables = caseTables.ToFrozenSet(StringComparer.OrdinalIgnoreCase);
        _sealedTables = sealedTables.ToImmutableHashSet(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets or sets who runs SQL now.</summary>
    public GuardMode Mode { get; set; } = GuardMode.Idle;

    /// <summary>
    /// Gets or sets the managed thread that the mode belongs to. A statement prepared on any other thread is refused,
    /// so that a module cannot borrow the Cases module's rights by running SQL on a second thread while Cases works.
    /// </summary>
    public int OwnerThread { get; set; }

    /// <summary>Adds sealed tables, for example those of modules this build does not know.</summary>
    /// <param name="tables">The tables.</param>
    public void AddSealed(IEnumerable<string> tables) => _sealedTables = _sealedTables.Union(tables);

    /// <summary>Decides one action, as SQLite's authorizer callback.</summary>
    /// <param name="action">SQLite's action code.</param>
    /// <param name="first">The first detail (a table, index, trigger, setting or function name).</param>
    /// <param name="second">
    /// The second detail (a table or column name, a setting's argument or a function's name).
    /// </param>
    /// <returns><see cref="Allow"/> or <see cref="Deny"/>.</returns>
    public int Authorize(int action, string? first, string? second)
    {
        var kind = (SqliteAction)action;
        if (Mode == GuardMode.Idle || Environment.CurrentManagedThreadId != OwnerThread
            || IsAlwaysDenied(kind, first, second))
        {
            return Deny;
        }

        var allowed = Mode switch
        {
            GuardMode.Internal => true,
            GuardMode.Migrating => AllowsMigrating(kind, first, second),
            GuardMode.Writing => AllowsWriting(kind, first, second),
            _ => AllowsReading(kind, first, second),
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
        SqliteAction.DropTrigger or SqliteAction.DropTempTrigger => IsSealTrigger(first),
        SqliteAction.Attach or SqliteAction.Detach => true,
        SqliteAction.CreateTempIndex or SqliteAction.CreateTempTable or SqliteAction.CreateTempTrigger
            or SqliteAction.CreateTempView or SqliteAction.CreateVirtualTable or SqliteAction.DropVirtualTable => true,
        SqliteAction.Pragma => !IsReadPragma(first, second),
        SqliteAction.Function => second is not null
            && (DeniedFunctions.Contains(second)
                || second.StartsWith(CipherFunctionPrefix, StringComparison.OrdinalIgnoreCase)),
        _ => false,
    };

    /// <summary>What another module's schema step may do.</summary>
    /// <param name="action">The action.</param>
    /// <param name="first">The first detail.</param>
    /// <param name="second">The second detail.</param>
    /// <returns><see langword="true"/> when allowed.</returns>
    private bool AllowsMigrating(SqliteAction action, string? first, string? second) => action switch
    {
        SqliteAction.CreateTable or SqliteAction.CreateView or SqliteAction.DropView => true,
        SqliteAction.Reindex or SqliteAction.Analyze => true,
        SqliteAction.CreateIndex or SqliteAction.DropIndex => !IsProtected(second),
        SqliteAction.DropTable => !IsProtected(first),
        SqliteAction.AlterTable => !IsProtected(second),
        SqliteAction.CreateTrigger => !IsProtected(second) && !IsSealTrigger(first),
        SqliteAction.DropTrigger => !IsProtected(second),

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
        SqliteAction.Function or SqliteAction.Pragma => true,
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

    /// <summary>Checks whether a name is one of SQLite's own tables other than the AUTOINCREMENT counters.</summary>
    /// <param name="table">The table name.</param>
    /// <returns><see langword="true"/> for <c>sqlite_schema</c>, <c>sqlite_master</c> and the like.</returns>
    private static bool IsSchemaTable(string? table) =>
        table is not null && table.StartsWith(SqlitePrefix, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(table, SequenceTable, StringComparison.OrdinalIgnoreCase);

    /// <summary>Checks whether a name is a trigger that protects a sealed or a case table.</summary>
    /// <param name="trigger">The trigger name.</param>
    /// <returns><see langword="true"/> when it has the reserved prefix.</returns>
    private static bool IsSealTrigger(string? trigger) =>
        trigger?.StartsWith(SealTriggerPrefix, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Checks whether a table is protected.</summary>
    /// <param name="table">The table name.</param>
    /// <returns><see langword="true"/> when only Cases may write it.</returns>
    private bool IsProtected(string? table) =>
        table is not null && (_caseTables.Contains(table) || _sealedTables.Contains(table));

    /// <summary>Checks whether a table is sealed.</summary>
    /// <param name="table">The table name.</param>
    /// <returns><see langword="true"/> when its records can never be changed or removed.</returns>
    private bool IsSealed(string? table) => table is not null && _sealedTables.Contains(table);
}
