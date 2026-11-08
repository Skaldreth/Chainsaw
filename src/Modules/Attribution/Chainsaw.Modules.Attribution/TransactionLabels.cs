using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// The labels on transactions in a case (see <see cref="ITransactionLabels"/>), sealed in their own chain
/// (<see cref="AttributionSchema.TransactionLabelsChain"/>), so that the address labels sealed before keep their
/// hashes.
/// </summary>
internal sealed class TransactionLabels : ITransactionLabels
{
    /// <summary>The action of a record that sets a label.</summary>
    private const string AddAction = "add";

    /// <summary>The action of a record that takes a label back.</summary>
    private const string RetractAction = "retract";

    /// <inheritdoc/>
    public TransactionLabel Add(ICase @case, TransactionLabelDraft draft)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(draft);
        if (!Enum.IsDefined(draft.Kind) || draft.Kind == LabelKind.Victim)
        {
            // A victim is a person whose money is followed, never what a transaction is.
            throw new ArgumentException("The label's kind is not one a transaction can have.", nameof(draft));
        }

        var id = Checked(draft.Chain, draft.Id)
            ?? throw new ArgumentException("The transaction's id is not valid on its chain.", nameof(draft));
        var name = LabelRules.Optional(draft.Name, LabelRules.MaximumNameLength, nameof(draft));
        var reason = LabelRules.Optional(draft.Reason, LabelRules.MaximumReasonLength, nameof(draft)) ?? string.Empty;
        var note = LabelRules.Optional(draft.Note, LabelRules.MaximumNoteLength, nameof(draft));
        return @case.Write(transaction =>
        {
            var journal = transaction.AppendJournal(new JournalDraft(
                AttributionJournalKinds.TransactionLabelAdded,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Etikett på tx: {0} är {1}{2}",
                    id,
                    draft.Kind.Describe().ToLowerInvariant(),
                    name is null ? string.Empty : $" ({name})"),
                JsonSerializer.Serialize(new
                {
                    chain = draft.Chain.ToString(), id, kind = draft.Kind.ToString(), name, reason,
                })));
            var record = transaction.AppendSealed(
                AttributionSchema.TransactionLabelsChain,
                [
                    AddAction, null, draft.Chain.ToString(), id, draft.Kind.ToString(), name,
                    JsonSerializer.Serialize(journal.Operator), LabelStore.Text(journal.Utc), reason, note,
                    journal.Sequence, LabelStore.Text(journal.Utc),
                ]);
            return new TransactionLabel(
                record.Sequence, draft.Chain, id, draft.Kind, name, journal.Operator, journal.Utc, reason, note,
                journal.Sequence, null);
        });
    }

    /// <inheritdoc/>
    public bool Retract(ICase @case, long sequence, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var checkedReason = LabelRules.Optional(reason, LabelRules.MaximumReasonLength, nameof(reason));
        return @case.Write(transaction =>
        {
            var label = Read(transaction).FirstOrDefault(each => each.Sequence == sequence && each.IsActive);
            if (label is null)
            {
                return false;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                AttributionJournalKinds.TransactionLabelRetracted,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Etiketten på tx togs tillbaka: {0} är {1}",
                    label.Id,
                    label.Kind.Describe().ToLowerInvariant()),
                JsonSerializer.Serialize(new { label = label.Sequence, reason = checkedReason })));
            transaction.AppendSealed(
                AttributionSchema.TransactionLabelsChain,
                [
                    RetractAction, label.Sequence, label.Chain.ToString(), label.Id, label.Kind.ToString(), null,
                    JsonSerializer.Serialize(journal.Operator), LabelStore.Text(journal.Utc), checkedReason, null,
                    journal.Sequence, LabelStore.Text(journal.Utc),
                ]);
            return true;
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<TransactionLabel> List(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(Read);
    }

    /// <inheritdoc/>
    public IReadOnlyList<TransactionLabel> For(ICase @case, Chain chain, string id)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var wanted = Checked(chain, id);
        return wanted is null
            ? []
            : [.. List(@case).Where(label => label.IsActive && label.Chain == chain && label.Id == wanted)];
    }

    /// <summary>Checks a transaction's id on its chain and writes it in lower case.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The id as given.</param>
    /// <returns>The id, or <see langword="null"/> when it is not valid on the chain.</returns>
    public static string? Checked(Chain chain, string? id) => chain switch
    {
        Chain.Bitcoin when Txid.TryParse(id, out var txid) => txid.Value,
        Chain.Ethereum when EthereumTxHash.TryParse(id?.ToLowerInvariant(), out var hash) => hash.Value,
        _ => null,
    };

    /// <summary>Reads every label on a transaction, each with its taking back.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <returns>The labels, oldest first.</returns>
    private static List<TransactionLabel> Read(ICaseReader reader)
    {
        using var command = reader.CreateCommand("""
            SELECT sequence, action, label_sequence, chain, transaction_id, kind, name, set_by, set_utc, reason, note,
                   journal_sequence
            FROM attribution_transaction_labels ORDER BY sequence
            """);
        using var rows = command.ExecuteReader();
        var labels = new List<TransactionLabel>();
        var retractions = new Dictionary<long, LabelRetraction>();
        while (rows.Read())
        {
            var by = JsonSerializer.Deserialize<OperatorProfile>(rows.GetString(7))
                ?? throw new InvalidDataException("A label on a transaction has no operator.");
            if (rows.GetString(1) == RetractAction)
            {
                retractions.TryAdd(rows.GetInt64(2), new LabelRetraction(
                    rows.GetInt64(0), by, LabelStore.Time(rows.GetString(8)),
                    rows.IsDBNull(9) ? null : rows.GetString(9), rows.GetInt64(11)));
                continue;
            }

            labels.Add(new TransactionLabel(
                rows.GetInt64(0),
                Enum.Parse<Chain>(rows.GetString(3)),
                rows.GetString(4),
                Enum.Parse<LabelKind>(rows.GetString(5)),
                rows.IsDBNull(6) ? null : rows.GetString(6),
                by,
                LabelStore.Time(rows.GetString(8)),
                rows.IsDBNull(9) ? string.Empty : rows.GetString(9),
                rows.IsDBNull(10) ? null : rows.GetString(10),
                rows.GetInt64(11),
                null));
        }

        return [.. labels.Select(label => retractions.TryGetValue(label.Sequence, out var retraction)
            ? label with { Retraction = retraction }
            : label)];
    }
}
