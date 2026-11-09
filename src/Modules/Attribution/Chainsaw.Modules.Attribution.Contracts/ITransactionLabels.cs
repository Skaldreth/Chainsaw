using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// Labels on transactions: what a transaction itself is, for example a swap at a known service or a bridge's deposit,
/// where no address says it. Every label is sealed in the case with a line in the journal and taken back the same
/// way (principle 1), and it is a judgement, never a fact (principle 2). A label that says where the money goes (a
/// swap or bridge, a mixer, an exchange) ends the trail at the transaction, with the label as its reason.
/// </summary>
/// <remarks>
/// The methods are synchronous and read or write the case; call them off the UI thread. Labels on transactions stay in
/// their case: they do not go to the label library (that is for addresses), which BACKLOG keeps as a later step.
/// </remarks>
public interface ITransactionLabels
{
    /// <summary>Sets a label on a transaction, sealed, with a line in the journal; the operator set it.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="draft">The label.</param>
    /// <returns>The sealed label.</returns>
    /// <exception cref="ArgumentException">The draft is not complete, or a text is too long.</exception>
    TransactionLabel Add(ICase @case, TransactionLabelDraft draft);

    /// <summary>
    /// Takes a label on a transaction back with a line in the journal: a new sealed record with who, when and the
    /// reason, if one is given. Nothing is removed.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="sequence">The label's <see cref="TransactionLabel.Sequence"/>.</param>
    /// <param name="reason">Why, at most 1 000 characters; optional.</param>
    /// <returns><see langword="false"/> when there is no such label or it is already taken back.</returns>
    bool Retract(ICase @case, long sequence, string? reason);

    /// <summary>Reads every label on a transaction in the case, also those taken back, oldest first.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The labels.</returns>
    IReadOnlyList<TransactionLabel> List(ICase @case);

    /// <summary>Reads the labels in force on one transaction.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="chain">The transaction's chain.</param>
    /// <param name="id">The transaction's id as <see cref="TransactionLabelDraft.Id"/> checks it.</param>
    /// <returns>The labels not taken back, oldest first.</returns>
    IReadOnlyList<TransactionLabel> For(ICase @case, Chain chain, string id);
}

/// <summary>A label to set on a transaction.</summary>
/// <param name="Chain">The transaction's chain: Bitcoin or Ethereum.</param>
/// <param name="Id">
/// The transaction's id: on Bitcoin its txid (64 hex digits), on Ethereum its hash (<c>0x</c> and 64 hex digits).
/// </param>
/// <param name="Kind">What the transaction is, for example <see cref="LabelKind.SwapBridge"/>.</param>
/// <param name="Name">The service's name, optional.</param>
/// <param name="Reason">Why, optional (the user's decision 2026-11-07).</param>
/// <param name="Note">A note, optional.</param>
public sealed record TransactionLabelDraft(
    Chain Chain, string Id, LabelKind Kind, string? Name, string? Reason, string? Note);

/// <summary>A sealed label on a transaction.</summary>
/// <param name="Sequence">Its place in the case's sealed chain.</param>
/// <param name="Chain">The transaction's chain.</param>
/// <param name="Id">The transaction's id in lower case.</param>
/// <param name="Kind">What the transaction is.</param>
/// <param name="Name">The service's name, if given.</param>
/// <param name="SetBy">Who set it.</param>
/// <param name="SetUtc">When.</param>
/// <param name="Reason">Why, or empty.</param>
/// <param name="Note">The note, if any.</param>
/// <param name="JournalSequence">The journal entry that records it.</param>
/// <param name="Retraction">Its taking back, or <see langword="null"/> while it counts.</param>
public sealed record TransactionLabel(
    long Sequence,
    Chain Chain,
    string Id,
    LabelKind Kind,
    string? Name,
    OperatorProfile SetBy,
    DateTimeOffset SetUtc,
    string Reason,
    string? Note,
    long JournalSequence,
    LabelRetraction? Retraction)
{
    /// <summary>Gets whether the label counts: it is not taken back.</summary>
    public bool IsActive => Retraction is null;

    /// <summary>
    /// Gets the label's kind and name as the explanations show it, for example <c>Swap eller brygga (Syntetisk)</c>.
    /// </summary>
    public string Title => Name is null ? Kind.Describe() : $"{Kind.Describe()} ({Name})";
}
