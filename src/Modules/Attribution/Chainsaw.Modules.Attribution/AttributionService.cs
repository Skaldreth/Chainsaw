using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Attribution.Contracts;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Attribution;

/// <summary>The labels in a case, sealed with their origins (see <see cref="IAttributionService"/>).</summary>
/// <param name="clustering">Clustering, asked which cluster an address is in for labels on clusters.</param>
/// <param name="sanctions">The imported sanction lists.</param>
/// <param name="profiles">The operator profile, to see whether the operator already set the same label.</param>
internal sealed class AttributionService(
    IClusteringService clustering, ISanctionLists sanctions, IOperatorProfileStore profiles) : IAttributionService
{
    /// <inheritdoc/>
    public LabelEntry Add(ICase @case, LabelDraft draft)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(draft.Subject);
        if (!Enum.IsDefined(draft.Kind))
        {
            throw new ArgumentException("The label's kind is not known.", nameof(draft));
        }

        var name = LabelRules.Optional(draft.Name, LabelRules.MaximumNameLength, nameof(draft));
        var reason = LabelRules.Required(draft.Reason, LabelRules.MaximumReasonLength, nameof(draft));
        var note = LabelRules.Optional(draft.Note, LabelRules.MaximumNoteLength, nameof(draft));
        var (sourceName, sourceVersion) = draft.Source switch
        {
            LabelSource.Own => (LabelRules.OwnSourceName, LabelRules.OwnSourceVersion),
            LabelSource.Behaviour or LabelSource.OpenSource => (
                LabelRules.Required(draft.SourceName, LabelRules.MaximumNameLength, nameof(draft)),
                LabelRules.Required(draft.SourceVersion, LabelRules.MaximumNameLength, nameof(draft))),
            _ => throw new ArgumentException(
                "Sanction lists and other cases have their own ways in; a draft is own, behaviour or an open source.",
                nameof(draft)),
        };

        var operatorProfile = profiles.Get();
        return @case.Write(transaction =>
        {
            // Exactly the same label again from the same operator (a double click) is not sealed twice; anything
            // that differs (the reason, the note, Sprid inte, a newer version) is sealed as said.
            var same = LabelStore.Read(transaction).Active.FirstOrDefault(label => label.Subject == draft.Subject
                && label.Kind == draft.Kind
                && LabelStore.NameKey(label.Name) == LabelStore.NameKey(name)
                && label.DoNotSpread == draft.DoNotSpread
                && label.Origin.Source == draft.Source
                && label.Origin.SourceName == sourceName
                && label.Origin.SourceVersion == sourceVersion
                && label.Origin.Reason == reason
                && label.Origin.Note == note
                && label.Origin.SetBy.Name == operatorProfile?.Name);
            if (same is not null)
            {
                return same;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                AttributionJournalKinds.LabelAdded,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Etikett: {0} är {1}{2}",
                    draft.Subject,
                    draft.Kind.Describe().ToLowerInvariant(),
                    name is null ? string.Empty : $" ({name})"),
                JsonSerializer.Serialize(new
                {
                    subject = draft.Subject.Target.Value,
                    subjectKind = draft.Subject.Kind.ToString(),
                    kind = draft.Kind.ToString(),
                    name,
                    source = draft.Source.ToString(),
                    sourceName,
                    sourceVersion,
                    reason,
                    doNotSpread = draft.DoNotSpread,
                })));
            var origin = new LabelOrigin(
                Guid.NewGuid().ToString("N"),
                draft.Source,
                draft.Source,
                sourceName,
                sourceVersion,
                journal.Operator,
                journal.Utc,
                reason,
                note);
            return LabelStore.Seal(
                transaction, new LabelEntry(0, draft.Subject, draft.Kind, name, origin, draft.DoNotSpread, 0), journal);
        });
    }

    /// <inheritdoc/>
    public bool Retract(ICase @case, long sequence, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var checkedReason = LabelRules.Optional(reason, LabelRules.MaximumReasonLength, nameof(reason));
        return @case.Write(transaction =>
        {
            var stored = LabelStore.Read(transaction).Active.FirstOrDefault(label => label.Sequence == sequence);
            if (stored is null)
            {
                return false;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                AttributionJournalKinds.LabelRetracted,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Etiketten togs tillbaka: {0} är {1}",
                    stored.Subject,
                    stored.Kind.Describe().ToLowerInvariant()),
                JsonSerializer.Serialize(new { label = stored.Sequence, reason = checkedReason })));
            LabelStore.SealRetract(transaction, stored, checkedReason, journal);
            return true;
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<LabelHistoryEntry> GetHistory(ICase @case, IReadOnlyCollection<BitcoinAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        return GetHistory(@case, OnChain(addresses));
    }

    /// <inheritdoc/>
    public IReadOnlyList<LabelHistoryEntry> GetHistory(ICase @case, IReadOnlyCollection<ChainAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(addresses);
        var wanted = addresses.ToHashSet();
        if (wanted.Count == 0)
        {
            return [];
        }

        var stored = @case.Read(LabelStore.Read);
        return
        [
            .. stored.All.Where(label => wanted.Contains(label.Subject.Target))
                .Select(label => new LabelHistoryEntry(
                    label, stored.Retractions.TryGetValue(label.Sequence, out var retraction) ? retraction : null)),
        ];
    }

    /// <inheritdoc/>
    public IReadOnlyList<LabelEntry> GetEntries(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(LabelStore.Read).Active;
    }

    /// <inheritdoc/>
    public IReadOnlyList<Label> GetLabels(ICase @case) => LabelRules.Merge(GetEntries(@case));

    /// <inheritdoc/>
    public IReadOnlyList<Label> GetLabels(ICase @case, BitcoinAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        var target = address.ToChainAddress();
        var active = GetEntries(@case);
        var onClusters = active
            .Where(label => label.Subject.Kind == LabelSubjectKind.Cluster && label.Subject.Target != target)
            .ToList();
        IReadOnlySet<ChainAddress> cluster = onClusters.Count == 0
            ? new HashSet<ChainAddress>()
            : OnChain(clustering.GetCluster(@case, address).Addresses);
        return LabelRules.Merge(active.Where(label => label.Subject.Target == target
            || (label.Subject.Kind == LabelSubjectKind.Cluster && cluster.Contains(label.Subject.Target))));
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<BitcoinAddress, IReadOnlyList<Label>> GetLabels(
        ICase @case, IReadOnlyCollection<BitcoinAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var onChain = GetLabels(@case, OnChain(addresses));
        var labelled = new Dictionary<BitcoinAddress, IReadOnlyList<Label>>();
        foreach (var address in addresses.Distinct())
        {
            if (onChain.TryGetValue(address.ToChainAddress(), out var labels))
            {
                labelled[address] = labels;
            }
        }

        return labelled;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<ChainAddress, IReadOnlyList<Label>> GetLabels(
        ICase @case, IReadOnlyCollection<ChainAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var active = GetEntries(@case);
        var labelled = new Dictionary<ChainAddress, IReadOnlyList<Label>>();
        if (active.Count == 0 || addresses.Count == 0)
        {
            return labelled;
        }

        var onAddress = active.ToLookup(label => label.Subject.Target);
        var onClusters = active.Where(label => label.Subject.Kind == LabelSubjectKind.Cluster).ToList();
        var clusters = onClusters.Count == 0
            ? new Dictionary<ChainAddress, IReadOnlySet<ChainAddress>>()
            : ClustersOf(@case, addresses);
        foreach (var address in addresses.Distinct())
        {
            var cluster = clusters.GetValueOrDefault(address) ?? new HashSet<ChainAddress>();
            var labels = LabelRules.Merge(onAddress[address].Concat(onClusters.Where(label =>
                label.Subject.Target != address && cluster.Contains(label.Subject.Target))));
            if (labels.Count > 0)
            {
                labelled[address] = labels;
            }
        }

        return labelled;
    }

    /// <inheritdoc/>
    public SanctionCheck CheckSanctions(ICase @case, IReadOnlyCollection<BitcoinAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(addresses);
        // Each hit keeps the list it was found on, so that a list imported meanwhile is never named for it.
        var matches = addresses.Distinct().Select(address => (Address: address, Match: sanctions.Find(address)))
            .Where(found => found.Match is not null)
            .ToList();
        if (matches.Count == 0)
        {
            return new SanctionCheck(sanctions.Current, [], 0);
        }

        var list = matches[^1].Match!.List;
        var lists = string.Join(", ", matches.Select(found => found.Match!.List).DistinctBy(used => used.Sha256)
            .Select(used => used.Version));
        var hits = matches
            .SelectMany(found => found.Match!.Hits
                .Where(hit => hit.Currency == OfacSdnParser.BitcoinCurrency)
                .Select(hit => (found.Address, Hit: hit, found.Match.List)))
            .ToList();
        if (hits.Count == 0)
        {
            return new SanctionCheck(list, [], 0);
        }

        return @case.Write(transaction =>
        {
            // Only the case's own checks count: a copy from another case leaves this case's own hit to be sealed.
            var stored = LabelStore.Read(transaction);
            var drafts = hits
                .Select(found => (found.Address, found.Hit, found.List,
                    Origin: SanctionOriginId(found.List, found.Hit, found.Address)))
                .Where(found => !stored.Has(
                    LabelStore.Key(
                        found.Origin, LabelSubject.ForAddress(found.Address), LabelKind.Sanctioned, found.Hit.EntryName),
                    LabelSource.SanctionList))
                .ToList();
            if (drafts.Count == 0)
            {
                return new SanctionCheck(list, [], hits.Count);
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                AttributionJournalKinds.SanctionsMatched,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} adresser på {1} ({2})",
                    drafts.Count,
                    list.Name,
                    lists),
                JsonSerializer.Serialize(new
                {
                    list = list.Name,
                    hits = drafts.Select(found => new
                    {
                        address = found.Address.Value,
                        uid = found.Hit.EntryUid,
                        sha256 = found.List.Sha256,
                        published = found.List.PublishDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    }),
                })));
            var added = drafts
                .Select(found => LabelStore.Seal(
                    transaction, SanctionLabel(found.List, found.Hit, found.Address, found.Origin, journal), journal))
                .ToList();
            return new SanctionCheck(list, added, hits.Count - drafts.Count);
        });
    }

    /// <summary>Gives Bitcoin addresses as addresses with their chain.</summary>
    /// <param name="addresses">The addresses.</param>
    /// <returns>The addresses on Bitcoin.</returns>
    private static HashSet<ChainAddress> OnChain(IEnumerable<BitcoinAddress> addresses) =>
        [.. addresses.Select(address => address.ToChainAddress())];

    /// <summary>
    /// Asks Clustering, once, for the clusters of the Bitcoin addresses among some addresses. Only Bitcoin is
    /// clustered, so an address on another chain has no cluster.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="addresses">The addresses on any chain.</param>
    /// <returns>The cluster's addresses of each Bitcoin address that is in a cluster.</returns>
    private Dictionary<ChainAddress, IReadOnlySet<ChainAddress>> ClustersOf(
        ICase @case, IReadOnlyCollection<ChainAddress> addresses)
    {
        var bitcoin = new Dictionary<BitcoinAddress, ChainAddress>();
        foreach (var address in addresses)
        {
            if (address.Chain == Chain.Bitcoin && BitcoinAddress.TryParse(address.Value, out var parsed))
            {
                bitcoin.TryAdd(parsed, address);
            }
        }

        var clusters = new Dictionary<ChainAddress, IReadOnlySet<ChainAddress>>();
        if (bitcoin.Count == 0)
        {
            return clusters;
        }

        foreach (var (address, cluster) in clustering.GetClusters(@case, bitcoin.Keys))
        {
            if (bitcoin.TryGetValue(address, out var asked))
            {
                clusters[asked] = OnChain(cluster.Addresses);
            }
        }

        return clusters;
    }

    /// <summary>Gives the stable origin of a hit: the list's hash, the entry and the address.</summary>
    /// <param name="list">The list.</param>
    /// <param name="hit">The entry's address.</param>
    /// <param name="address">The address.</param>
    /// <returns>The origin id.</returns>
    private static string SanctionOriginId(SanctionListInfo list, SanctionedAddress hit, BitcoinAddress address) =>
        $"sanction/{list.Sha256}/{hit.EntryUid}/{address.Value}";

    /// <summary>Gives the label for a hit on the list.</summary>
    /// <param name="list">The list.</param>
    /// <param name="hit">The entry's address.</param>
    /// <param name="address">The address.</param>
    /// <param name="originId">The origin id.</param>
    /// <param name="journal">The journal entry that records it.</param>
    /// <returns>The label, not sealed yet.</returns>
    private static LabelEntry SanctionLabel(
        SanctionListInfo list, SanctionedAddress hit, BitcoinAddress address, string originId, JournalEntry journal)
    {
        var programs = hit.Programs.Count == 0 ? "inget program angivet" : string.Join(", ", hit.Programs);
        var reason = string.Format(
            CultureInfo.InvariantCulture,
            "Adressen står på listan som Digital Currency Address - {0} för {1} (uid {2}, {3}).",
            hit.Currency,
            hit.EntryName,
            hit.EntryUid,
            programs);
        var origin = new LabelOrigin(
            originId,
            LabelSource.SanctionList,
            LabelSource.SanctionList,
            list.Name,
            list.Version,
            journal.Operator,
            journal.Utc,
            reason,
            null);
        return new LabelEntry(
            0, LabelSubject.ForAddress(address), LabelKind.Sanctioned, hit.EntryName, origin, false, 0);
    }
}
