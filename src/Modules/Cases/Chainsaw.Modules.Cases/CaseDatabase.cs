using System.Security.Cryptography;
using Chainsaw.Modules.Cases.Contracts;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The SQLite connection to a case's working copy, encrypted by SQLite3 Multiple Ciphers (ChaCha20-Poly1305, which
/// authenticates every page) with a raw 256-bit key, and guarded by <see cref="SqlGuard"/>.
/// </summary>
internal sealed class CaseDatabase : IDisposable
{
    /// <summary>The cipher, set explicitly so that a change of the library's default never changes the files.</summary>
    private const string Cipher = "chacha20";

    /// <summary>The settings for every connection: durable writes, no data left in freed pages, no shared cache.</summary>
    private const string ConnectionPragmas =
        "PRAGMA journal_mode = DELETE; PRAGMA synchronous = FULL; PRAGMA secure_delete = ON; "
        + "PRAGMA foreign_keys = ON; PRAGMA trusted_schema = OFF; PRAGMA temp_store = MEMORY;";

    /// <summary>The open connection.</summary>
    private readonly SqliteConnection _connection;

    /// <summary>The guard; kept in a field so that the callback that SQLite holds stays alive.</summary>
    private readonly SqlGuard _guard;

    /// <summary>The callback registered with SQLite; kept so that it is not collected while SQLite holds it.</summary>
    private readonly delegate_authorizer _authorizer;

    /// <summary>Loads the bundled SQLite3 Multiple Ciphers once per process.</summary>
    static CaseDatabase()
    {
        Batteries_V2.Init();
    }

    /// <summary>Wraps an open, keyed connection.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="guard">The guard to install.</param>
    private CaseDatabase(SqliteConnection connection, SqlGuard guard)
    {
        _connection = connection;
        _guard = guard;
        _authorizer = (_, action, first, second, _, _) =>
            _guard.Authorize(action, first.utf8_to_string(), second.utf8_to_string());
    }

    /// <summary>Gets who runs SQL now.</summary>
    public GuardMode Mode => _guard.Mode;

    /// <summary>Gets the connection, for the Cases module's own commands.</summary>
    public SqliteConnection Connection => _connection;

    /// <summary>Opens (or creates) the working copy with the database key and checks that the key fits.</summary>
    /// <param name="path">The working copy.</param>
    /// <param name="databaseKey">The 32-byte database key.</param>
    /// <param name="guard">The guard for the case's tables.</param>
    /// <returns>The open database.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.Corrupt"/> when the file does not open with the key.</exception>
    public static CaseDatabase Open(string path, ReadOnlySpan<byte> databaseKey, SqlGuard guard)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };
        var connection = new SqliteConnection(builder.ToString());
        try
        {
            connection.Open();
            Execute(connection, $"PRAGMA cipher = '{Cipher}';");
            ApplyKey(connection, databaseKey);
            Execute(connection, "SELECT count(*) FROM sqlite_schema;");
            Execute(connection, ConnectionPragmas);
            var database = new CaseDatabase(connection, guard);
            raw.sqlite3_set_authorizer(connection.Handle, database._authorizer, null);
            return database;
        }
        catch (SqliteException exception)
        {
            connection.Dispose();
            throw new CaseException(CaseError.Corrupt, exception);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>Creates a command for the Cases module's own SQL.</summary>
    /// <param name="sql">The SQL.</param>
    /// <param name="transaction">The current transaction, if any.</param>
    /// <returns>The command; the caller disposes it.</returns>
    public SqliteCommand Command(string sql, SqliteTransaction? transaction = null) =>
        new(sql, _connection, transaction);

    /// <summary>Begins a transaction; the caller is in a <see cref="As"/> scope that allows it.</summary>
    /// <param name="deferred">
    /// <see langword="true"/> for a read transaction that takes no write lock until it writes.
    /// </param>
    /// <returns>The transaction; disposing it without committing rolls it back.</returns>
    public SqliteTransaction Begin(bool deferred = false) => _connection.BeginTransaction(deferred);

    /// <summary>
    /// Lets SQL run as someone until the scope is disposed, which gives the mode back. Outside every scope the guard
    /// is <see cref="GuardMode.Idle"/> and refuses everything.
    /// </summary>
    /// <param name="mode">Who runs SQL in the scope.</param>
    /// <returns>The scope.</returns>
    public GuardScope As(GuardMode mode)
    {
        var previous = _guard.Mode;
        _guard.Mode = mode;
        return new GuardScope(_guard, previous);
    }

    /// <summary>Adds sealed tables to the guard, for example those of modules this build does not know.</summary>
    /// <param name="tables">The tables.</param>
    public void AddSealed(IEnumerable<string> tables) => _guard.AddSealed(tables);

    /// <summary>Closes the connection; SQLite3 Multiple Ciphers clears its copy of the key.</summary>
    public void Dispose()
    {
        _connection.Dispose();
    }

    /// <summary>Gives the key to SQLite3 Multiple Ciphers as a raw key literal from a pinned, cleared buffer.</summary>
    /// <param name="connection">The connection, before anything has been read.</param>
    /// <param name="databaseKey">The 32-byte database key.</param>
    /// <exception cref="SqliteException">SQLite refused the key.</exception>
    private static void ApplyKey(SqliteConnection connection, ReadOnlySpan<byte> databaseKey)
    {
        var literal = CaseCrypto.ToRawKeyLiteral(databaseKey);
        try
        {
            var result = raw.sqlite3_key(connection.Handle, literal);
            SqliteException.ThrowExceptionForRC(result, connection.Handle);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(literal);
        }
    }

    /// <summary>Runs SQL on a connection before the guard is installed.</summary>
    /// <param name="connection">The connection.</param>
    /// <param name="sql">The SQL.</param>
    private static void Execute(SqliteConnection connection, string sql)
    {
        using var command = new SqliteCommand(sql, connection);
        using var reader = command.ExecuteReader();
        do
        {
            while (reader.Read())
            {
            }
        }
        while (reader.NextResult());
    }
}
