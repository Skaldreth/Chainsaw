using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution.Contracts;

/// <summary>
/// The labels in a case: what an address or a cluster is, always with its origin. Every label is sealed in the case
/// with a line in the journal, so that none changes silently, and it is taken back the same way (principle 1). A
/// label is a judgement, never a fact (principle 2).
/// </summary>
/// <remarks>The methods are synchronous and read or write the case; call them off the UI thread.</remarks>
public interface IAttributionService
{
    /// <summary>
    /// Sets a label in the case, sealed, with a line in the journal; the operator is who set it. The subject can be
    /// on any chain that carries labels (<see cref="LabelSubject.ForAccount"/>).
    /// </summary>
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
    /// Marks a label <em>Sprid inte</em>, or lets it spread again, with a line in the journal: a new sealed record
    /// with who and when, so that the label's own record is never touched (principle 1). Only the user's own labels
    /// (<see cref="LabelHistoryEntry.CanChangeSpread"/>) can be marked, and only while they count. The label library
    /// follows the next time the case is absorbed (<see cref="ILabelLibrary.Absorb"/>): a label marked
    /// <em>Sprid inte</em> then leaves it and is not given on from any case while it is marked, but copies already
    /// given to other cases stay there.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="sequence">The label's <see cref="LabelEntry.Sequence"/>.</param>
    /// <param name="doNotSpread">Whether the label stays in this case.</param>
    /// <returns>
    /// <see langword="false"/> when there is no such label, it is taken back, it is not the user's own, or it is
    /// already marked so.
    /// </returns>
    bool SetDoNotSpread(ICase @case, long sequence, bool doNotSpread);

    /// <summary>
    /// Reads the history of every label in the case, also those taken back, each with its taking back, oldest first:
    /// what the view Etiketter lists (sketch ET1a).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <returns>The labels and their taking back.</returns>
    IReadOnlyList<LabelHistoryEntry> GetHistory(ICase @case);

    /// <summary>
    /// Reads the history of the labels on some addresses: every label whose subject is one of them, also those taken
    /// back, each with its taking back, oldest first (sketch AN1b, <em>Historik</em>).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses, for a cluster all of its addresses.</param>
    /// <returns>The labels and their taking back.</returns>
    IReadOnlyList<LabelHistoryEntry> GetHistory(ICase @case, IReadOnlyCollection<BitcoinAddress> addresses);

    /// <summary>
    /// Reads the history of the labels on some addresses on any chain, as
    /// <see cref="GetHistory(ICase, IReadOnlyCollection{BitcoinAddress})"/> does for Bitcoin: the same text on two
    /// chains is two addresses.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses, each with its chain; for a cluster all of its addresses.</param>
    /// <returns>The labels and their taking back.</returns>
    IReadOnlyList<LabelHistoryEntry> GetHistory(ICase @case, IReadOnlyCollection<ChainAddress> addresses);

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
    /// Reads the labels that apply to many addresses on any chain at once, reading the case only once. A Bitcoin
    /// address gets the labels on itself and on its cluster, as
    /// <see cref="GetLabels(ICase, IReadOnlyCollection{BitcoinAddress})"/> gives them; an address on another chain
    /// (an Ethereum account) is not clustered and gets only the labels on itself. The same text on two chains is two
    /// addresses.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses, each with its chain.</param>
    /// <returns>The labels of each address that has any; an address without labels is not in it.</returns>
    IReadOnlyDictionary<ChainAddress, IReadOnlyList<Label>> GetLabels(
        ICase @case, IReadOnlyCollection<ChainAddress> addresses);

    /// <summary>
    /// Checks addresses against every current sanction list (<see cref="ISanctionLists.CurrentLists"/>: OFAC SDN and
    /// the EU's list) and labels each hit
    /// <see cref="LabelKind.Sanctioned"/> in the case, with the list's hash and date as the origin and one line in the
    /// journal. A hit already labelled from the same list, also one taken back, is not labelled again.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses.</param>
    /// <returns>What was checked and labelled.</returns>
    SanctionCheck CheckSanctions(ICase @case, IReadOnlyCollection<BitcoinAddress> addresses);

    /// <summary>
    /// Checks Ethereum addresses against the current sanction list as <see
    /// cref="CheckSanctions(ICase, IReadOnlyCollection{BitcoinAddress})"/> does Bitcoin's: the list's entries for
    /// ether and for USDT and USDC on Ethereum (<see cref="SanctionCurrency.OnEthereum"/>).
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses.</param>
    /// <returns>What was checked and labelled.</returns>
    SanctionCheck CheckSanctions(ICase @case, IReadOnlyCollection<EthereumAddress> addresses);

    /// <summary>
    /// Checks addresses on any chain against the sanction lists, each on its own chain as the overloads for Bitcoin
    /// and Ethereum do; an address on a chain the lists are not read for is passed over. Used when the graph is read
    /// or expanded, so that an address that comes in is checked the moment it is in the case.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses, each with its chain.</param>
    /// <returns>What was checked and labelled, both chains together.</returns>
    SanctionCheck CheckSanctions(ICase @case, IReadOnlyCollection<ChainAddress> addresses);

    /// <summary>
    /// Checks addresses against every imported open source (<see cref="IOpenSourceLists.CurrentLists"/>) and labels
    /// each hit with what the source says, sealed with the source's name, licence, date and hash as the origin
    /// (<see cref="LabelSource.OpenSource"/>) and one line in the journal. A hit already labelled from the same file,
    /// also one taken back, is not labelled again. What an open source says is a judgement, never a fact.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses, each with its chain.</param>
    /// <returns>What was checked and labelled.</returns>
    OpenSourceCheck CheckOpenSources(ICase @case, IReadOnlyCollection<ChainAddress> addresses);
}

/// <summary>What a check against the sanction list found.</summary>
/// <param name="List">The list that was used, or <see langword="null"/> when no list is imported.</param>
/// <param name="Added">The new labels.</param>
/// <param name="AlreadyLabelled">Hits that were already labelled from the same list.</param>
public sealed record SanctionCheck(SanctionListInfo? List, IReadOnlyList<LabelEntry> Added, int AlreadyLabelled)
{
    /// <summary>
    /// Gets the current lists that could not be looked on, since their file has lost its hash; the others were.
    /// </summary>
    public IReadOnlyList<SanctionListInfo> Unusable { get; init; } = [];
}
