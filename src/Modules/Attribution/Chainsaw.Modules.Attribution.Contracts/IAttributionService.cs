using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;

namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// The labels in a case: what an address or a cluster is, always with its origin. Every label is sealed in the case
/// with a line in the journal, so that none changes silently, and it is taken back the same way (principle 1). A
/// label is a judgement, never a fact (principle 2).
/// </summary>
/// <remarks>The methods are synchronous and read or write the case; call them off the UI thread.</remarks>
public interface IAttributionService
{
    /// <summary>Sets a label in the case, sealed, with a line in the journal; the operator is who set it.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="draft">The label.</param>
    /// <returns>The sealed label.</returns>
    /// <exception cref="ArgumentException">The draft is not complete, or a text is too long.</exception>
    /// <exception cref="CaseException">See <see cref="ICase.Write{T}"/>.</exception>
    LabelEntry Add(ICase @case, LabelDraft draft);

    /// <summary>
    /// Takes a label back with a line in the journal: a new sealed record with who, when and the reason, if one is
    /// given. Nothing is removed; the label stays sealed in the case, marked as taken back, so that what was believed
    /// and when can always be read, and it no longer counts. A label taken back cannot be taken back again; it is set
    /// anew instead. A label set in this case and taken back is withdrawn from the label library the next time the
    /// case is absorbed (<see cref="ILabelLibrary.Absorb"/>).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="sequence">The label's <see cref="LabelEntry.Sequence"/>.</param>
    /// <param name="reason">Why, at most 1 000 characters; optional (the user's decision 2026-11-07, AN1b).</param>
    /// <returns><see langword="false"/> when there is no such label or it is already taken back.</returns>
    /// <exception cref="ArgumentException">The reason is too long.</exception>
    bool Retract(ICase @case, long sequence, string? reason);

    /// <summary>
    /// Reads the history of the labels on some addresses: every label whose subject is one of them, also those taken
    /// back, each with its taking back, oldest first (sketch AN1b, <em>Historik</em>).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses, for a cluster all of its addresses.</param>
    /// <returns>The labels and their taking back.</returns>
    IReadOnlyList<LabelHistoryEntry> GetHistory(ICase @case, IReadOnlyCollection<BitcoinAddress> addresses);

    /// <summary>Reads every label in the case that is not taken back, oldest first.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The labels.</returns>
    IReadOnlyList<LabelEntry> GetEntries(ICase @case);

    /// <summary>Reads every label in the case that is not taken back, the same label merged from its origins.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The labels.</returns>
    IReadOnlyList<Label> GetLabels(ICase @case);

    /// <summary>
    /// Reads the labels that apply to an address: those on the address itself and those on its cluster (asked of
    /// Clustering), merged from their origins, the best supported first.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="address">The address.</param>
    /// <returns>The labels; empty when nothing is known.</returns>
    IReadOnlyList<Label> GetLabels(ICase @case, BitcoinAddress address);

    /// <summary>
    /// Reads the labels that apply to many addresses at once, as <see cref="GetLabels(ICase, BitcoinAddress)"/> does
    /// for one, but reading the case's labels and clusters only once, so that a large graph is not read address by
    /// address.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses.</param>
    /// <returns>The labels of each address that has any; an address without labels is not in it.</returns>
    IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<Label>> GetLabels(
        ICase @case, IReadOnlyCollection<BitcoinAddress> addresses);

    /// <summary>
    /// Checks addresses against the current sanction list (<see cref="ISanctionLists.Current"/>) and labels each hit
    /// <see cref="LabelKind.Sanctioned"/> in the case, with the list's hash and date as the origin and one line in the
    /// journal. A hit already labelled from the same list, also one taken back, is not labelled again.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses.</param>
    /// <returns>What was checked and labelled.</returns>
    SanctionCheck CheckSanctions(ICase @case, IReadOnlyCollection<BitcoinAddress> addresses);
}

/// <summary>What a check against the sanction list found.</summary>
/// <param name="List">The list that was used, or <see langword="null"/> when no list is imported.</param>
/// <param name="Added">The new labels.</param>
/// <param name="AlreadyLabelled">Hits that were already labelled from the same list.</param>
public sealed record SanctionCheck(SanctionListInfo? List, IReadOnlyList<LabelEntry> Added, int AlreadyLabelled);
