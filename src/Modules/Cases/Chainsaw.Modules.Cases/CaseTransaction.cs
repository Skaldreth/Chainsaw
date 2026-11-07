using System.Data.Common;
using Chainsaw.Modules.Cases.Contracts;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases;

/// <summary>The reader or transaction handed to another module's work; it stops working when the work returns.</summary>
/// <param name="owner">The case.</param>
/// <param name="transaction">The SQLite transaction.</param>
/// <param name="writable">
/// <see langword="false"/> for <see cref="ICase.Read{T}"/>, so that a reader cast to a transaction cannot seal.
/// </param>
internal sealed class CaseTransaction(Case owner, SqliteTransaction transaction, bool writable) : ICaseTransaction
{
    /// <summary>Whether the work has returned.</summary>
    private bool _ended;

    /// <inheritdoc />
    public DbCommand CreateCommand(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        EnsureActive();
        return owner.Database.Command(sql, transaction);
    }

    /// <inheritdoc />
    public JournalEntry AppendJournal(JournalDraft draft)
    {
        EnsureWritable();
        return owner.AppendJournalIn(transaction, draft);
    }

    /// <inheritdoc />
    public SealedRecord AppendSealed(string chain, IReadOnlyList<object?> values)
    {
        EnsureWritable();
        return owner.AppendSealedIn(transaction, chain, values);
    }

    /// <summary>Marks the work as returned, so that a kept reference can no longer be used.</summary>
    public void End() => _ended = true;

    /// <summary>Refuses writing from a reader or after the work has returned.</summary>
    /// <exception cref="InvalidOperationException">This is a reader, or the work has returned.</exception>
    private void EnsureWritable()
    {
        EnsureActive();
        if (!writable)
        {
            throw new InvalidOperationException("A read transaction cannot write.");
        }
    }

    /// <summary>Refuses use after the work has returned.</summary>
    /// <exception cref="InvalidOperationException">The work has returned.</exception>
    private void EnsureActive()
    {
        if (_ended)
        {
            throw new InvalidOperationException("The transaction has ended.");
        }
    }
}
