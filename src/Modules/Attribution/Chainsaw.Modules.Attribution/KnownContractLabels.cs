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
        var found = addresses
            .Where(address => address.Chain == Chain.Ethereum)
            .Select(address => EthereumAddress.TryParse(address.Value, out var parsed) ? parsed : null)
            .OfType<EthereumAddress>()
            .Distinct()
            .Select(KnownContracts.Find)
            .OfType<KnownContract>()
            .Select(contract => (Contract: contract, Kind: KindOf(contract.Category)))
            .Where(pair => pair.Kind is not null)
            .Select(pair => (pair.Contract, Kind: pair.Kind!.Value))
            .ToList();
        if (found.Count == 0)
        {
            return [];
        }

        return @case.Write(transaction =>
        {
            // Any way in counts: a copy from another case, or a label taken back, is never set again.
            var stored = LabelStore.Read(transaction);
            var fresh = found
                .Where(pair => !stored.Has(LabelStore.Key(
                    OriginOf(pair.Contract), SubjectOf(pair.Contract), pair.Kind, pair.Contract.Name)))
                .ToList();
            if (fresh.Count == 0)
            {
                return (IReadOnlyList<LabelEntry>)[];
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                AttributionJournalKinds.KnownContractsMatched,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} adresser är kända kontrakt enligt {1} {2}",
                    fresh.Count,
                    KnownContracts.Name,
                    KnownContracts.Version),
                JsonSerializer.Serialize(new
                {
                    list = KnownContracts.Name,
                    version = KnownContracts.Version,
                    contracts = fresh.Select(pair => new
                    {
                        address = pair.Contract.Address.Value,
                        name = pair.Contract.Name,
                        source = pair.Contract.Source.AbsoluteUri,
                    }),
                })));
            return [.. fresh.Select(pair => LabelStore.Seal(
                transaction, LabelFor(pair.Contract, pair.Kind, journal), journal))];
        });
    }

    /// <summary>Gives a known contract's stable origin, the same whatever the list's version.</summary>
    /// <param name="contract">The contract.</param>
    /// <returns>The origin id.</returns>
    private static string OriginOf(KnownContract contract) => OriginPrefix + contract.Address.Value;

    /// <summary>Gives the subject a known contract's label is on.</summary>
    /// <param name="contract">The contract.</param>
    /// <returns>The subject.</returns>
    private static LabelSubject SubjectOf(KnownContract contract) =>
        LabelSubject.ForAccount(contract.Address.ToChainAddress());

    /// <summary>Gives the label for a known contract.</summary>
    /// <param name="contract">The contract.</param>
    /// <param name="kind">The label's kind.</param>
    /// <param name="journal">The journal entry that records it.</param>
    /// <returns>The label, not sealed yet.</returns>
    private static LabelEntry LabelFor(KnownContract contract, LabelKind kind, JournalEntry journal)
    {
        var reason = string.Format(
            CultureInfo.InvariantCulture,
            "Kontraktet är {0} ({1}) {2}.",
            contract.Name,
            contract.Operator,
            contract.Provenance());
        var origin = new LabelOrigin(
            OriginOf(contract),
            LabelSource.OpenSource,
            LabelSource.OpenSource,
            KnownContracts.Name,
            KnownContracts.Version,
            journal.Operator,
            journal.Utc,
            reason,
            null);
        return new LabelEntry(0, SubjectOf(contract), kind, contract.Name, origin, false, 0);
    }
}
