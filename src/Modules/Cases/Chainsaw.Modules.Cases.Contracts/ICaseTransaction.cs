namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>Writes to an open case inside <see cref="ICase.Write{T}"/>; everything commits or rolls back together.</summary>
public interface ICaseTransaction : ICaseReader
{
    /// <summary>Adds one entry to the journal, sealed in the hash chain, in this transaction.</summary>
    /// <param name="draft">What happened.</param>
    /// <returns>The sealed entry.</returns>
    /// <exception cref="CaseException"><see cref="CaseError.NoOperatorProfile"/>.</exception>
    JournalEntry AppendJournal(JournalDraft draft);

    /// <summary>
    /// Adds one record to a sealed table (see <see cref="SealedChain"/>) and seals it in that table's hash chain, in
    /// this transaction. Sealed tables can only be written this way.
    /// </summary>
    /// <param name="chain">The name of the sealed chain.</param>
    /// <param name="values">
    /// One value per content column, in the order of <see cref="SealedChain.ContentColumns"/>: <see langword="null"/>,
    /// <see cref="long"/> for INTEGER, <see cref="double"/> for REAL, <see cref="string"/> for TEXT or a
    /// <see cref="byte"/> array for BLOB, matching the column's declared type.
    /// </param>
    /// <returns>The record's sequence number and hash.</returns>
    /// <exception cref="ArgumentException">The chain is unknown, or the values do not match the columns.</exception>
    /// <exception cref="CaseException">
    /// <see cref="CaseError.Corrupt"/> when the chain's table has been changed outside Chainsaw.
    /// </exception>
    SealedRecord AppendSealed(string chain, IReadOnlyList<object?> values);
}
