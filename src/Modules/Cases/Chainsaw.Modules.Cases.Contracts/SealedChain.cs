namespace Chainsaw.Modules.Cases.Contracts;

/// <summary>
/// A table whose records can only be added to and are sealed in a hash chain, like the journal (principle 1). Each
/// record's hash is SHA-256 over the previous record's hash and the record's content in an unambiguous encoding, so
/// a change anywhere breaks the chain from that record on, and <see cref="ICaseVerifier"/> says where.
/// </summary>
/// <param name="Name">
/// The chain's stable name, for example <c>chains.raw_responses</c>: lower-case ASCII letters, digits, <c>_</c> and
/// <c>.</c>, starting with a letter.
/// </param>
/// <param name="Table">The table: lower-case ASCII letters, digits and <c>_</c>, starting with a letter.</param>
/// <param name="ContentColumns">The columns that make up a record's content, in the order they are sealed.</param>
/// <remarks>
/// The table must be <c>STRICT</c>, with <c>sequence INTEGER PRIMARY KEY</c>, <c>hash BLOB NOT NULL</c> and the
/// content columns, each declared INTEGER, REAL, TEXT, BLOB or ANY. The Cases module adds the triggers that refuse
/// UPDATE and DELETE in the same transaction that creates the table, and writes the records itself
/// (<see cref="ICaseTransaction.AppendSealed"/>).
/// </remarks>
public sealed record SealedChain(string Name, string Table, IReadOnlyList<string> ContentColumns);
