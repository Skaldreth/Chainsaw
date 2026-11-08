using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// Turns the plan, the queue's entries and the background's progress into JSON and back, in Chainsaw's own spelled-out
/// form (the filter as the case seals it), so that the files never depend on how a type happens to serialise.
/// </summary>
internal static class BackgroundCodec
{
    /// <summary>The form's version; a file of another version is not read.</summary>
    public const int Version = 1;

    /// <summary>Writes the plan.</summary>
    /// <param name="plan">The plan.</param>
    /// <returns>The UTF-8 JSON.</returns>
    public static byte[] Encode(BackgroundPlan plan) => JsonSerializer.SerializeToUtf8Bytes(new PlanText(
        Version,
        plan.Enabled,
        plan.Route.ToString(),
        (int)plan.Alerts,
        [.. plan.Cases.Select(@case => new CaseText(
            @case.CaseId,
            WatchCodec.Text(@case.WrittenUtc),
            [.. @case.Watches.Select(watch => new WatchText(
                watch.WatchId,
                [.. watch.Addresses.Select(address => address.Value)],
                WatchCodec.Encode(watch.Filter),
                watch.StartHeight,
                Of(watch.Checked),
                [.. watch.Known]))]))]));

    /// <summary>Reads the plan.</summary>
    /// <param name="json">The UTF-8 JSON.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="InvalidDataException">It cannot be read.</exception>
    public static BackgroundPlan DecodePlan(byte[] json) => Reading(() =>
    {
        var text = JsonSerializer.Deserialize<PlanText>(json) ?? throw Broken();
        Expect(text.Version);
        var route = Enum.Parse<ExitRoute>(text.Route);
        var cases = text.Cases.Select(@case => new BackgroundCase(
            @case.CaseId,
            [.. @case.Watches.Select(watch =>
            {
                var filter = WatchCodec.DecodeFilter(watch.Filter).Checked();
                return new BackgroundWatch(
                    watch.WatchId,
                    [.. watch.Addresses.Select(value => new ChainAddress(filter.Chain, value))],
                    filter,
                    watch.Start,
                    Of(watch.Checked))
                {
                    Known = watch.Known ?? [],
                };
            })],
            WatchCodec.Time(@case.WrittenUtc)));

        // An unknown way out is read as VPN, never as Windows (as the settings do).
        return new BackgroundPlan(
            text.Enabled, Enum.IsDefined(route) ? route : ExitRoute.Vpn, (WatchAlerts)text.Alerts, [.. cases]);
    });

    /// <summary>Writes a queued entry.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The UTF-8 JSON.</returns>
    public static byte[] Encode(QueuedCheck entry) => JsonSerializer.SerializeToUtf8Bytes(new EntryText(
        Version,
        entry.Id,
        entry.CaseId,
        entry.WatchId,
        entry.Kind.ToString(),
        WatchCodec.Encode(entry.Filter),
        entry.Start,
        entry.Height,
        Of(entry.Checked),
        [.. entry.Matches.Select(match => new MatchText(
            match.Transaction,
            match.Block.Height,
            match.Block.Hash,
            WatchCodec.Text(match.Block.Time),
            match.Direction.ToString(),
            WatchCodec.Encode(match.Amount.Asset),
            WatchCodec.Units(match.Amount),
            [.. match.Counterparties.Select(party => party.Value)]))],
        [.. entry.Origins],
        [.. entry.Responses],
        WatchCodec.Text(entry.Utc),
        entry.HasNewHits));

    /// <summary>Reads a queued entry.</summary>
    /// <param name="json">The UTF-8 JSON.</param>
    /// <returns>The entry.</returns>
    /// <exception cref="InvalidDataException">It cannot be read.</exception>
    public static QueuedCheck DecodeEntry(byte[] json) => Reading(() =>
    {
        var text = JsonSerializer.Deserialize<EntryText>(json) ?? throw Broken();
        Expect(text.Version);
        var filter = WatchCodec.DecodeFilter(text.Filter);
        var matches = text.Matches.Select(match =>
        {
            var asset = WatchCodec.DecodeAsset(match.Asset);
            return new WatchMatch(
                match.Transaction,
                new WatchBlock(match.Height, match.Hash, WatchCodec.Time(match.Time)),
                Enum.Parse<WatchDirection>(match.Direction),
                new AssetAmount(asset, BigInteger.Parse(match.Units, CultureInfo.InvariantCulture)),
                [.. match.Counterparties.Select(value => new ChainAddress(filter.Chain, value))]);
        });
        return Valid(new QueuedCheck(
            text.Id,
            text.CaseId,
            text.WatchId,
            Enum.Parse<QueuedKind>(text.Kind),
            filter,
            text.Start,
            text.Height,
            Of(text.Checked),
            [.. matches],
            text.Origins,
            text.Responses,
            WatchCodec.Time(text.Utc))
        {
            HasNewHits = text.HasNewHits,
        });
    });

    /// <summary>
    /// Refuses an entry that does not hold together, since the queue's file is never trusted: every value the case
    /// seals is there, every enum is a known one, and every match is one the entry's filter could give (money in or
    /// out as the filter watches, of its asset and chain, above its minimum, with only its chosen counterparties).
    /// </summary>
    /// <param name="entry">The entry as read.</param>
    /// <returns>The same entry.</returns>
    /// <exception cref="InvalidDataException">It does not hold together.</exception>
    private static QueuedCheck Valid(QueuedCheck entry)
    {
        var filter = entry.Filter.Checked();
        var holds = !string.IsNullOrWhiteSpace(entry.WatchId)
            && entry.WatchId.Length <= WatchRules.MaximumTextLength
            && Enum.IsDefined(entry.Kind)
            && entry.Start >= 0 && entry.Height >= 0
            && entry.Origins is not null
            && entry.Responses is not null && entry.Responses.All(response => response is not null)
            && (entry.Checked is not { } read
                || (Enum.IsDefined(read.Status) && read.Height >= 0 && read.Tip >= read.Height))
            && (entry.Kind == QueuedKind.Checked || entry.Matches.Count == 0)
            && entry.Matches.All(match => Fits(filter, match));
        return holds ? entry : throw Broken();
    }

    /// <summary>Says whether a match is one a filter could give.</summary>
    /// <param name="filter">The filter.</param>
    /// <param name="match">The match.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    private static bool Fits(WatchFilter filter, WatchMatch match)
    {
        var amount = match.Amount;
        var chosen = match.Direction == WatchDirection.Out ? filter.Receivers : filter.Senders;
        return !string.IsNullOrWhiteSpace(match.Transaction)
            && !string.IsNullOrWhiteSpace(match.Block.Hash)
            && match.Block.Height >= 0
            && (match.Direction == WatchDirection.Out ? filter.WatchesOut
                : match.Direction == WatchDirection.In && filter.WatchesIn)
            && amount.Asset.Chain == filter.Chain
            && (filter.Asset is not { } asset || asset == amount.Asset)
            && amount.Units.Sign > 0
            && (filter.Minimum is not { } minimum || minimum.Asset != amount.Asset || amount.Units >= minimum.Units)
            && (chosen.Count == 0 || match.Counterparties.All(chosen.Contains));
    }

    /// <summary>Writes the background's progress.</summary>
    /// <param name="progress">Per watch.</param>
    /// <returns>The UTF-8 JSON.</returns>
    public static byte[] Encode(IReadOnlyList<BackgroundProgress> progress) =>
        JsonSerializer.SerializeToUtf8Bytes(new ProgressFileText(
            Version,
            [.. progress.Select(each => new ProgressText(
                each.CaseId, each.WatchId, each.StartHeight, Of(each.Checked), [.. each.Alerted]))]));

    /// <summary>Reads the background's progress.</summary>
    /// <param name="json">The UTF-8 JSON.</param>
    /// <returns>Per watch.</returns>
    /// <exception cref="InvalidDataException">It cannot be read.</exception>
    public static IReadOnlyList<BackgroundProgress> DecodeProgress(byte[] json) => Reading(() =>
    {
        var text = JsonSerializer.Deserialize<ProgressFileText>(json) ?? throw Broken();
        Expect(text.Version);
        return (IReadOnlyList<BackgroundProgress>)[.. text.Watches.Select(each => new BackgroundProgress(
            each.CaseId, each.WatchId, each.Start, Of(each.Checked), each.Alerted))];
    });

    /// <summary>Writes how far a watch is read.</summary>
    /// <param name="checkedAt">The check, or <see langword="null"/>.</param>
    /// <returns>The stored form.</returns>
    private static CheckedText? Of(WatchChecked? checkedAt) => checkedAt is null
        ? null
        : new CheckedText(
            WatchCodec.Text(checkedAt.CheckedUtc), checkedAt.Height, checkedAt.Tip, checkedAt.Status.ToString(),
            checkedAt.Problem);

    /// <summary>Reads how far a watch is read.</summary>
    /// <param name="text">The stored form, or <see langword="null"/>.</param>
    /// <returns>The check.</returns>
    private static WatchChecked? Of(CheckedText? text) => text is null
        ? null
        : new WatchChecked(
            WatchCodec.Time(text.Utc), text.Height, Enum.Parse<WatchFeedStatus>(text.Status), text.Problem)
        {
            Tip = text.Tip,
        };

    /// <summary>Runs a read and turns any failure of the form into one error.</summary>
    /// <typeparam name="T">What is read.</typeparam>
    /// <param name="read">The read.</param>
    /// <returns>What was read.</returns>
    /// <exception cref="InvalidDataException">It cannot be read.</exception>
    private static T Reading<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception exception) when (exception is JsonException or FormatException or ArgumentException
            or OverflowException or NullReferenceException)
        {
            throw new InvalidDataException("A file of the background watching cannot be read.", exception);
        }
    }

    /// <summary>Refuses a form of another version.</summary>
    /// <param name="version">The version read.</param>
    /// <exception cref="InvalidDataException">It is another.</exception>
    private static void Expect(int version)
    {
        if (version != Version)
        {
            throw Broken();
        }
    }

    /// <summary>Gives the error for a file that cannot be read.</summary>
    /// <returns>The error.</returns>
    private static InvalidDataException Broken() => new("A file of the background watching cannot be read.");

    /// <summary>The plan as stored.</summary>
    /// <param name="Version">The form's version.</param>
    /// <param name="Enabled">Whether the background is on.</param>
    /// <param name="Route">The way out.</param>
    /// <param name="Alerts">How a hit is said.</param>
    /// <param name="Cases">The cases.</param>
    private sealed record PlanText(int Version, bool Enabled, string Route, int Alerts, List<CaseText> Cases);

    /// <summary>A case as stored.</summary>
    /// <param name="CaseId">Its id.</param>
    /// <param name="WrittenUtc">When it was written.</param>
    /// <param name="Watches">Its watches.</param>
    private sealed record CaseText(Guid CaseId, string WrittenUtc, List<WatchText> Watches);

    /// <summary>A watch as stored.</summary>
    /// <param name="WatchId">Its id.</param>
    /// <param name="Addresses">Its addresses.</param>
    /// <param name="Filter">Its filter, as the case seals it.</param>
    /// <param name="Start">Its start.</param>
    /// <param name="Checked">How far it is read.</param>
    /// <param name="Known">The hits the case has in the look-back.</param>
    private sealed record WatchText(
        string WatchId, List<string> Addresses, string Filter, long? Start, CheckedText? Checked, List<string>? Known);

    /// <summary>How far a watch is read, as stored.</summary>
    /// <param name="Utc">When it was checked.</param>
    /// <param name="Height">The read-through height.</param>
    /// <param name="Tip">The chain's height then.</param>
    /// <param name="Status">How it went.</param>
    /// <param name="Problem">What went wrong.</param>
    private sealed record CheckedText(string Utc, long Height, long Tip, string Status, string? Problem);

    /// <summary>A queued entry as stored.</summary>
    /// <param name="Version">The form's version.</param>
    /// <param name="Id">Its id.</param>
    /// <param name="CaseId">The case.</param>
    /// <param name="WatchId">The watch.</param>
    /// <param name="Kind">Start or check.</param>
    /// <param name="Filter">The filter.</param>
    /// <param name="Start">The start.</param>
    /// <param name="Height">The height.</param>
    /// <param name="Checked">How far it read.</param>
    /// <param name="Matches">The matches.</param>
    /// <param name="Origins">What the matches rest on.</param>
    /// <param name="Responses">The raw responses.</param>
    /// <param name="Utc">When.</param>
    /// <param name="HasNewHits">Whether the background told of a hit in it.</param>
    private sealed record EntryText(
        int Version,
        Guid Id,
        Guid CaseId,
        string WatchId,
        string Kind,
        string Filter,
        long Start,
        long Height,
        CheckedText? Checked,
        List<MatchText> Matches,
        List<long> Origins,
        List<SealedResponse> Responses,
        string Utc,
        bool HasNewHits);

    /// <summary>A match as stored.</summary>
    /// <param name="Transaction">The tx.</param>
    /// <param name="Height">Its block.</param>
    /// <param name="Hash">The block's hash.</param>
    /// <param name="Time">The block's time.</param>
    /// <param name="Direction">In or out.</param>
    /// <param name="Asset">The asset.</param>
    /// <param name="Units">The amount's units.</param>
    /// <param name="Counterparties">The other side.</param>
    private sealed record MatchText(
        string Transaction,
        long Height,
        string Hash,
        string Time,
        string Direction,
        string Asset,
        string Units,
        List<string> Counterparties);

    /// <summary>The progress file as stored.</summary>
    /// <param name="Version">The form's version.</param>
    /// <param name="Watches">Per watch.</param>
    private sealed record ProgressFileText(int Version, List<ProgressText> Watches);

    /// <summary>One watch's progress as stored.</summary>
    /// <param name="CaseId">The case.</param>
    /// <param name="WatchId">The watch.</param>
    /// <param name="Start">The start the background set.</param>
    /// <param name="Checked">How far it read.</param>
    /// <param name="Alerted">The hits told of.</param>
    private sealed record ProgressText(
        Guid CaseId, string WatchId, long? Start, CheckedText? Checked, List<string> Alerted);
}
