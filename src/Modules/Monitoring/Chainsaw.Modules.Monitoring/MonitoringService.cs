using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// The watches of a case, sealed in it and checked through the chains' sources (see <see cref="IMonitoringService"/>).
/// </summary>
/// <param name="check">The check, the same the background process runs.</param>
/// <param name="gate">Whether lookups may go.</param>
/// <param name="bitcoin">Bitcoin's source.</param>
/// <param name="ethereum">Ethereum's source.</param>
/// <param name="clustering">The clustering, which says a cluster's addresses at each check.</param>
/// <param name="clock">The clock.</param>
internal sealed class MonitoringService(
    IWatchCheck check,
    IWatchGate gate,
    IBitcoinSource bitcoin,
    IEthereumSource ethereum,
    IClusteringService clustering,
    IClock clock) : IMonitoringService
{
    /// <summary>What a check says when a cluster has more addresses than one check reads.</summary>
    public const string TooManyAddressesText =
        "Klustret har {0} adresser; en kontroll läser de {1} första, så resten bevakas inte nu.";

    /// <summary>What a check says when a cluster's addresses could not be read.</summary>
    public const string NoClusterText = "Klustrets adresser kunde inte läsas; bara {0} kontrollerades.";

    /// <inheritdoc/>
    public IReadOnlyList<Watch> GetWatches(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(WatchStore.ReadWatches);
    }

    /// <inheritdoc/>
    public IReadOnlyList<WatchRecord> GetRecords(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(WatchStore.ReadRecords);
    }

    /// <inheritdoc/>
    public IReadOnlyList<WatchHit> GetHits(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(WatchStore.ReadHits);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, WatchChecked> GetChecked(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(WatchStore.ReadChecked);
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, long> GetSeen(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(WatchStore.ReadSeen);
    }

    /// <inheritdoc/>
    public void MarkSeen(ICase @case, string watchId, long through)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentException.ThrowIfNullOrWhiteSpace(watchId);
        @case.Write(transaction =>
        {
            WatchStore.WriteSeen(transaction, watchId, through);
            return true;
        });
    }

    /// <inheritdoc/>
    public Watch Add(ICase @case, WatchSubject subject, WatchFilter filter)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(filter);
        var checkedFilter = Checked(filter);
        var checkedSubject = Checked(subject, checkedFilter.Chain);
        var id = Guid.NewGuid().ToString("N");
        return @case.Write(transaction =>
        {
            var journal = transaction.AppendJournal(new JournalDraft(
                MonitoringJournalKinds.Added,
                $"Bevakning: {checkedSubject.Name} ({WatchTexts.Describe(checkedFilter)})",
                Details(id, checkedSubject, checkedFilter, null)));
            var watch = new Watch(
                id,
                checkedSubject,
                checkedFilter,
                WatchState.Active,
                null,
                journal.Operator,
                journal.Utc,
                journal.Utc);
            WatchStore.Seal(transaction, WatchAction.Added, watch, null, journal);
            return watch;
        });
    }

    /// <inheritdoc/>
    public Watch? Change(ICase @case, string id, WatchFilter filter)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentNullException.ThrowIfNull(filter);
        var checkedFilter = Checked(filter);
        return Update(@case, id, WatchAction.Changed, null, watch =>
        {
            if (watch.Chain != checkedFilter.Chain)
            {
                throw new ArgumentException("A watch stays on its chain.", nameof(filter));
            }

            return watch with { Filter = checkedFilter };
        });
    }

    /// <inheritdoc/>
    public Watch? SetPaused(ICase @case, string id, bool paused)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var action = paused ? WatchAction.Paused : WatchAction.Resumed;
        return Update(@case, id, action, null, watch => watch with
        {
            State = paused ? WatchState.Paused : WatchState.Active,
        });
    }

    /// <inheritdoc/>
    public bool Remove(ICase @case, string id, string? reason)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var why = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        if (why?.Length > WatchRules.MaximumTextLength)
        {
            throw new ArgumentException("The reason is too long.", nameof(reason));
        }

        return Update(@case, id, WatchAction.Removed, why, watch => watch with { State = WatchState.Removed })
            is not null;
    }

    /// <inheritdoc/>
    public WatchGate Gate(Chain chain) => gate.For(chain);

    /// <inheritdoc/>
    public async Task<long?> GetHeightAsync(ICase @case, Chain chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        if (gate.For(chain) != WatchGate.Open)
        {
            return null;
        }

        switch (chain)
        {
            case Chain.Bitcoin:
                var tip = await bitcoin.GetTipHeightAsync(@case, cancellationToken).ConfigureAwait(false);
                return tip.Status == LookupStatus.Fetched ? tip.Value?.Height : null;
            case Chain.Ethereum:
                var height = await ethereum.GetTipHeightAsync(@case, cancellationToken).ConfigureAwait(false);
                return height.Status == LookupStatus.Fetched ? height.Value?.Height : null;
            default:
                return null;
        }
    }

    /// <inheritdoc/>
    public async Task<WatchRun> CheckAsync(
        ICase @case, Chain chain, long height, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var open = gate.For(chain);
        if (open != WatchGate.Open || FeedFor(@case, chain) is not { } feed)
        {
            return new WatchRun(chain, open, height, [], clock.UtcNow);
        }

        var watches = GetWatches(@case).Where(w => w.Chain == chain && w.State == WatchState.Active).ToList();
        Start(@case, watches.Where(watch => watch.StartHeight is null).ToList(), height);
        var known = GetHits(@case).Select(hit => WatchCodec.Key(hit.WatchId, hit.Match))
            .ToHashSet(StringComparer.Ordinal);
        var checkedBefore = GetChecked(@case);
        var outcomes = new List<WatchOutcome>();
        foreach (var watch in watches.Where(watch => watch.StartHeight is not null))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = watch.StartHeight!.Value;
            var last = checkedBefore.TryGetValue(watch.Id, out var before) ? before.Height : start;
            var (addresses, notes) = AddressesOf(@case, watch);
            var result = await check.CheckAsync(
                addresses, watch.Filter, feed, start, Math.Max(start, last - WatchRules.Lookback), cancellationToken)
                .ConfigureAwait(false);
            var fresh = result.Matches.Where(match => !known.Contains(WatchCodec.Key(watch.Id, match))).ToList();
            var problems = notes.Concat(result.Problems).ToList();
            var hits = Seal(@case, watch, fresh, result, problems, height);
            outcomes.Add(new WatchOutcome(watch.Id, result.Status, hits, problems));
        }

        return new WatchRun(chain, open, height, outcomes, clock.UtcNow);
    }

    /// <summary>Gives the feed that reads a chain through the case's sources.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="chain">The chain.</param>
    /// <returns>The feed, or <see langword="null"/> for a chain that cannot be watched yet.</returns>
    private IWatchFeed? FeedFor(ICase @case, Chain chain) => chain switch
    {
        Chain.Bitcoin => new BitcoinCaseFeed(bitcoin, @case),
        Chain.Ethereum => new EthereumCaseFeed(ethereum, @case),
        _ => null,
    };

    /// <summary>
    /// Gives a watch's addresses now: the address, or the cluster's addresses as the clustering puts them, the anchor
    /// first and at most <see cref="WatchRules.MaximumAddresses"/>.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="watch">The watch.</param>
    /// <returns>The addresses, and what to say about them.</returns>
    private (IReadOnlyList<ChainAddress> Addresses, IReadOnlyList<string> Notes) AddressesOf(ICase @case, Watch watch)
    {
        var anchor = watch.Subject.Anchor;
        if (watch.Subject.Kind != WatchSubjectKind.Cluster || !BitcoinAddress.TryParse(anchor.Value, out var start))
        {
            return ([anchor], []);
        }

        List<ChainAddress> all;
        try
        {
            all = [.. clustering.GetCluster(@case, start).Addresses
                .Select(address => new ChainAddress(Chain.Bitcoin, address.Value))];
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or CaseException))
        {
            return ([anchor], [string.Format(CultureInfo.InvariantCulture, NoClusterText, anchor.Value)]);
        }

        all.Remove(anchor);
        all.Insert(0, anchor);
        if (all.Count <= WatchRules.MaximumAddresses)
        {
            return (all, []);
        }

        var note = string.Format(
            CultureInfo.InvariantCulture, TooManyAddressesText, all.Count, WatchRules.MaximumAddresses);
        return (all[..WatchRules.MaximumAddresses], [note]);
    }

    /// <summary>Seals the block each new watch starts from, one record and journal line each.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="watches">The watches without a start.</param>
    /// <param name="height">The chain's height.</param>
    private void Start(ICase @case, IReadOnlyList<Watch> watches, long height)
    {
        if (watches.Count == 0)
        {
            return;
        }

        @case.Write(transaction =>
        {
            foreach (var watch in watches)
            {
                var started = watch with { StartHeight = height };
                var journal = transaction.AppendJournal(new JournalDraft(
                    MonitoringJournalKinds.Started,
                    $"Bevakningen av {watch.Subject.Name} börjar efter block {Number(height)}",
                    JsonSerializer.Serialize(new { watch = watch.Id, start = height })));
                WatchStore.Seal(transaction, WatchAction.Started, started with { ChangedUtc = journal.Utc }, null,
                    journal);
                WatchStore.WriteChecked(
                    transaction, watch.Id, new WatchChecked(clock.UtcNow, height, WatchFeedStatus.Read, null));
            }

            return true;
        });
    }

    /// <summary>
    /// Seals a check's new hits under one journal line, and notes when the watch was checked; a check without new hits
    /// writes no journal line.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="watch">The watch.</param>
    /// <param name="fresh">The new matches.</param>
    /// <param name="result">The check.</param>
    /// <param name="problems">What to say about it.</param>
    /// <param name="height">The chain's height.</param>
    /// <returns>The sealed hits.</returns>
    private IReadOnlyList<WatchHit> Seal(
        ICase @case,
        Watch watch,
        IReadOnlyList<WatchMatch> fresh,
        WatchCheckResult result,
        IReadOnlyList<string> problems,
        long height)
    {
        var problem = problems.Count == 0 ? null : string.Join(' ', problems);
        var through = Math.Max(height, result.Highest ?? height);
        return @case.Write(transaction =>
        {
            // Every read address or not, the height is only moved on when every address was read, so that a
            // failed lookup is read again from where it was.
            var previous = WatchStore.ReadChecked(transaction).GetValueOrDefault(watch.Id);
            var kept = result.Status == WatchFeedStatus.Read ? through : previous?.Height ?? watch.StartHeight!.Value;
            WatchStore.WriteChecked(
                transaction, watch.Id, new WatchChecked(clock.UtcNow, kept, result.Status, problem));
            if (fresh.Count == 0)
            {
                return (IReadOnlyList<WatchHit>)[];
            }

            var journal = transaction.AppendJournal(new JournalDraft(
                MonitoringJournalKinds.Hit,
                $"Bevakningen av {watch.Subject.Name} slog till: {WatchTexts.Hits(fresh)}",
                JsonSerializer.Serialize(new
                {
                    watch = watch.Id,
                    hits = fresh.Select(match => new
                    {
                        tx = match.Transaction,
                        block = match.Block.Height,
                        hash = match.Block.Hash,
                        direction = match.Direction.ToString(),
                        amount = match.Amount.ToString(),
                    }),
                    origins = result.Origins,
                })));
            return (IReadOnlyList<WatchHit>)[.. fresh.Select(match =>
                WatchStore.SealHit(transaction, watch, match, result.Origins, journal))];
        });
    }

    /// <summary>Seals a change of a watch in force, with a line in the journal.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The watch.</param>
    /// <param name="action">What the change is.</param>
    /// <param name="reason">Why, for a removal.</param>
    /// <param name="change">Makes the changed watch.</param>
    /// <returns>The changed watch, or <see langword="null"/> when there is no such watch in force.</returns>
    private static Watch? Update(ICase @case, string id, WatchAction action, string? reason, Func<Watch, Watch> change)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return @case.Write(transaction =>
        {
            if (WatchStore.ReadWatches(transaction).FirstOrDefault(watch => watch.Id == id) is not { } watch)
            {
                return null;
            }

            var changed = change(watch);
            var journal = transaction.AppendJournal(new JournalDraft(
                KindOf(action),
                $"{WatchTexts.Verb(action)}: {watch.Subject.Name} ({WatchTexts.Describe(changed.Filter)})",
                Details(watch.Id, watch.Subject, changed.Filter, reason)));
            changed = changed with { ChangedUtc = journal.Utc };
            WatchStore.Seal(transaction, action, changed, reason, journal);
            return changed;
        });
    }

    /// <summary>Gives the journal code of a record.</summary>
    /// <param name="action">What the record does.</param>
    /// <returns>The code.</returns>
    private static string KindOf(WatchAction action) => action switch
    {
        WatchAction.Changed => MonitoringJournalKinds.Changed,
        WatchAction.Paused => MonitoringJournalKinds.Paused,
        WatchAction.Resumed => MonitoringJournalKinds.Resumed,
        WatchAction.Removed => MonitoringJournalKinds.Removed,
        WatchAction.Started => MonitoringJournalKinds.Started,
        _ => MonitoringJournalKinds.Added,
    };

    /// <summary>Gives a journal line's details: the watch's node and filter, as sealed.</summary>
    /// <param name="id">The watch.</param>
    /// <param name="subject">The node.</param>
    /// <param name="filter">The filter.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <returns>The JSON.</returns>
    private static string Details(string id, WatchSubject subject, WatchFilter filter, string? reason) =>
        JsonSerializer.Serialize(new
        {
            watch = id,
            subject = JsonDocument.Parse(WatchCodec.Encode(subject)).RootElement,
            filter = JsonDocument.Parse(WatchCodec.Encode(filter)).RootElement,
            reason,
        });

    /// <summary>Checks a filter.</summary>
    /// <param name="filter">The filter.</param>
    /// <returns>The filter in its one form.</returns>
    /// <exception cref="ArgumentException">It is wrong.</exception>
    private static WatchFilter Checked(WatchFilter filter)
    {
        if (filter.Chain is not (Chain.Bitcoin or Chain.Ethereum))
        {
            throw new ArgumentException("Only Bitcoin and Ethereum can be watched yet.", nameof(filter));
        }

        var valid = filter.Receivers.Concat(filter.Senders).All(party => IsAddress(filter.Chain, party.Value));
        return valid
            ? filter.Checked()
            : throw new ArgumentException("A counterparty is not an address on the chain.", nameof(filter));
    }

    /// <summary>Says whether a text is an address on a chain.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="value">The text.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool IsAddress(Chain chain, string value) => chain == Chain.Bitcoin
        ? BitcoinAddress.TryParse(value, out _)
        : EthereumAddress.TryParse(value, out _);

    /// <summary>Checks a node.</summary>
    /// <param name="subject">The node.</param>
    /// <param name="chain">The filter's chain.</param>
    /// <returns>The node with its name trimmed.</returns>
    /// <exception cref="ArgumentException">It is wrong.</exception>
    private static WatchSubject Checked(WatchSubject subject, Chain chain)
    {
        var name = subject.Name?.Trim() ?? string.Empty;
        if (name.Length == 0 || name.Length > WatchRules.MaximumTextLength || !Enum.IsDefined(subject.Kind))
        {
            throw new ArgumentException("The watched node needs a name, at most 1 000 characters.", nameof(subject));
        }

        if (subject.Anchor.Chain != chain || (subject.Kind == WatchSubjectKind.Cluster && chain != Chain.Bitcoin))
        {
            throw new ArgumentException("The node is on another chain, or a cluster outside Bitcoin.", nameof(subject));
        }

        return IsAddress(chain, subject.Anchor.Value)
            ? subject with { Name = name }
            : throw new ArgumentException("The watched node is not an address on its chain.", nameof(subject));
    }

    /// <summary>Writes a block height with spaces between thousands, as the journal does.</summary>
    /// <param name="height">The height.</param>
    /// <returns>For example <c>921 344</c>.</returns>
    private static string Number(long height) => WatchTexts.Number(height);
}
