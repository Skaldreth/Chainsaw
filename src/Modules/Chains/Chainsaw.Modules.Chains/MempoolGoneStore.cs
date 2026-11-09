using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Chains;

/// <summary>A sealed record in force that a transaction which never was in a block is gone from the mempool.</summary>
/// <param name="Sequence">The record's number in <see cref="ChainsSchema.MempoolGoneChain"/>.</param>
/// <param name="SeenSequence">The last sealed answer that had the transaction unconfirmed.</param>
/// <param name="GoneSequence">The newest sealed answer that said the source does not know it, when it was sealed.</param>
internal sealed record MempoolGoneRecord(long Sequence, long SeenSequence, long GoneSequence);

/// <summary>
/// Reads and writes the sealed records of the transactions that never were in a block and are counted as gone from the
/// mempool (<see cref="BlockChangeRule.CompareMempool"/>), and of their coming back, each with a line in the journal.
/// A transaction counts as gone only while such a record is in force and no answer after it has the transaction again,
/// so that nothing reads as gone without a line in the journal saying why, and the case shows it again as soon as a
/// source does (principles 1 and 3). No record is ever changed: coming back is a record of its own.
/// </summary>
/// <param name="chainName">The chain and network in the sealed table, for example <c>bitcoin-mainnet</c>.</param>
/// <param name="displayName">The chain as the user reads it, for example <c>Bitcoin</c>.</param>
internal sealed class MempoolGoneStore(string chainName, string displayName)
{
    /// <summary>The journal kind of a transaction counted as gone from the mempool.</summary>
    public const string GoneJournalKind = "chains.mempool-gone";

    /// <summary>The journal kind of a transaction that a source knows again after it was counted as gone.</summary>
    public const string BackJournalKind = "chains.mempool-back";

    /// <summary>The rule's name and version, as the journal and <em>Varför?</em> name it.</summary>
    public const string Rule = "mempool-gone v1";

    /// <summary>The action of a record that counts a transaction as gone.</summary>
    private const string GoneAction = "gone";

    /// <summary>The action of a record that says a transaction is known again.</summary>
    private const string BackAction = "back";

    /// <summary>
    /// Seals, after a lookup of a transaction, what the newest answers change: that it is gone from the mempool when
    /// the rule first holds, or that it is back when an answer after the record in force has it again. The answers are
    /// read first and the case is written only when something changed, read again inside the write so that two lookups
    /// at once seal it once.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="subject">The transaction's id as its chain writes it.</param>
    /// <param name="store">The chain's sealed answers.</param>
    /// <param name="answersOf">Reads every sealed answer about the transaction.</param>
    /// <returns><see langword="true"/> when a record was sealed.</returns>
    public bool Settle(
        ICase @case, string subject, RawResponseStore store, Func<ICaseReader, IReadOnlyList<BlockAnswer>> answersOf)
    {
        ArgumentNullException.ThrowIfNull(@case);
        if (!@case.Read(reader => Changes(reader, subject, store, answersOf(reader))))
        {
            return false;
        }

        return @case.Write(transaction => Seal(transaction, subject, store, answersOf(transaction)));
    }

    /// <summary>
    /// Gives a transaction's change when it is gone from the mempool: a record in force, and no answer after it that
    /// has the transaction. The change runs from the last answer that had it unconfirmed to the newest that says the
    /// source does not know it.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="subject">The transaction's id as its chain writes it.</param>
    /// <param name="answers">Every sealed answer about it.</param>
    /// <returns>The answers it rests on, or <see langword="null"/> when it is not gone from the mempool.</returns>
    public (BlockSighting Before, BlockSighting Now)? Gone(
        ICaseReader reader, string subject, IReadOnlyList<BlockAnswer> answers)
    {
        if (InForce(reader, subject) is not { } record || BackAfter(answers, record) is not null)
        {
            return null;
        }

        var before = answers.FirstOrDefault(answer => answer.Sighting.Sequence == record.SeenSequence);
        var now = answers.Where(answer => answer.IsGone).MaxBy(answer => answer.Sighting.Sequence);
        return before.Sighting is null || now.Sighting is null ? null : (before.Sighting, now.Sighting);
    }

    /// <summary>Reads the record in force for a transaction: its newest record, when that counts it as gone.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="subject">The transaction's id as its chain writes it.</param>
    /// <returns>The record, or <see langword="null"/>.</returns>
    public MempoolGoneRecord? InForce(ICaseReader reader, string subject)
    {
        ArgumentNullException.ThrowIfNull(reader);
        using var command = reader.CreateCommand($"""
            SELECT sequence, action, seen_sequence, gone_sequence FROM {ChainsSchema.MempoolGoneTable}
            WHERE chain = $chain AND subject = $subject ORDER BY sequence DESC LIMIT 1
            """);
        Add(command, "$chain", chainName);
        Add(command, "$subject", subject);
        using var rows = command.ExecuteReader();
        return rows.Read() && rows.GetString(1) == GoneAction
            ? new MempoolGoneRecord(rows.GetInt64(0), rows.GetInt64(2), rows.GetInt64(3))
            : null;
    }

    /// <summary>Says whether the answers change what is sealed about a transaction.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="subject">The transaction's id.</param>
    /// <param name="store">The chain's sealed answers.</param>
    /// <param name="answers">Every sealed answer about it.</param>
    /// <returns><see langword="true"/> when a record is to be sealed.</returns>
    private bool Changes(
        ICaseReader reader, string subject, RawResponseStore store, IReadOnlyList<BlockAnswer> answers) =>
        InForce(reader, subject) is { } record
            ? BackAfter(answers, record) is not null
            : BlockChangeRule.CompareMempool(answers, store, reader) is not null;

    /// <summary>Seals what the answers change, with its line in the journal.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="subject">The transaction's id.</param>
    /// <param name="store">The chain's sealed answers.</param>
    /// <param name="answers">Every sealed answer about it.</param>
    /// <returns><see langword="true"/> when a record was sealed.</returns>
    private bool Seal(
        ICaseTransaction transaction, string subject, RawResponseStore store, IReadOnlyList<BlockAnswer> answers)
    {
        if (InForce(transaction, subject) is { } record)
        {
            if (BackAfter(answers, record) is not { } back)
            {
                return false;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                BackJournalKind,
                $"{displayName}-tx {subject} syns igen hos källan och räknas inte längre som borta ur mempool",
                JsonSerializer.Serialize(new
                {
                    chain = chainName, transaction = subject, answer = back.Sighting.Sequence,
                    endpoint = back.Sighting.Endpoint, goneRecord = record.Sequence, rule = Rule,
                })));
            Append(transaction, BackAction, subject, back.Sighting.Sequence, record.Sequence, journal);
            return true;
        }

        if (BlockChangeRule.CompareMempool(answers, store, transaction) is not { } gone)
        {
            return false;
        }

        var entry = transaction.AppendJournal(new JournalDraft(
            GoneJournalKind,
            $"{displayName}-tx {subject} räknas som borta ur mempool: den låg aldrig i ett block, och källan känner "
                + "inte längre till den",
            JsonSerializer.Serialize(new
            {
                chain = chainName, transaction = subject, lastSeen = gone.Before.Sequence,
                notKnown = gone.Now.Sequence, endpoint = gone.Now.Endpoint, rule = Rule,
                graceMinutes = (int)BlockChangeRule.MempoolGrace.TotalMinutes,
            })));
        Append(transaction, GoneAction, subject, gone.Before.Sequence, gone.Now.Sequence, entry);
        return true;
    }

    /// <summary>Gives the first answer after a record that has the transaction again, in a block or not.</summary>
    /// <param name="answers">Every sealed answer about it.</param>
    /// <param name="record">The record in force.</param>
    /// <returns>The answer, or <see langword="null"/>.</returns>
    private static BlockAnswer? BackAfter(IReadOnlyList<BlockAnswer> answers, MempoolGoneRecord record) =>
        answers.Where(answer => !answer.IsGone && answer.Sighting.Sequence > record.GoneSequence)
            .OrderBy(answer => answer.Sighting.Sequence)
            .Select(answer => (BlockAnswer?)answer)
            .FirstOrDefault();

    /// <summary>Seals one record.</summary>
    /// <param name="transaction">The case's transaction.</param>
    /// <param name="action">Gone or back.</param>
    /// <param name="subject">The transaction's id.</param>
    /// <param name="seen">The answer the record rests on that had the transaction.</param>
    /// <param name="gone">The newest answer that did not, or the gone record a coming back ends.</param>
    /// <param name="journal">The record's line in the journal.</param>
    private void Append(
        ICaseTransaction transaction, string action, string subject, long seen, long gone, JournalEntry journal) =>
        transaction.AppendSealed(
            ChainsSchema.MempoolGoneChain,
            [action, chainName, subject, seen, gone, journal.Sequence,
                journal.Utc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture)]);

    /// <summary>Adds a parameter to a command.</summary>
    /// <param name="command">The command.</param>
    /// <param name="name">The parameter's name.</param>
    /// <param name="value">Its value.</param>
    private static void Add(System.Data.Common.DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
