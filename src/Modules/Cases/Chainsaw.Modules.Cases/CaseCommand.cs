using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The command another module gets from <see cref="CaseTransaction.CreateCommand"/>. It runs only while the module's
/// work runs, in that work's transaction, and does not hand out the connection or the transaction, so that a module
/// cannot reach past the guard (for example by keeping the command, or by changing the key on the connection).
/// </summary>
/// <param name="inner">The SQLite command, bound to the work's transaction.</param>
/// <param name="isActive">Tells whether the work is still running.</param>
internal sealed class CaseCommand(SqliteCommand inner, Func<bool> isActive) : DbCommand
{
    /// <summary>The readers handed out, released when the work ends.</summary>
    private readonly List<CaseDataReader> _readers = [];

    /// <inheritdoc />
    [AllowNull]
    public override string CommandText
    {
        get => inner.CommandText;
        set => inner.CommandText = value;
    }

    /// <inheritdoc />
    public override int CommandTimeout
    {
        get => inner.CommandTimeout;
        set => inner.CommandTimeout = value;
    }

    /// <inheritdoc />
    public override CommandType CommandType
    {
        get => inner.CommandType;
        set => inner.CommandType = value;
    }

    /// <inheritdoc />
    public override bool DesignTimeVisible
    {
        get => false;
        set
        {
        }
    }

    /// <inheritdoc />
    public override UpdateRowSource UpdatedRowSource
    {
        get => inner.UpdatedRowSource;
        set => inner.UpdatedRowSource = value;
    }

    /// <summary>Gets nothing: the connection stays inside the Cases module. Setting it is refused.</summary>
    protected override DbConnection? DbConnection
    {
        get => null;
        set => throw new NotSupportedException("The connection of a case cannot be changed.");
    }

    /// <inheritdoc />
    protected override DbParameterCollection DbParameterCollection => inner.Parameters;

    /// <summary>Gets nothing: the transaction stays inside the Cases module. Setting it is refused.</summary>
    protected override DbTransaction? DbTransaction
    {
        get => null;
        set => throw new NotSupportedException("The transaction of a case cannot be changed.");
    }

    /// <inheritdoc />
    public override void Cancel() => inner.Cancel();

    /// <inheritdoc />
    public override int ExecuteNonQuery()
    {
        EnsureActive();
        return inner.ExecuteNonQuery();
    }

    /// <inheritdoc />
    public override object? ExecuteScalar()
    {
        EnsureActive();
        return inner.ExecuteScalar();
    }

    /// <inheritdoc />
    public override void Prepare()
    {
        EnsureActive();
        inner.Prepare();
    }

    /// <inheritdoc />
    protected override DbParameter CreateDbParameter() => inner.CreateParameter();

    /// <inheritdoc />
    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
    {
        EnsureActive();
        var reader = new CaseDataReader(inner.ExecuteReader(behavior & ~CommandBehavior.CloseConnection), isActive);
        _readers.Add(reader);
        return reader;
    }

    /// <summary>
    /// Disposes SQLite's command at once only while the work runs, since disposing it can run statements a reader
    /// left; otherwise <see cref="Release"/> does it when the work ends.
    /// </summary>
    /// <param name="disposing">Whether this is a call to <see cref="IDisposable.Dispose"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing && isActive())
        {
            Release();
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Disposes the readers and SQLite's command; called by the work's end, inside the work's own scope.
    /// </summary>
    internal void Release()
    {
        try
        {
            foreach (var reader in _readers)
            {
                reader.Release();
            }
        }
        finally
        {
            _readers.Clear();
            inner.Dispose();
        }
    }

    /// <summary>Refuses to run after the module's work has returned.</summary>
    /// <exception cref="InvalidOperationException">The work has returned.</exception>
    private void EnsureActive()
    {
        if (!isActive())
        {
            throw new InvalidOperationException("The command belongs to a read or write that has ended.");
        }
    }
}
