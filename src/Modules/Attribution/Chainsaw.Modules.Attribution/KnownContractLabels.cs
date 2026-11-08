using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution;

/// <summary>Labels the known contracts among a case's addresses (see <see cref="IKnownContractLabels"/>).</summary>
internal sealed class KnownContractLabels : IKnownContractLabels
{
    /// <summary>The start of a known contract's origin id; the address follows.</summary>
    private const string OriginPrefix = "known-contract/";

    /// <summary>The end of the origin id of a contract's former sanction, after the contract's own.</summary>
    private const string FormerSanctionSuffix = "/former-sanction";

    /// <summary>Gives the label a known contract gets, or none when the list does not end the trail there.</summary>
    /// <param name="category">What the contract is.</param>
    /// <returns>The kind, or <see langword="null"/>.</returns>
    public static LabelKind? KindOf(KnownContractCategory category) => category switch
    {
        KnownContractCategory.Mixer => LabelKind.Mixer,
        KnownContractCategory.Bridge => LabelKind.SwapBridge,
        _ => null,
    };

    /// <inheritdoc/>
    public IReadOnlyList<LabelEntry> Check(ICase @case, IReadOnlyCollection<ChainAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(addresses);
        var drafts = addresses
            .Where(address => address.Chain == Chain.Ethereum)
            .Select(address => EthereumAddress.TryParse(address.Value, out var parsed) ? parsed : null)
            .OfType<EthereumAddress>()
            .Distinct()
            .Select(KnownContracts.Find)
            .OfType<KnownContract>()
            .SelectMany(DraftsOf)
            .ToList();

        // Read first: the graph asks on every read, and a write transaction would make the case save itself again
        // with nothing changed.
        if (drafts.Count == 0 || @case.Read(reader => Fresh(LabelStore.Read(reader), drafts)).Count == 0)
        {
            return [];
        }

        return @case.Write(transaction =>
        {
            var fresh = Fresh(LabelStore.Read(transaction), drafts);
            if (fresh.Count == 0)
            {
                return (IReadOnlyList<LabelEntry>)[];
            }

            var contracts = fresh.Select(draft => draft.Contract).Distinct().ToList();
            var journal = transaction.AppendJournal(new JournalDraft(
                AttributionJournalKinds.KnownContractsMatched,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} enligt {1} {2}",
                    CountText(contracts.Count),
                    KnownContracts.Name,
                    KnownContracts.Version),
                JsonSerializer.Serialize(new
                {
                    list = KnownContracts.Name,
                    version = KnownContracts.Version,
                    labels = fresh.Select(draft => new
                    {
                        address = draft.Contract.Address.Value,
                        kind = draft.Kind.ToString(),
                        name = draft.Name,
                    }),
                })));
            return [.. fresh.Select(draft => LabelStore.Seal(transaction, LabelFor(draft, journal), journal))];
        });
    }

    /// <summary>
    /// Gives the labels a known contract gets: its kind (a mixer or a bridge), and a former sanction beside it when
    /// it was on a list and taken off it (the user's decision 2026-11-08 for Tornado Cash). A router gets none.
    /// </summary>
    /// <param name="contract">The contract.</param>
    /// <returns>The labels to set, not sealed yet.</returns>
    private static IEnumerable<Draft> DraftsOf(KnownContract contract)
    {
        if (KindOf(contract.Category) is not { } kind)
        {
            yield break;
        }

        var origin = OriginPrefix + contract.Address.Value;
        yield return new Draft(
            contract,
            kind,
            contract.Name,
            origin,
            $"Kontraktet är {contract.Name} ({contract.Operator}) {contract.Provenance()}.");
        if (contract.FormerSanction is { } former)
        {
            yield return new Draft(
                contract,
                LabelKind.FormerlySanctioned,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{former.List} {former.Designated:yyyy-MM-dd} – {former.Removed:yyyy-MM-dd}"),
                origin + FormerSanctionSuffix,
                former.Describe());
        }
    }

    /// <summary>
    /// Gives the labels the case has never had. Any way in counts, deliberately: a copy from another case (the same
    /// origin id through the library), or a label taken back, is never set again, so that the same judgement never
    /// stands twice and a label the user took back stays taken back.
    /// </summary>
    /// <param name="stored">The case's labels.</param>
    /// <param name="drafts">The labels the known contracts among the addresses get.</param>
    /// <returns>Those not labelled yet.</returns>
    private static List<Draft> Fresh(StoredLabels stored, IEnumerable<Draft> drafts) =>
    [
        .. drafts.Where(draft => !stored.Has(LabelStore.Key(draft.OriginId, SubjectOf(draft.Contract), draft.Kind,
            draft.Name))),
    ];

    /// <summary>Says how many addresses are known contracts, in Swedish with the right number.</summary>
    /// <param name="count">How many; at least one.</param>
    /// <returns>For example <c>1 adress är ett känt kontrakt</c>.</returns>
    private static string CountText(int count) => count == 1
        ? "1 adress är ett känt kontrakt"
        : string.Create(CultureInfo.InvariantCulture, $"{count} adresser är kända kontrakt");

    /// <summary>Gives the subject a known contract's label is on.</summary>
    /// <param name="contract">The contract.</param>
    /// <returns>The subject.</returns>
    private static LabelSubject SubjectOf(KnownContract contract) =>
        LabelSubject.ForAccount(contract.Address.ToChainAddress());

    /// <summary>Gives the label for a draft.</summary>
    /// <param name="draft">The draft.</param>
    /// <param name="journal">The journal entry that records it.</param>
    /// <returns>The label, not sealed yet.</returns>
    private static LabelEntry LabelFor(Draft draft, JournalEntry journal)
    {
        var origin = new LabelOrigin(
            draft.OriginId,
            LabelSource.OpenSource,
            LabelSource.OpenSource,
            KnownContracts.Name,
            KnownContracts.Version,
            journal.Operator,
            journal.Utc,
            draft.Reason,
            null);
        return new LabelEntry(0, SubjectOf(draft.Contract), draft.Kind, draft.Name, origin, false, 0);
    }

    /// <summary>A label a known contract gets, before it is sealed.</summary>
    /// <param name="Contract">The contract.</param>
    /// <param name="Kind">The label's kind.</param>
    /// <param name="Name">The label's name.</param>
    /// <param name="OriginId">The origin's stable id, the same whatever the list's version.</param>
    /// <param name="Reason">Why, in Swedish, with the source.</param>
    private sealed record Draft(KnownContract Contract, LabelKind Kind, string Name, string OriginId, string Reason);
}
