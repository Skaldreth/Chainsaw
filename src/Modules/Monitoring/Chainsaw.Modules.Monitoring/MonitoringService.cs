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
/// <param name="feeds">The feeds that read a chain through the case's sources.</param>
/// <param name="clock">The clock.</param>
internal sealed class MonitoringService(
    IWatchCheck check,
    IWatchGate gate,
    IBitcoinSource bitcoin,
    IEthereumSource ethereum,
    IClusteringService clustering,
    IWatchFeeds feeds,
    IClock clock) : IMonitoringService
{
    /// <summary>What a check says when a cluster has more addresses than one check reads.</summary>
    public const string TooManyAddressesText =
        "Klustret har {0} adresser; en kontroll läser de {1} första, så resten bevakas inte nu.";

    /// <summary>The journal line when Chainsaw's own lookup gave a hit the background said it saw.</summary>
    public const string TipConfirmedText =
        "Bevakningen av {0} slog till (bakgrundens träff, kontrollerad mot kedjan): {1}";

    /// <summary>The journal line when the case already had the hit a tip from the background was about.</summary>
    public const string TipKnownText = "Bakgrundens träff för bevakningen av {0} fanns redan i ärendet: {1}";

    /// <summary>The journal line when a tip from the background did not match the chain.</summary>
    public const string TipRejectedText =
        "Bakgrundens träff för bevakningen av {0} stämde inte med kedjan och förseglades inte: {1}";

    /// <summary>The journal line when a tip from the background was not checked: its watch changed first.</summary>
    public const string TipDroppedText =
        "Bakgrundens träff för bevakningen av {0} kontrollerades inte, bevakningen ändrades eller togs bort: {1}";

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
    public IReadOnlyList<WatchTip> GetTips(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(WatchStore.ReadTips);
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
    public DateTimeOffset? GetLastSeen(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return @case.Read(WatchStore.ReadLastSeen);
    }

    /// <inheritdoc/>
    public void MarkSeen(ICase @case, string watchId, long through)
    {
        ArgumentNullException.ThrowIfNull(@case);
        ArgumentException.ThrowIfNullOrWhiteSpace(watchId);
        @case.Write(transaction =>
        {
            WatchStore.WriteSeen(transaction, watchId, through, clock.UtcNow);
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
        RefuseOwn(@case, checkedSubject, checkedFilter);
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

        // The clustering is read outside the case's write, which allows no other read inside it.
        if (GetWatches(@case).FirstOrDefault(watch => watch.Id == id) is { } current)
        {
            RefuseOwn(@case, current.Subject, checkedFilter);
        }

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

        // Its waiting tips are closed unchecked in the same write: no check of its chain may come again to close them.
        return Update(@case, id, WatchAction.Removed, why, watch => watch with { State = WatchState.Removed },
            (transaction, removed) =>
            {
                foreach (var tip in WatchStore.ReadTips(transaction)
                    .Where(tip => tip.WatchId == removed.Id && tip.State == WatchTipState.Pending).ToList())
                {
                    CloseTip(transaction, tip, removed, null);
                }
            }) is not null;
    }

    /// <inheritdoc/>
    public WatchGate Gate(Chain chain) => gate.For(chain);

    /// <inheritdoc/>
    public async Task<long?> GetHeightAsync(ICase @case, Chain chain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        return gate.For(chain) == WatchGate.Open
            ? await WatchSchedule.HeightAsync(bitcoin, ethereum, @case, chain, cancellationToken).ConfigureAwait(false)
            : null;
    }

    /// <inheritdoc/>
    public async Task<WatchRun> CheckAsync(
        ICase @case, Chain chain, long height, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var open = gate.For(chain);
        if (open != WatchGate.Open || feeds.For(@case, chain) is not { } feed)
        {
            return new WatchRun(chain, open, height, [], clock.UtcNow);
        }

        var watches = GetWatches(@case).Where(w => w.Chain == chain && w.State == WatchState.Active).ToList();
        Start(@case, watches.Where(watch => watch.StartHeight is null).ToList(), height);
        var outcomes = await CheckTipsAsync(@case, chain, cancellationToken).ConfigureAwait(false);
        var known = GetHits(@case).Select(hit => WatchSchedule.Key(hit.WatchId, hit.Match))
            .ToHashSet(StringComparer.Ordinal);
        var checkedBefore = GetChecked(@case);
        var now = clock.UtcNow;
        var due = watches.Where(watch => watch.StartHeight is not null
            && IsDue(watch, checkedBefore.GetValueOrDefault(watch.Id), height, now));
        foreach (var watch in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var start = watch.StartHeight!.Value;
            var (addresses, notes) = AddressesOf(@case, watch);
            var result = await check.CheckAsync(
                addresses, watch.Filter, feed, start,
                WatchSchedule.DownTo(start, checkedBefore.GetValueOrDefault(watch.Id)), cancellationToken)
                .ConfigureAwait(false);
            var fresh = result.Matches.Where(match => !known.Contains(WatchSchedule.Key(watch.Id, match))).ToList();
            var problems = notes.Concat(result.Problems).ToList();
            var hits = Seal(@case, watch, fresh, result, problems, height, now);
            outcomes.Add(new WatchOutcome(watch.Id, result.Status, hits, problems));
        }

        return new WatchRun(chain, open, height, outcomes, clock.UtcNow);
    }

    /// <summary>
    /// Checks the background's waiting tips on a chain against the chain (decided 2026-11-08): each tip of an active
    /// watch has its transaction looked up through the case's own source, and the watch's check runs on that answer. A
    /// tip the answer gives is sealed as a hit from Chainsaw's own answer; one it does not give, or whose transaction
    /// is not on the chain, is closed with a line in the journal and no hit; one of a watch taken back or changed is
    /// closed unchecked; and one that cannot be looked up now, or of a paused watch, waits for the next check.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="chain">The chain, whose gate is open.</param>
    /// <param name="cancellationToken">Stops the lookups.</param>
    /// <returns>One outcome per watch that got a hit from its tips.</returns>
    private async Task<List<WatchOutcome>> CheckTipsAsync(
        ICase @case, Chain chain, CancellationToken cancellationToken)
    {
        var outcomes = new List<WatchOutcome>();
        var waiting = GetTips(@case).Where(tip => tip.Chain == chain && tip.State == WatchTipState.Pending).ToList();
        if (waiting.Count == 0)
        {
            return outcomes;
        }

        var watches = @case.Read(WatchStore.ReadRecords).GroupBy(record => record.Watch.Id)
            .ToDictionary(group => group.Key, group => group.Last().Watch, StringComparer.Ordinal);
        foreach (var tip in waiting)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var watch = watches.GetValueOrDefault(tip.WatchId);
            if (watch is { State: WatchState.Paused })
            {
                continue;
            }

            var filter = @case.Read(reader => WatchStore.ReadTipFilter(reader, tip));
            if (watch is not { State: WatchState.Active, StartHeight: { } start }
                || WatchCodec.Encode(watch.Filter with { Pace = WatchPace.EveryBlock })
                    != WatchCodec.Encode(filter with { Pace = WatchPace.EveryBlock }))
            {
                CloseTip(@case, tip, watch, null);
                continue;
            }

            var finding = await TipLookup.ReadAsync(
                check, bitcoin, ethereum, @case, tip, AddressesOf(@case, watch).Addresses.ToHashSet(), filter, start,
                cancellationToken).ConfigureAwait(false);
            if (finding.Verdict == TipVerdict.Wait)
            {
                continue;
            }

            if (CloseTip(@case, tip, watch, finding) is { } hit)
            {
                outcomes.Add(new WatchOutcome(watch.Id, WatchFeedStatus.Read, [hit], []));
            }
        }

        return outcomes;
    }

    /// <summary>
    /// Closes a tip in one write: sealed as a hit from Chainsaw's own answer when the finding confirms it (unless the
    /// case has the hit already), closed as not matching the chain when it rejects it, and as not checked when there is
    /// no finding. A tip closed meanwhile is left as it is.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="tip">The tip.</param>
    /// <param name="watch">Its watch, or <see langword="null"/> when there is none.</param>
    /// <param name="finding">What Chainsaw's lookup found, or <see langword="null"/> when it was not checked.</param>
    /// <returns>The hit sealed now, or <see langword="null"/>.</returns>
    private WatchHit? CloseTip(ICase @case, WatchTip tip, Watch? watch, TipFinding? finding) =>
        @case.Write(transaction => CloseTip(transaction, tip, watch, finding));

    /// <summary>Closes a tip in the case's write, as the method that takes the case says.</summary>
    /// <param name="transaction">The case's write.</param>
    /// <param name="tip">The tip.</param>
    /// <param name="watch">Its watch, or <see langword="null"/>.</param>
    /// <param name="finding">What Chainsaw's own lookup found, or <see langword="null"/>.</param>
    /// <returns>The hit sealed now, or <see langword="null"/>.</returns>
    private static WatchHit? CloseTip(ICaseTransaction transaction, WatchTip tip, Watch? watch, TipFinding? finding)
    {
        if (WatchStore.ReadTips(transaction).FirstOrDefault(each => each.Sequence == tip.Sequence) is not
            { State: WatchTipState.Pending })
        {
            return null;
        }

        // A watch removed, paused or changed while its tip was looked up gets nothing from the lookup: the next check
        // looks at the tip again as the watch is then.
        var now = WatchStore.ReadWatches(transaction).FirstOrDefault(each => each.Id == tip.WatchId);
        if (finding is not null && (now is null || watch is null || now.State != WatchState.Active
            || WatchCodec.Encode(now.Filter) != WatchCodec.Encode(watch.Filter)))
        {
            return null;
        }

        var name = watch?.Subject.Name ?? tip.WatchId;
        var claimed = WatchTexts.Hits([tip.Match]);
        if (finding is { Verdict: TipVerdict.Confirmed, Match: { } match } && watch is not null)
        {
            var key = WatchSchedule.Key(watch.Id, match);
            var known = WatchStore.ReadHits(transaction)
                .FirstOrDefault(hit => WatchSchedule.Key(hit.WatchId, hit.Match) == key);
            if (known is not null)
            {
                // The open check sealed it first: the tip points at that hit, and nothing is told twice.
                WatchStore.CloseTip(transaction, tip, WatchTipState.Confirmed, known.Sequence, Journal(
                    transaction, MonitoringJournalKinds.Tip, string.Format(
                        CultureInfo.InvariantCulture, TipKnownText, name, claimed), tip, finding));
                return null;
            }

            var journal = Journal(transaction, MonitoringJournalKinds.Hit, string.Format(
                CultureInfo.InvariantCulture, TipConfirmedText, name, WatchTexts.Hits([match])), tip, finding);
            var sealedHit = WatchStore.SealHit(transaction, watch, match, finding.Origins, journal);
            WatchStore.CloseTip(transaction, tip, WatchTipState.Confirmed, sealedHit.Sequence, journal);
            return sealedHit;
        }

        var (state, kind, text) = finding is null
            ? (WatchTipState.Dropped, MonitoringJournalKinds.TipDropped, TipDroppedText)
            : (WatchTipState.Rejected, MonitoringJournalKinds.TipRejected, TipRejectedText);
        WatchStore.CloseTip(transaction, tip, state, null, Journal(
            transaction, kind, string.Format(CultureInfo.InvariantCulture, text, name, claimed), tip, finding));
        return null;
    }

    /// <summary>Writes the journal line that closes a tip, with the tip and Chainsaw's own responses.</summary>
    /// <param name="transaction">The case's write.</param>
    /// <param name="kind">The journal code.</param>
    /// <param name="summary">The line.</param>
    /// <param name="tip">The tip.</param>
    /// <param name="finding">What Chainsaw's own lookup found, or <see langword="null"/>.</param>
    /// <returns>The journal entry.</returns>
    private static JournalEntry Journal(
        ICaseTransaction transaction, string kind, string summary, WatchTip tip, TipFinding? finding) =>
        transaction.AppendJournal(new JournalDraft(kind, summary, JsonSerializer.Serialize(new
        {
            watch = tip.WatchId,
            tip = tip.Sequence,
            entry = tip.Entry,
            tx = tip.Match.Transaction,
            verdict = finding?.Verdict.ToString(),
            origins = finding?.Origins ?? [],
            backgroundOrigins = tip.Origins,
        })));

    /// <summary>
    /// Says whether a watch is due: a new block has come since it was last checked, and its pace's time has passed
    /// since then. A watch never checked, or whose last check failed or could not ask, is read again.
    /// </summary>
    /// <param name="watch">The watch.</param>
    /// <param name="last">When it was last checked, or <see langword="null"/>.</param>
    /// <param name="height">The chain's height now.</param>
    /// <param name="now">Now.</param>
    /// <returns><see langword="true"/> when it is checked now.</returns>
    public static bool IsDue(Watch watch, WatchChecked? last, long height, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(watch);
        return WatchSchedule.IsDue(watch.Filter.Pace, last, height, now);
    }

    /// <summary>
    /// Gives a watch's addresses now: the address, or the cluster's addresses as the clustering puts them, the anchor
    /// first and at most <see cref="WatchRules.MaximumAddresses"/>.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="watch">The watch.</param>
    /// <returns>The addresses, and what to say about them.</returns>
    public (IReadOnlyList<ChainAddress> Addresses, IReadOnlyList<string> Notes) AddressesOf(ICase @case, Watch watch)
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
                    $"Bevakningen av {watch.Subject.Name} börjar efter block {WatchTexts.Number(height)}",
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
    /// <param name="started">When the check started, which is when it counts as made for the pace.</param>
    /// <returns>The sealed hits.</returns>
    private IReadOnlyList<WatchHit> Seal(
        ICase @case,
        Watch watch,
        IReadOnlyList<WatchMatch> fresh,
        WatchCheckResult result,
        IReadOnlyList<string> problems,
        long height,
        DateTimeOffset started)
    {
        return @case.Write(transaction =>
        {
            // The height is only moved on when every address was read back to where the check was, so that a failed,
            // partial or not asked lookup is read again from the same place.
            // A watch removed, paused or changed while it was checked gets nothing sealed from the check: the next
            // check reads it again as it is now.
            var now = WatchStore.ReadWatches(transaction).FirstOrDefault(each => each.Id == watch.Id);
            if (now is null || now.State != WatchState.Active
                || WatchCodec.Encode(now.Filter) != WatchCodec.Encode(watch.Filter))
            {
                return (IReadOnlyList<WatchHit>)[];
            }

            var previous = WatchStore.ReadChecked(transaction).GetValueOrDefault(watch.Id)?.Height
                ?? watch.StartHeight!.Value;
            WatchStore.WriteChecked(
                transaction, watch.Id, WatchSchedule.After(previous, result, problems, height, started));
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
    /// <param name="then">What else the change writes, in the same write, or <see langword="null"/>.</param>
    /// <returns>The changed watch, or <see langword="null"/> when there is no such watch in force.</returns>
    private static Watch? Update(
        ICase @case,
        string id,
        WatchAction action,
        string? reason,
        Func<Watch, Watch> change,
        Action<ICaseTransaction, Watch>? then = null)
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
            then?.Invoke(transaction, changed);
            return changed;
        });
    }

    /// <summary>
    /// Refuses counterparties that are the watched node itself: its address, or for a cluster any address the
    /// clustering puts in it now, since money between them is never money in or out.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="subject">The node.</param>
    /// <param name="filter">The filter.</param>
    /// <exception cref="ArgumentException">A counterparty is the node.</exception>
    private void RefuseOwn(ICase @case, WatchSubject subject, WatchFilter filter)
    {
        var parties = filter.Receivers.Concat(filter.Senders).ToHashSet();
        if (parties.Count == 0)
        {
            return;
        }

        IEnumerable<ChainAddress> own = [subject.Anchor];
        if (subject.Kind == WatchSubjectKind.Cluster && BitcoinAddress.TryParse(subject.Anchor.Value, out var start))
        {
            own = clustering.GetCluster(@case, start).Addresses
                .Select(member => new ChainAddress(Chain.Bitcoin, member.Value));
        }

        if (own.Any(parties.Contains))
        {
            throw new ArgumentException("A counterparty is the watched node itself.", nameof(filter));
        }
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
}
