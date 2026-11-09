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
/// <param name="openSources">The imported open sources.</param>
/// <param name="profiles">The operator profile, to see whether the operator already set the same label.</param>
internal sealed class AttributionService(
    IClusteringService clustering,
    ISanctionLists sanctions,
    IOpenSourceLists openSources,
    IOperatorProfileStore profiles) : IAttributionService
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

        if (LabelRules.IsFromListsOnly(draft.Kind))
        {
            // A sanction, a possible or a former one comes only from an imported list or Chainsaw's own list of
            // known contracts, never from a judgement (principle 2).
            throw new ArgumentException("A sanction comes only from a list, never from a draft.", nameof(draft));
        }

        var name = LabelRules.Optional(draft.Name, LabelRules.MaximumNameLength, nameof(draft));
        var reason = LabelRules.Optional(draft.Reason, LabelRules.MaximumReasonLength, nameof(draft)) ?? string.Empty;
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
                    subject = draft.Subject.Stored,
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
    public bool SetDoNotSpread(ICase @case, long sequence, bool doNotSpread)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Write(transaction =>
        {
            var stored = LabelStore.Read(transaction);
            var label = stored.Active.FirstOrDefault(each => each.Sequence == sequence);
            if (label is null || label.DoNotSpread == doNotSpread
                || !new LabelHistoryEntry(label, null).CanChangeSpread)
            {
                return false;
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                AttributionJournalKinds.LabelSpreadChanged,
                string.Format(
                    CultureInfo.InvariantCulture,
                    doNotSpread ? "Etiketten sprids inte: {0} är {1}" : "Etiketten får spridas: {0} är {1}",
                    label.Subject,
                    label.Kind.Describe().ToLowerInvariant()),
                JsonSerializer.Serialize(new { label = label.Sequence, doNotSpread })));
            LabelStore.SealSpread(transaction, label, doNotSpread, journal);
            return true;
        });
    }

    /// <inheritdoc/>
    public IReadOnlyList<LabelHistoryEntry> GetHistory(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var stored = @case.Read(LabelStore.Read);
        return
        [
            .. stored.All.Select(label => new LabelHistoryEntry(
                label, stored.Retractions.TryGetValue(label.Sequence, out var retraction) ? retraction : null)),
        ];
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
        ArgumentNullException.ThrowIfNull(addresses);
        return CheckSanctions(@case, [.. addresses.Select(address => address.ToChainAddress())]);
    }

    /// <inheritdoc/>
    public SanctionCheck CheckSanctions(ICase @case, IReadOnlyCollection<EthereumAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        return CheckSanctions(@case, [.. addresses.Select(address => address.ToChainAddress())]);
    }

    /// <inheritdoc/>
    public SanctionCheck CheckSanctions(ICase @case, IReadOnlyCollection<ChainAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(addresses);
        // The lists are asked once for every address; each hit keeps the list it was found on, so that a list
        // imported meanwhile is never named for it.
        var lookup = sanctions.FindMany([.. addresses.Where(IsSanctionChain).Distinct()]);
        var list = lookup.Lists.FirstOrDefault();
        var hits = lookup.Matches
            .SelectMany(pair => pair.Value.SelectMany(match => match.Hits
                .Where(hit => SanctionCurrency.On(pair.Key.Chain, hit.Currency))
                .Select(hit => new SanctionHit(pair.Key, hit, match.List))))
            .ToList();
        var done = new SanctionCheck(list, [], hits.Count) { Unusable = lookup.Unusable, Missing = lookup.Missing };

        // Read first: the graph checks at every reading, and a write transaction would make the case save itself
        // again with nothing new (the review of round 5, I1).
        if (hits.Count == 0 || @case.Read(reader => FreshSanctions(LabelStore.Read(reader), hits)).Count == 0)
        {
            return done;
        }

        return @case.Write(transaction =>
        {
            // Only the case's own checks count: a copy from another case leaves this case's own hit to be sealed.
            var drafts = FreshSanctions(LabelStore.Read(transaction), hits);
            if (drafts.Count == 0)
            {
                return done;
            }

            var used = drafts.Select(found => found.List).DistinctBy(each => each.Sha256).ToList();
            var names = string.Join(" och ", used.Select(each => each.Name).Distinct(StringComparer.Ordinal));
            var journal = transaction.AppendJournal(new JournalDraft(
                AttributionJournalKinds.SanctionsMatched,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} adresser på {1} ({2})",
                    drafts.Count,
                    names,
                    string.Join(", ", used.Select(each => $"{each.Name} {each.Version}"))),
                JsonSerializer.Serialize(new
                {
                    list = names,
                    hits = drafts.Select(found => new
                    {
                        address = found.Address.Value,
                        list = found.List.Name,
                        uid = found.Hit.EntryUid,
                        sha256 = found.List.Sha256,
                        published = found.List.PublishDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    }),
                })));
            var added = drafts
                .Select(found => LabelStore.Seal(
                    transaction, SanctionLabel(found.List, found.Hit, found.Address, found.Origin, journal), journal))
                .ToList();
            return done with { Added = added, AlreadyLabelled = hits.Count - drafts.Count };
        });
    }

    /// <summary>Says whether an address is on a chain the sanction lists are read for.</summary>
    /// <param name="address">The address with its chain.</param>
    /// <returns><see langword="true"/> for Bitcoin, Ethereum, TRON, Litecoin and BSC.</returns>
    private static bool IsSanctionChain(ChainAddress address) =>
        address.Chain is Chain.Bitcoin or Chain.Ethereum or Chain.Tron or Chain.Litecoin or Chain.Bsc;

    /// <summary>Gives the hits the case has not had from the same list, with their origins.</summary>
    /// <param name="stored">The case's labels.</param>
    /// <param name="hits">The hits.</param>
    /// <returns>The hits to label.</returns>
    private static List<SanctionDraft> FreshSanctions(StoredLabels stored, IReadOnlyList<SanctionHit> hits) =>
    [
        .. hits
            .Select(found => new SanctionDraft(
                found.Address, found.Hit, found.List, SanctionOriginId(found.List, found.Hit, found.Address)))
            .Where(found => !stored.Has(
                LabelStore.Key(
                    found.Origin, LabelSubject.ForAccount(found.Address), KindOf(found.Hit), found.Hit.EntryName),
                LabelSource.SanctionList)),
    ];

    /// <inheritdoc/>
    public OpenSourceCheck CheckOpenSources(ICase @case, IReadOnlyCollection<ChainAddress> addresses)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(addresses);
        var lookup = openSources.Find([.. addresses.Distinct()]);
        var hits = lookup.Matches.Values
            .SelectMany(matches => matches.SelectMany(match => match.Hits.Select(hit => (match.List, Hit: hit))))
            .ToList();
        var done = new OpenSourceCheck(lookup.Lists, [], hits.Count)
        {
            Unusable = lookup.Unusable,
            Missing = lookup.Missing,
        };
        if (hits.Count == 0 || @case.Read(reader => FreshOpenSources(LabelStore.Read(reader), hits)).Count == 0)
        {
            return done;
        }

        return @case.Write(transaction =>
        {
            var drafts = FreshOpenSources(LabelStore.Read(transaction), hits);
            if (drafts.Count == 0)
            {
                return done;
            }

            var used = drafts.Select(found => found.List).DistinctBy(each => each.Sha256).ToList();
            var journal = transaction.AppendJournal(new JournalDraft(
                AttributionJournalKinds.OpenSourcesMatched,
                string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} adresser i öppna källor ({1})",
                    drafts.Count,
                    string.Join(", ", used.Select(each => $"{each.Name} {each.Version}"))),
                JsonSerializer.Serialize(new
                {
                    hits = drafts.Select(found => new
                    {
                        address = found.Hit.Address.Value,
                        chain = found.Hit.Address.Chain.ToString(),
                        source = found.List.Name,
                        sha256 = found.List.Sha256,
                        name = found.Hit.Name,
                    }),
                })));
            var added = drafts
                .Select(found => LabelStore.Seal(
                    transaction, OpenSourceLabel(found.List, found.Hit, found.Origin, journal), journal))
                .ToList();
            return done with { Added = added, AlreadyLabelled = hits.Count - drafts.Count };
        });
    }

    /// <summary>Gives an open source's hits the case has not had from the same file, with their origins.</summary>
    /// <param name="stored">The case's labels.</param>
    /// <param name="hits">The hits.</param>
    /// <returns>The hits to label.</returns>
    private static List<(OpenSourceListInfo List, OpenSourceEntry Hit, string Origin)> FreshOpenSources(
        StoredLabels stored, IReadOnlyList<(OpenSourceListInfo List, OpenSourceEntry Hit)> hits) =>
    [
        .. hits
            .Select(found => (found.List, found.Hit, Origin: OpenSourceOriginId(found.List, found.Hit)))
            .Where(found => !stored.Has(
                LabelStore.Key(
                    found.Origin, LabelSubject.ForAccount(found.Hit.Address), found.Hit.Kind, found.Hit.Name),
                LabelSource.OpenSource)),
    ];

    /// <summary>Gives the stable origin of an open source's hit: the file's hash, the address and the name.</summary>
    /// <param name="list">The source.</param>
    /// <param name="hit">What it says.</param>
    /// <returns>The origin id.</returns>
    private static string OpenSourceOriginId(OpenSourceListInfo list, OpenSourceEntry hit) =>
        $"{OpenSourceLicenses.OriginPrefix}{list.Sha256}/{hit.Address.Chain}/{hit.Address.Value}/{hit.Name}";

    /// <summary>Gives the label for a hit in an open source.</summary>
    /// <param name="list">The source.</param>
    /// <param name="hit">What it says.</param>
    /// <param name="originId">The origin id.</param>
    /// <param name="journal">The journal entry that records it.</param>
    /// <returns>The label, not sealed yet.</returns>
    private static LabelEntry OpenSourceLabel(
        OpenSourceListInfo list, OpenSourceEntry hit, string originId, JournalEntry journal)
    {
        var reason = string.Format(
            CultureInfo.InvariantCulture,
            "Adressen står i {0} som {1}. Datamängden ({2}) bygger på rapporter, så det är en bedömning, " +
            "inte ett faktum.",
            list.Name,
            hit.Detail,
            list.License);
        var origin = new LabelOrigin(
            originId,
            LabelSource.OpenSource,
            LabelSource.OpenSource,
            list.Name,
            list.Version,
            journal.Operator,
            journal.Utc,
            reason,
            null);
        return new LabelEntry(0, LabelSubject.ForAccount(hit.Address), hit.Kind, hit.Name, origin, false, 0);
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
    private static string SanctionOriginId(SanctionListInfo list, SanctionedAddress hit, ChainAddress address) =>
        $"sanction/{list.Sha256}/{hit.EntryUid}/{address.Value}";

    /// <summary>
    /// Says why a hit is labelled, in the list's own terms: on OFAC's list the address is a
    /// <em>Digital Currency Address</em> with its code; on the EU's list, which has no such type, it was recognised by
    /// its form where it stood on the entry.
    /// </summary>
    /// <param name="list">The list.</param>
    /// <param name="hit">The entry's address.</param>
    /// <returns>The reason, in Swedish.</returns>
    private static string SanctionReason(SanctionListInfo list, SanctionedAddress hit)
    {
        var programs = hit.Programs.Count == 0 ? "inget program angivet" : string.Join(", ", hit.Programs);
        return list.Name == EuFsfParser.ListName
            ? string.Format(
                CultureInfo.InvariantCulture,
                "Adressen står på EU:s konsoliderade lista över finansiella sanktioner för {0} (EU-referens {1}, " +
                "{2}), i {3}. Listan har ingen egen sort för kryptoadresser; Chainsaw kände igen adressen på dess " +
                "form{4}",
                hit.EntryName,
                hit.EntryUid,
                programs,
                hit.ListedAs ?? "posten",
                hit.IsUncertain
                    ? ", men texten säger inte säkert vilken kedja den gäller, så det är en möjlig sanktion " +
                      "att kontrollera, aldrig en bekräftad."
                    : " och på valutan som står närmast före den.")
            : string.Format(
                CultureInfo.InvariantCulture,
                "Adressen står på listan som Digital Currency Address - {0} för {1} (uid {2}, {3}).",
                hit.Currency,
                hit.EntryName,
                hit.EntryUid,
                programs);
    }

    /// <summary>Gives the label for a hit on the list.</summary>
    /// <param name="list">The list.</param>
    /// <param name="hit">The entry's address.</param>
    /// <param name="address">The address.</param>
    /// <param name="originId">The origin id.</param>
    /// <param name="journal">The journal entry that records it.</param>
    /// <returns>The label, not sealed yet.</returns>
    private static LabelEntry SanctionLabel(
        SanctionListInfo list, SanctionedAddress hit, ChainAddress address, string originId, JournalEntry journal)
    {
        var origin = new LabelOrigin(
            originId,
            LabelSource.SanctionList,
            LabelSource.SanctionList,
            list.Name,
            list.Version,
            journal.Operator,
            journal.Utc,
            SanctionReason(list, hit),
            null);
        return new LabelEntry(0, LabelSubject.ForAccount(address), KindOf(hit), hit.EntryName, origin, false, 0);
    }

    /// <summary>
    /// Gives what a hit is labelled: a sanction, or only a possible one when the list does not say for certain which
    /// chain the address is on (principle 2: never a sanction on a guess).
    /// </summary>
    /// <param name="hit">The list's entry.</param>
    /// <returns>The label's kind.</returns>
    private static LabelKind KindOf(SanctionedAddress hit) =>
        hit.IsUncertain ? LabelKind.PossiblySanctioned : LabelKind.Sanctioned;

    /// <summary>A hit on a sanction list for an address.</summary>
    /// <param name="Address">The address with its chain.</param>
    /// <param name="Hit">The list's entry.</param>
    /// <param name="List">The list.</param>
    private sealed record SanctionHit(ChainAddress Address, SanctionedAddress Hit, SanctionListInfo List);

    /// <summary>A hit on a sanction list to label, with its origin.</summary>
    /// <param name="Address">The address with its chain.</param>
    /// <param name="Hit">The list's entry.</param>
    /// <param name="List">The list.</param>
    /// <param name="Origin">The label's origin id.</param>
    private sealed record SanctionDraft(
        ChainAddress Address, SanctionedAddress Hit, SanctionListInfo List, string Origin);
}
