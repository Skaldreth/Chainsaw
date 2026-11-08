using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// The case's side of the background watching (see <see cref="IBackgroundWatches"/>): what the plan may know of a
/// case, and bringing the queue's entries into it with journal and hash. The hits an entry carries come in as tips
/// (<see cref="WatchTip"/>), never as hits: whoever can write the queue can also write the responses an entry carries,
/// so only Chainsaw's own lookup makes a hit (<see cref="MonitoringService"/>).
/// </summary>
/// <param name="service">The watches, which say a cluster's addresses as each check does.</param>
/// <param name="queue">The queue.</param>
/// <param name="transfer">Seals the raw responses again in the case.</param>
/// <param name="heights">How far the case's own lookups have seen each chain, which bounds the background.</param>
/// <param name="clock">The clock.</param>
internal sealed class BackgroundWatches(
    MonitoringService service, IWatchQueue queue, IResponseTransfer transfer, ICaseHeights heights, IClock clock)
    : IBackgroundWatches
{
    /// <summary>The journal line when entries were brought in.</summary>
    public const string ImportedText =
        "Bakgrundens bevakning förd in: {0} kontroller, {1} hämtade svar, {2} träffar som väntar på kontroll";

    /// <summary>What is said when an entry cannot be read.</summary>
    public const string UnreadableText =
        "En kontroll från bakgrunden kunde inte läsas och lades åt sidan; den förs inte in.";

    /// <summary>The journal line when the background says a watch has hit: a tip, until Chainsaw checked it.</summary>
    public const string TipText =
        "Bakgrunden säger att bevakningen av {0} slog till: {1}; väntar på kontroll mot kedjan";

    /// <summary>The chains the background can watch.</summary>
    private static readonly IReadOnlySet<Chain> Watchable = new HashSet<Chain> { Chain.Bitcoin, Chain.Ethereum };

    /// <inheritdoc/>
    public BackgroundCase Describe(ICase @case, DateTimeOffset utc)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var checkedAt = service.GetChecked(@case);
        var hits = service.GetHits(@case).Select(hit => (hit.WatchId, hit.Match))
            .Concat(service.GetTips(@case).Select(tip => (tip.WatchId, tip.Match)))
            .ToList();
        var watches = service.GetWatches(@case)
            .Where(watch => watch.State == WatchState.Active && Watchable.Contains(watch.Chain))
            .Select(watch =>
            {
                var last = checkedAt.GetValueOrDefault(watch.Id);
                var lookBack = (last?.Height ?? watch.StartHeight ?? 0) - WatchRules.Lookback;
                return new BackgroundWatch(
                    watch.Id, service.AddressesOf(@case, watch).Addresses, watch.Filter, watch.StartHeight, last)
                {
                    Known = [.. hits.Where(hit => hit.WatchId == watch.Id && hit.Match.Block.Height > lookBack)
                        .Select(hit => WatchSchedule.Key(hit.WatchId, hit.Match)).Distinct(StringComparer.Ordinal)],
                };
            })
            .ToList();
        return new BackgroundCase(@case.Info.Id, watches, utc);
    }

    /// <inheritdoc/>
    public BackgroundImport Import(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var caseId = @case.Info.Id;
        var done = new List<Guid>();
        var tips = new List<WatchTip>();
        var problems = new List<string>();
        var responses = 0;
        var entries = 0;
        IReadOnlyList<Guid> ids;
        try
        {
            ids = queue.List(caseId);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return BackgroundImport.None;
        }

        foreach (var id in ids)
        {
            QueuedCheck? entry;
            try
            {
                entry = queue.Read(caseId, id);
            }
            catch (InvalidDataException)
            {
                problems.Add(UnreadableText);
                continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (entry is null || @case.Read(reader => Waits(reader, entry)))
            {
                continue;
            }

            try
            {
                var brought = @case.Write(transaction => Bring(transaction, entry));
                if (brought is { } some)
                {
                    entries++;
                    responses += entry.Responses.Count;
                    tips.AddRange(some);
                }
            }
            catch (ArgumentException)
            {
                problems.Add(UnreadableText);
                try
                {
                    queue.SetAside(caseId, id);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // The file is held just now (a scanner); it is set aside at the next round.
                }

                continue;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The entry's file is held just now; it is brought in at the next round.
                continue;
            }

            done.Add(id);
        }

        if (entries > 0)
        {
            @case.AppendJournal(new JournalDraft(
                MonitoringJournalKinds.Imported,
                string.Format(CultureInfo.InvariantCulture, ImportedText, entries, responses, tips.Count),
                JsonSerializer.Serialize(new { entries = done, fetchedBy = BackgroundNames.Fetcher.Name })));
        }

        if (done.Count > 0)
        {
            // The entries go only once the case is saved: until then a crash leaves them to be brought in again,
            // which the ids in the case make harmless.
            @case.Save();
            foreach (var id in done)
            {
                try
                {
                    queue.Remove(caseId, id);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Left for the next round, which finds it already in the case and removes it.
                }
            }
        }

        return new BackgroundImport(entries, responses, tips, problems);
    }

    /// <summary>Brings one entry into the case in one write.</summary>
    /// <param name="transaction">The case's write.</param>
    /// <param name="entry">The entry.</param>
    /// <returns>The tips sealed, or <see langword="null"/> when the entry was already in the case.</returns>
    private List<WatchTip>? Bring(ICaseTransaction transaction, QueuedCheck entry)
    {
        var key = entry.Id.ToString("N");
        if (WasImported(transaction, key))
        {
            return null;
        }

        // Everything the background fetched is sealed in the case (principle 1), also for a watch changed since.
        var sequences = new Dictionary<long, long>();
        foreach (var response in entry.Responses)
        {
            sequences[response.Sequence] = transfer.Seal(transaction, response).Sequence;
        }

        MarkImported(transaction, key);
        var watch = WatchStore.ReadWatches(transaction).FirstOrDefault(each => each.Id == entry.WatchId);
        if (watch is null || watch.State != WatchState.Active || !SameFilter(watch.Filter, entry.Filter))
        {
            return [];
        }

        if (entry.Kind == QueuedKind.Started)
        {
            if (watch.StartHeight is null)
            {
                Start(transaction, watch, entry.Height, entry.Utc);
            }

            return [];
        }

        if (entry.Checked is null)
        {
            return [];
        }

        // A check whose start never came in (its entry was set aside) brings the start it counted from, so that its
        // hits are not lost. A check counted from another start than the case's (one the background set before a
        // crash, say) still stands: only money above the case's own start is a hit. A start beyond what the case has
        // seen waited in the queue (Waits) until the case had seen that far, so it is taken as the background set it.
        if ((watch.StartHeight ?? Start(transaction, watch, entry.Start, null)) is not { } start)
        {
            return [];
        }

        // How far it read, and when, never move the case's own progress: like a hit, they are only the background's
        // word, and the case's own height is no bound once the open Chainsaw has asked it again (as it does before it
        // brings in a start), so a forged entry could make the open check skip blocks it never read, or never come
        // (dated in a far future). The open check reads from where the case itself had read; what it finds again of
        // the background's hits closes their tips (MonitoringService).

        // A hit the case has, or a tip it already holds in any state, is never brought in again.
        var known = WatchStore.ReadHits(transaction).Select(hit => WatchSchedule.Key(hit.WatchId, hit.Match))
            .Concat(WatchStore.ReadTips(transaction).Select(tip => WatchSchedule.Key(tip.WatchId, tip.Match)))
            .ToHashSet(StringComparer.Ordinal);
        var fresh = entry.Matches
            .Where(match => match.Block.Height > Math.Max(start, entry.Start)
                && known.Add(WatchSchedule.Key(watch.Id, match)))
            .ToList();
        if (fresh.Count == 0)
        {
            return [];
        }

        IReadOnlyList<long> origins =
            [.. entry.Origins.Where(sequences.ContainsKey).Select(origin => sequences[origin])];
        var journal = transaction.AppendJournal(new JournalDraft(
            MonitoringJournalKinds.Tip,
            string.Format(CultureInfo.InvariantCulture, TipText, watch.Subject.Name, WatchTexts.Hits(fresh)),
            JsonSerializer.Serialize(new
            {
                watch = watch.Id,
                background = true,
                verified = false,
                entry = entry.Id,
                checkedUtc = WatchCodec.Text(entry.Utc),
                tips = fresh.Select(match => new
                {
                    tx = match.Transaction,
                    block = match.Block.Height,
                    hash = match.Block.Hash,
                    direction = match.Direction.ToString(),
                    amount = match.Amount.ToString(),
                }),
                origins,
            })));
        return [.. fresh.Select(match => WatchStore.SealTip(transaction, watch, entry.Id, match, origins, journal))];
    }

    /// <summary>
    /// Says whether an entry must wait in the queue: it would set the start of a watch that has none, from a start
    /// beyond what the case's own lookups have seen the chain, or while the watch is paused. The start is only the
    /// background's word, so it is not taken until the case has seen the chain that far (the runner asks the height and
    /// brings the queue in again before it checks); taken lower, money from before the watch was made would be hits,
    /// and dropped, the open check would start later and the background's hits would be lost.
    /// </summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="entry">The entry.</param>
    /// <returns><see langword="true"/> when it waits.</returns>
    private bool Waits(ICaseReader reader, QueuedCheck entry)
    {
        if (entry.Kind != QueuedKind.Started && entry.Checked is null)
        {
            return false;
        }

        var watch = WatchStore.ReadWatches(reader).FirstOrDefault(each => each.Id == entry.WatchId);
        if (watch is not { StartHeight: null, State: WatchState.Active or WatchState.Paused }
            || !SameFilter(watch.Filter, entry.Filter) || WasImported(reader, entry.Id.ToString("N")))
        {
            return false;
        }

        // A watch paused before it had a start keeps the entry until it is resumed: brought in now, the start would be
        // dropped, and the open check would start the watch only then, after the background's hits.
        var start = entry.Kind == QueuedKind.Started ? entry.Height : entry.Start;
        return watch.State == WatchState.Paused || !(heights.Known(reader, watch.Chain) >= start);
    }

    /// <summary>
    /// Says whether a watch still looks for what a check looked for: the same filter, how often aside, since a change
    /// of the pace changes nothing of what is a hit.
    /// </summary>
    /// <param name="watch">The watch's filter now.</param>
    /// <param name="checkedWith">The filter the check ran with.</param>
    /// <returns><see langword="true"/> when they are the same.</returns>
    private static bool SameFilter(WatchFilter watch, WatchFilter checkedWith) =>
        WatchCodec.Encode(watch with { Pace = WatchPace.EveryBlock })
        == WatchCodec.Encode(checkedWith with { Pace = WatchPace.EveryBlock });

    /// <summary>
    /// Seals the start the background set, for a watch that has none. Like how far it read, the start is only the
    /// background's word, and only money above the start is a hit: it counts no higher than the case's own lookups
    /// have seen the chain, so that a forged entry cannot hide every hit behind a start far ahead, and no lower than
    /// the chain can have been when the watch was made (what they had seen by then, or what a later lookup bounds),
    /// since the open check starts no earlier than that either and money from before would be hits. An entry with
    /// a start beyond what the case has seen waits in the queue (<see cref="Waits"/>) until it has seen that far.
    /// </summary>
    /// <param name="transaction">The case's write.</param>
    /// <param name="watch">The watch.</param>
    /// <param name="background">The start the background set.</param>
    /// <param name="checkedUtc">
    /// When it was set, to note the watch as read to its start when nothing is noted yet; <see langword="null"/> when
    /// the check that brings it notes how far it read.
    /// </param>
    /// <returns>The start sealed, or <see langword="null"/> when none was.</returns>
    private long? Start(ICaseTransaction transaction, Watch watch, long background, DateTimeOffset? checkedUtc)
    {
        if (heights.Known(transaction, watch.Chain) is not { } seen)
        {
            return null;
        }

        var made = heights.AtLeast(transaction, watch.Chain, watch.CreatedUtc) ?? 0;
        var height = Math.Max(Math.Min(background, seen), made);
        var journal = transaction.AppendJournal(new JournalDraft(
            MonitoringJournalKinds.Started,
            $"Bevakningen av {watch.Subject.Name} börjar efter block {WatchTexts.Number(height)} (i bakgrunden)",
            JsonSerializer.Serialize(new { watch = watch.Id, start = height, background = true })));
        var started = watch with { StartHeight = height, ChangedUtc = journal.Utc };
        WatchStore.Seal(transaction, WatchAction.Started, started, null, journal);
        if (checkedUtc is { } utc && WatchStore.ReadChecked(transaction).GetValueOrDefault(watch.Id) is null)
        {
            // Never later than now: a start dated in a far future would make the watch never due.
            var at = utc < clock.UtcNow ? utc : clock.UtcNow;
            WatchStore.WriteChecked(transaction, watch.Id, new WatchChecked(at, height, WatchFeedStatus.Read, null));
        }

        return height;
    }

    /// <summary>Says whether an entry is already in the case.</summary>
    /// <param name="reader">The case's reader.</param>
    /// <param name="key">The entry's id.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool WasImported(ICaseReader reader, string key)
    {
        using var command = reader.CreateCommand("SELECT 1 FROM monitoring_imported WHERE entry_id = $id");
        var parameter = command.CreateParameter();
        parameter.ParameterName = "$id";
        parameter.Value = key;
        command.Parameters.Add(parameter);
        return command.ExecuteScalar() is not null;
    }

    /// <summary>Notes that an entry is in the case.</summary>
    /// <param name="transaction">The case's write.</param>
    /// <param name="key">The entry's id.</param>
    private void MarkImported(ICaseTransaction transaction, string key)
    {
        using var command = transaction.CreateCommand(
            "INSERT INTO monitoring_imported (entry_id, imported_utc) VALUES ($id, $utc)");
        var id = command.CreateParameter();
        id.ParameterName = "$id";
        id.Value = key;
        command.Parameters.Add(id);
        var utc = command.CreateParameter();
        utc.ParameterName = "$utc";
        utc.Value = WatchCodec.Text(clock.UtcNow);
        command.Parameters.Add(utc);
        command.ExecuteNonQuery();
    }
}
