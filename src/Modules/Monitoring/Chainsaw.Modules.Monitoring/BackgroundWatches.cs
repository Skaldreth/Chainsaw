using System.Globalization;
using System.Text.Json;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// The case's side of the background watching (see <see cref="IBackgroundWatches"/>): what the plan may know of a
/// case, and bringing the queue's entries into it with journal, hash and sealed hits.
/// </summary>
/// <param name="service">The watches, which say a cluster's addresses as each check does.</param>
/// <param name="queue">The queue.</param>
/// <param name="transfer">Seals the raw responses again in the case.</param>
/// <param name="clock">The clock.</param>
internal sealed class BackgroundWatches(
    MonitoringService service, IWatchQueue queue, IResponseTransfer transfer, IClock clock) : IBackgroundWatches
{
    /// <summary>The journal line when entries were brought in.</summary>
    public const string ImportedText =
        "Bakgrundens bevakning förd in: {0} kontroller, {1} hämtade svar, {2} träffar";

    /// <summary>What is said when an entry cannot be read.</summary>
    public const string UnreadableText =
        "En kontroll från bakgrunden kunde inte läsas och lades åt sidan; den förs inte in.";

    /// <summary>The chains the background can watch.</summary>
    private static readonly IReadOnlySet<Chain> Watchable = new HashSet<Chain> { Chain.Bitcoin, Chain.Ethereum };

    /// <inheritdoc/>
    public BackgroundCase Describe(ICase @case, DateTimeOffset utc)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var checkedAt = service.GetChecked(@case);
        var watches = service.GetWatches(@case)
            .Where(watch => watch.State == WatchState.Active && Watchable.Contains(watch.Chain))
            .Select(watch => new BackgroundWatch(
                watch.Id,
                service.AddressesOf(@case, watch).Addresses,
                watch.Filter,
                watch.StartHeight,
                checkedAt.GetValueOrDefault(watch.Id)))
            .ToList();
        return new BackgroundCase(@case.Info.Id, watches, utc);
    }

    /// <inheritdoc/>
    public BackgroundImport Import(ICase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var caseId = @case.Info.Id;
        var done = new List<Guid>();
        var hits = new List<WatchHit>();
        var problems = new List<string>();
        var responses = 0;
        var entries = 0;
        foreach (var id in queue.List(caseId))
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

            if (entry is null)
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
                    hits.AddRange(some);
                }
            }
            catch (ArgumentException)
            {
                queue.SetAside(caseId, id);
                problems.Add(UnreadableText);
                continue;
            }

            done.Add(id);
        }

        if (entries > 0)
        {
            @case.AppendJournal(new JournalDraft(
                MonitoringJournalKinds.Imported,
                string.Format(CultureInfo.InvariantCulture, ImportedText, entries, responses, hits.Count),
                JsonSerializer.Serialize(new { entries = done, fetchedBy = BackgroundNames.Fetcher.Name })));
        }

        if (done.Count > 0)
        {
            // The entries go only once the case is saved: until then a crash leaves them to be brought in again,
            // which the ids in the case make harmless.
            @case.Save();
            foreach (var id in done)
            {
                queue.Remove(caseId, id);
            }
        }

        return new BackgroundImport(entries, responses, hits, problems);
    }

    /// <summary>Brings one entry into the case in one write.</summary>
    /// <param name="transaction">The case's write.</param>
    /// <param name="entry">The entry.</param>
    /// <returns>The hits sealed, or <see langword="null"/> when the entry was already in the case.</returns>
    private List<WatchHit>? Bring(ICaseTransaction transaction, QueuedCheck entry)
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
        if (watch is null || watch.State != WatchState.Active
            || WatchCodec.Encode(watch.Filter) != WatchCodec.Encode(entry.Filter))
        {
            return [];
        }

        if (entry.Kind == QueuedKind.Started)
        {
            Start(transaction, watch, entry);
            return [];
        }

        if (watch.StartHeight != entry.Start || entry.Checked is not { } checkedAt)
        {
            return [];
        }

        var current = WatchStore.ReadChecked(transaction).GetValueOrDefault(watch.Id);
        if (current is null || checkedAt.CheckedUtc > current.CheckedUtc)
        {
            WatchStore.WriteChecked(transaction, watch.Id, checkedAt);
        }

        var known = WatchStore.ReadHits(transaction).Select(hit => WatchSchedule.Key(hit.WatchId, hit.Match))
            .ToHashSet(StringComparer.Ordinal);
        var fresh = entry.Matches
            .Where(match => match.Block.Height > entry.Start && known.Add(WatchSchedule.Key(watch.Id, match)))
            .ToList();
        if (fresh.Count == 0)
        {
            return [];
        }

        IReadOnlyList<long> origins =
            [.. entry.Origins.Where(sequences.ContainsKey).Select(origin => sequences[origin])];
        var journal = transaction.AppendJournal(new JournalDraft(
            MonitoringJournalKinds.Hit,
            $"Bevakningen av {watch.Subject.Name} slog till i bakgrunden: {WatchTexts.Hits(fresh)}",
            JsonSerializer.Serialize(new
            {
                watch = watch.Id,
                background = true,
                entry = entry.Id,
                checkedUtc = WatchCodec.Text(entry.Utc),
                hits = fresh.Select(match => new
                {
                    tx = match.Transaction,
                    block = match.Block.Height,
                    hash = match.Block.Hash,
                    direction = match.Direction.ToString(),
                    amount = match.Amount.ToString(),
                }),
                origins,
            })));
        return [.. fresh.Select(match => WatchStore.SealHit(transaction, watch, match, origins, journal))];
    }

    /// <summary>Seals the start the background set, when the watch still has none.</summary>
    /// <param name="transaction">The case's write.</param>
    /// <param name="watch">The watch.</param>
    /// <param name="entry">The entry.</param>
    private void Start(ICaseTransaction transaction, Watch watch, QueuedCheck entry)
    {
        if (watch.StartHeight is not null)
        {
            return;
        }

        var journal = transaction.AppendJournal(new JournalDraft(
            MonitoringJournalKinds.Started,
            $"Bevakningen av {watch.Subject.Name} börjar efter block {WatchTexts.Number(entry.Height)} (i bakgrunden)",
            JsonSerializer.Serialize(new { watch = watch.Id, start = entry.Height, background = true })));
        var started = watch with { StartHeight = entry.Height, ChangedUtc = journal.Utc };
        WatchStore.Seal(transaction, WatchAction.Started, started, null, journal);
        if (WatchStore.ReadChecked(transaction).GetValueOrDefault(watch.Id) is null)
        {
            WatchStore.WriteChecked(
                transaction, watch.Id, new WatchChecked(entry.Utc, entry.Height, WatchFeedStatus.Read, null));
        }
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
