using System.Data.Common;
using Chainsaw.Modules.Cases.Contracts;
using Microsoft.Data.Sqlite;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// The reader or transaction handed to another module's work; it stops working when the work returns.
/// </summary>
/// <param name="owner">The case.</param>
/// <param name="transaction">The SQLite transaction.</param>
/// <param name="writable">
/// <see langword="false"/> for <see cref="ICase.Read{T}"/>, so that a reader cast to a transaction cannot seal.
/// </param>
internal sealed class CaseTransaction(Case owner, SqliteTransaction transaction, bool writable) : ICaseTransaction
{
    /// <summary>The commands handed out, disposed when the work returns.</summary>
    private readonly List<CaseCommand> _commands = [];

    /// <summary>Whether the work has returned.</summary>
    private bool _ended;

    /// <summary>
    /// Whether the Cases module is sealing on the work's behalf; the work's commands are inert meanwhile, so that no
    /// module code can run SQL while the guard gives the Cases module's rights.
    /// </summary>
    private bool _busy;

    /// <inheritdoc />
    public DbCommand CreateCommand(string sql)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sql);
        EnsureActive();
        var command = new CaseCommand(owner.Database.Command(sql, transaction), () => !_ended && !_busy);
        _commands.Add(command);
        return command;
    }

    /// <inheritdoc />
    public JournalEntry AppendJournal(JournalDraft draft)
    {
        EnsureWritable();
        JournalRecords.ValidateFromModule(draft);
        return AsCases(() => owner.AppendJournalIn(transaction, draft));
    }

    /// <inheritdoc />
    public SealedRecord AppendSealed(string chain, IReadOnlyList<object?> values)
    {
        EnsureWritable();
        ArgumentNullException.ThrowIfNull(values);

        // The module's list is read here, with the work's rights, never inside the Cases module's scope.
        object?[] copy = [.. values];
        return AsCases(() => owner.AppendSealedIn(transaction, chain, copy));
    }

    /// <summary>Runs the Cases module's sealing with the work's commands made inert.</summary>
    /// <typeparam name="T">What the sealing gives.</typeparam>
    /// <param name="seal">The sealing.</param>
    /// <returns>What it gave.</returns>
    private T AsCases<T>(Func<T> seal)
    {
        _busy = true;
        try
        {
            return seal();
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Marks the work as returned and disposes its commands, so that kept references no longer work.</summary>
    public void End()
    {
        _ended = true;
        foreach (var command in _commands)
        {
            command.Dispose();
        }

        _commands.Clear();
    }

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
