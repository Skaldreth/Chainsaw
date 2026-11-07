using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Contracts;

/// <summary>
/// How a watch says it has hit when Chainsaw is closed (decided 2026-11-07, chosen in the settings): one or more
/// ways, each saying only that a watch has hit, never the address, the amount or the case.
/// </summary>
[Flags]
public enum WatchAlerts
{
    /// <summary>None.</summary>
    None = 0,

    /// <summary>A notification in Windows.</summary>
    Windows = 1,

    /// <summary>A sound: a coin swoosh with a rattle.</summary>
    Sound = 2,

    /// <summary>A message in the middle of the screen that stays on top until it is closed.</summary>
    Screen = 4,
}

/// <summary>
/// One watch as the background process knows it: only what the check needs (CLAUDE.md, <em>Bevaka</em>). Neither the
/// node's name nor anything else of the case.
/// </summary>
/// <param name="WatchId">The watch's id in the case.</param>
/// <param name="Addresses">
/// The watched addresses: the address, or a cluster's as the clustering put them when the plan was written.
/// </param>
/// <param name="Filter">What it looks for, with how often.</param>
/// <param name="StartHeight">The block it starts from, or <see langword="null"/> when it has not started.</param>
/// <param name="Checked">When the case last checked it, or <see langword="null"/>.</param>
public sealed record BackgroundWatch(
    string WatchId,
    IReadOnlyList<ChainAddress> Addresses,
    WatchFilter Filter,
    long? StartHeight,
    WatchChecked? Checked)
{
    /// <summary>
    /// Gets the hits the case already has in the look-back above where the watch is read
    /// (<see cref="WatchSchedule.Key"/>), so that the background never tells of one the open Chainsaw sealed.
    /// </summary>
    public IReadOnlyList<string> Known { get; init; } = [];
}

/// <summary>A case's active watches as the background process knows them, by the case's random id only.</summary>
/// <param name="CaseId">The case's id (<see cref="CaseInfo.Id"/>), never its number or title.</param>
/// <param name="Watches">Its active watches on chains that can be watched.</param>
/// <param name="WrittenUtc">When Chainsaw wrote them.</param>
public sealed record BackgroundCase(Guid CaseId, IReadOnlyList<BackgroundWatch> Watches, DateTimeOffset WrittenUtc);

/// <summary>
/// What Chainsaw leaves for the background process (the plan), protected for the Windows user (DPAPI): whether it
/// should run, the way out, how it says a watch has hit, and per case the active watches. Chainsaw writes it when
/// watches are made, changed, paused or taken back, after its own checks, when a case is closed and when the settings
/// change.
/// </summary>
/// <param name="Enabled">Whether the watching goes on when Chainsaw is closed (the user can turn it off).</param>
/// <param name="Route">The way out chosen in the settings.</param>
/// <param name="Alerts">How a hit is said.</param>
/// <param name="Cases">The cases with active watches.</param>
public sealed record BackgroundPlan(
    bool Enabled, ExitRoute Route, WatchAlerts Alerts, IReadOnlyList<BackgroundCase> Cases)
{
    /// <summary>Gets the plan when none is written: off, through VPN, nothing to watch.</summary>
    public static BackgroundPlan Empty { get; } = new(false, ExitRoute.Vpn, WatchAlerts.None, []);

    /// <summary>Gets whether the background process has anything to do: it is on and a watch is active.</summary>
    public bool HasWork => Enabled && Cases.Any(@case => @case.Watches.Count > 0);

    /// <summary>Gives the plan with one case's watches replaced; a case without active watches is left out.</summary>
    /// <param name="case">The case.</param>
    /// <returns>The new plan.</returns>
    public BackgroundPlan With(BackgroundCase @case)
    {
        ArgumentNullException.ThrowIfNull(@case);
        var others = Cases.Where(each => each.CaseId != @case.CaseId);
        return this with { Cases = [.. others, .. @case.Watches.Count > 0 ? [@case] : Array.Empty<BackgroundCase>()] };
    }
}

/// <summary>
/// Reads and writes the plan, protected for the Windows user. Chainsaw writes it; the background reads it.
/// </summary>
public interface IBackgroundPlan
{
    /// <summary>Reads the plan.</summary>
    /// <returns>The plan, or <see cref="BackgroundPlan.Empty"/> when none is written or it cannot be read.</returns>
    BackgroundPlan Read();

    /// <summary>
    /// Reads the plan, telling a plan that cannot be read just now (held by a write or a scanner) apart from one that
    /// is missing or not this user's.
    /// </summary>
    /// <param name="plan">The plan; <see cref="BackgroundPlan.Empty"/> when missing or foreign.</param>
    /// <returns><see langword="false"/> when it could not be read now and should be read again later.</returns>
    bool TryRead(out BackgroundPlan plan);

    /// <summary>Gets when the plan was last written, so that the background reads it again only then.</summary>
    /// <returns>The time, or <see langword="null"/> when there is no plan.</returns>
    DateTimeOffset? WrittenAt();

    /// <summary>
    /// Changes the plan under a lock, so that two changes in Chainsaw never overwrite each other, and writes it in one
    /// step.
    /// </summary>
    /// <param name="change">Makes the new plan from the one written.</param>
    /// <returns>The plan written.</returns>
    BackgroundPlan Update(Func<BackgroundPlan, BackgroundPlan> change);
}

/// <summary>What a queued entry is.</summary>
public enum QueuedKind
{
    /// <summary>A watch without a start got the chain's height as its start.</summary>
    Started,

    /// <summary>A watch was checked.</summary>
    Checked,
}

/// <summary>
/// What the background process saw in one check of one watch, kept until the case is opened and unlocked: the
/// matches, the raw responses they rest on with their SHA-256, and how far the watch was read.
/// </summary>
/// <param name="Id">The entry's id; bringing it in twice brings it in once.</param>
/// <param name="CaseId">The case.</param>
/// <param name="WatchId">The watch.</param>
/// <param name="Kind">A start, or a check.</param>
/// <param name="Filter">The filter the check ran with; a watch changed since gets no hits from it.</param>
/// <param name="Start">The start the check counted from.</param>
/// <param name="Height">The chain's height the check (or start) used.</param>
/// <param name="Checked">How far the watch was read, for a check.</param>
/// <param name="Matches">The money that moved the way the watch looks for.</param>
/// <param name="Origins">The responses the matches rest on, by their <see cref="SealedResponse.Sequence"/>.</param>
/// <param name="Responses">Every raw response the check fetched.</param>
/// <param name="Utc">When the check started.</param>
public sealed record QueuedCheck(
    Guid Id,
    Guid CaseId,
    string WatchId,
    QueuedKind Kind,
    WatchFilter Filter,
    long Start,
    long Height,
    WatchChecked? Checked,
    IReadOnlyList<WatchMatch> Matches,
    IReadOnlyList<long> Origins,
    IReadOnlyList<SealedResponse> Responses,
    DateTimeOffset Utc)
{
    /// <summary>
    /// Gets whether a match is one the background told of: above where the watch was read and not known to the case.
    /// The queue marks such an entry, so that the lock screen can say that a watch has hit.
    /// </summary>
    public bool HasNewHits { get; init; }
}

/// <summary>
/// The queue of what the background process saw, one protected file per entry, written in one step so that it
/// survives a crash. The background adds; Chainsaw brings the entries into the case and removes them.
/// </summary>
public interface IWatchQueue
{
    /// <summary>Adds an entry, durably.</summary>
    /// <param name="entry">The entry.</param>
    void Add(QueuedCheck entry);

    /// <summary>Lists a case's entries, oldest first.</summary>
    /// <param name="caseId">The case.</param>
    /// <returns>The entries' ids.</returns>
    IReadOnlyList<Guid> List(Guid caseId);

    /// <summary>Reads an entry.</summary>
    /// <param name="caseId">The case.</param>
    /// <param name="id">The entry.</param>
    /// <returns>The entry, or <see langword="null"/> when it is gone.</returns>
    /// <exception cref="InvalidDataException">
    /// It cannot be read; it is set aside so that it is not read again.
    /// </exception>
    QueuedCheck? Read(Guid caseId, Guid id);

    /// <summary>Removes an entry once it is in the case.</summary>
    /// <param name="caseId">The case.</param>
    /// <param name="id">The entry.</param>
    void Remove(Guid caseId, Guid id);

    /// <summary>Sets an entry that cannot be brought in aside: it is kept, but never read again.</summary>
    /// <param name="caseId">The case.</param>
    /// <param name="id">The entry.</param>
    void SetAside(Guid caseId, Guid id);

    /// <summary>
    /// Says whether any entry has a hit, without reading the entries, so that the lock screen can say that a watch has
    /// hit (and nothing more) while the vault is locked.
    /// </summary>
    /// <returns><see langword="true"/> when one has.</returns>
    bool HasHits();
}

/// <summary>
/// Marks a case as open in Chainsaw, so that the background process leaves it to Chainsaw meanwhile. The mark is a
/// held file lock: it goes with the process, also when Chainsaw crashes.
/// </summary>
public interface ICaseHolds
{
    /// <summary>Holds a case.</summary>
    /// <param name="caseId">The case.</param>
    /// <returns>
    /// Releases the hold; <see langword="null"/> when it could not be taken (another Chainsaw holds it).
    /// </returns>
    IDisposable? Hold(Guid caseId);

    /// <summary>Says whether a case is held by a running Chainsaw.</summary>
    /// <param name="caseId">The case.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    bool IsHeld(Guid caseId);
}

/// <summary>What the background process is doing, as it says itself.</summary>
public enum BackgroundState
{
    /// <summary>It watches through the way out.</summary>
    Running,

    /// <summary>VPN is the way out, which it cannot connect without the vault; it looks nothing up.</summary>
    WaitingForExit,

    /// <summary>The watched chains' sources are off; it looks nothing up.</summary>
    SourcesOff,

    /// <summary>The lookups fail; it tries again.</summary>
    Failing,

    /// <summary>It has nothing to do and stops.</summary>
    Stopped,
}

/// <summary>The background process's own word on what it does; nothing about a case.</summary>
/// <param name="State">What it does.</param>
/// <param name="InStepWith">The endpoint in step it asks, for example <c>mempool.space</c>, when known.</param>
/// <param name="Utc">When it said so.</param>
/// <param name="ProcessId">Its process.</param>
public sealed record BackgroundStatus(BackgroundState State, string? InStepWith, DateTimeOffset Utc, int ProcessId);

/// <summary>The background process's status file (plain: it says nothing about a case).</summary>
public interface IBackgroundStatusFile
{
    /// <summary>Reads the status.</summary>
    /// <returns>The status, or <see langword="null"/> when none can be read.</returns>
    BackgroundStatus? Read();

    /// <summary>Writes the status in one step.</summary>
    /// <param name="status">The status.</param>
    void Write(BackgroundStatus status);
}

/// <summary>How far the background process has come with one watch: kept by it, between its runs.</summary>
/// <param name="CaseId">The case.</param>
/// <param name="WatchId">The watch.</param>
/// <param name="StartHeight">The start it set, when the plan had none.</param>
/// <param name="Checked">When it last checked the watch, or <see langword="null"/>.</param>
/// <param name="Alerted">The hits it has already told of (<c>watch|tx|direction|asset</c>), newest last.</param>
public sealed record BackgroundProgress(
    Guid CaseId, string WatchId, long? StartHeight, WatchChecked? Checked, IReadOnlyList<string> Alerted);

/// <summary>The background process's progress, protected for the Windows user; only it writes it.</summary>
public interface IBackgroundProgressFile
{
    /// <summary>Reads the progress.</summary>
    /// <returns>Per watch; empty when none can be read.</returns>
    IReadOnlyList<BackgroundProgress> Read();

    /// <summary>Writes the progress in one step.</summary>
    /// <param name="progress">Per watch.</param>
    void Write(IReadOnlyList<BackgroundProgress> progress);
}

/// <summary>What bringing the background's entries into a case did.</summary>
/// <param name="Entries">How many entries were brought in.</param>
/// <param name="Responses">How many raw responses were sealed.</param>
/// <param name="Hits">The hits sealed.</param>
/// <param name="Problems">What could not be brought in, in Swedish.</param>
public sealed record BackgroundImport(
    int Entries, int Responses, IReadOnlyList<WatchHit> Hits, IReadOnlyList<string> Problems)
{
    /// <summary>Gets an import that did nothing.</summary>
    public static BackgroundImport None { get; } = new(0, 0, [], []);
}

/// <summary>
/// The case's side of the background watching: what of a case the background may know, and bringing what it saw
/// into the case with journal, hash and sealed hits as the open Chainsaw does.
/// </summary>
/// <remarks>Both read or write the case; call them off the UI thread.</remarks>
public interface IBackgroundWatches
{
    /// <summary>
    /// Describes a case's active watches for the plan: the addresses (a cluster's as the clustering puts them now, at
    /// most <see cref="WatchRules.MaximumAddresses"/>), the filter, the start and how far it is read.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="utc">When.</param>
    /// <returns>The case for the plan.</returns>
    BackgroundCase Describe(ICase @case, DateTimeOffset utc);

    /// <summary>
    /// Brings the queued entries of a case into it, oldest first, each in one write: the raw responses sealed again
    /// unchanged after their SHA-256 is checked, a start sealed when the watch has none, the hits sealed under a line in
    /// the journal when the watch is still active and unchanged and the hit is new, how far the watch is read, and the
    /// entry's id, so that it is never brought in twice. The case is saved before the entries are removed.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <returns>What was brought in.</returns>
    BackgroundImport Import(ICase @case);
}

/// <summary>
/// Gives the feed that reads a chain through a case's sources, for the open Chainsaw and the background.
/// </summary>
public interface IWatchFeeds
{
    /// <summary>Gives the feed for a chain.</summary>
    /// <param name="case">The case the responses are sealed in.</param>
    /// <param name="chain">The chain.</param>
    /// <returns>The feed, or <see langword="null"/> for a chain that cannot be watched yet.</returns>
    IWatchFeed? For(ICase @case, Chain chain);
}

/// <summary>When a watch is due and how far a check leaves it, written once for Chainsaw and the background.</summary>
public static class WatchSchedule
{
    /// <summary>
    /// How much earlier than its pace a watch may be checked, since the height is asked once a minute and a check
    /// takes time: without it a watch of one minute would wait for two.
    /// </summary>
    public static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Says whether a watch is due: a new block has come since it was last checked, and its pace's time has passed
    /// since then. A watch never checked, or whose last check failed or could not ask, is read again.
    /// </summary>
    /// <param name="pace">The watch's pace.</param>
    /// <param name="last">When it was last checked, or <see langword="null"/>.</param>
    /// <param name="height">The chain's height now.</param>
    /// <param name="now">Now.</param>
    /// <returns><see langword="true"/> when it is checked now.</returns>
    public static bool IsDue(WatchPace pace, WatchChecked? last, long height, DateTimeOffset now)
    {
        if (last is null)
        {
            return true;
        }

        var newBlock = height > last.Tip || last.Status is WatchFeedStatus.Failed or WatchFeedStatus.NotAsked;
        return newBlock && now - last.CheckedUtc >= pace.Interval() - Tolerance;
    }

    /// <summary>Gives the block a check reads back to: the last read-through height less the look-back.</summary>
    /// <param name="start">The watch's start.</param>
    /// <param name="last">When it was last checked, or <see langword="null"/>.</param>
    /// <returns>The block.</returns>
    public static long DownTo(long start, WatchChecked? last) =>
        Math.Max(start, (last?.Height ?? start) - WatchRules.Lookback);

    /// <summary>
    /// Gives how far a check leaves a watch: moved on to what was read only when every address was read back to where
    /// the check was, so that a failed, partial or not asked lookup is read again from the same place.
    /// </summary>
    /// <param name="previous">The read-through height before the check (its start when never checked).</param>
    /// <param name="result">The check.</param>
    /// <param name="problems">What to say about it.</param>
    /// <param name="height">The chain's height.</param>
    /// <param name="started">When the check started, which is when it counts as made for the pace.</param>
    /// <returns>How far it is read.</returns>
    public static WatchChecked After(
        long previous, WatchCheckResult result, IReadOnlyList<string> problems, long height, DateTimeOffset started)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(problems);
        var through = Math.Max(height, result.Highest ?? height);
        var kept = result.Status switch
        {
            WatchFeedStatus.Read => through,
            WatchFeedStatus.Partial when result.CompleteAbove is { } above => Math.Max(previous, above),
            _ => previous,
        };
        var problem = problems.Count == 0 ? null : string.Join(' ', problems);
        return new WatchChecked(started, kept, result.Status, problem) { Tip = Math.Max(height, kept) };
    }

    /// <summary>Gives the key that says two hits are the same: the watch, transaction, direction and asset.</summary>
    /// <param name="watchId">The watch.</param>
    /// <param name="match">The match.</param>
    /// <returns>The key.</returns>
    public static string Key(string watchId, WatchMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return string.Join('|', watchId, match.Transaction, match.Direction, match.Amount.Asset.ToString());
    }
}

/// <summary>The names the open Chainsaw and the background process share.</summary>
public static class BackgroundNames
{
    /// <summary>The folder under Chainsaw's application data.</summary>
    public const string Folder = "Bevakning";

    /// <summary>The background process's program, next to Chainsaw's.</summary>
    public const string Executable = "Chainsaw.Watcher.exe";

    /// <summary>The single-instance mutex, per Windows session.</summary>
    public const string Mutex = @"Local\Chainsaw.Watcher";

    /// <summary>The event that asks the background process to stop.</summary>
    public const string StopEvent = @"Local\Chainsaw.Watcher.Stop";

    /// <summary>The value under the user's <c>Run</c> key that starts it with Windows.</summary>
    public const string RunValue = "Chainsaw.Watcher";

    /// <summary>What every way of telling says: only that a watch has hit (CLAUDE.md, <em>Bevaka</em>).</summary>
    public const string AlertTitle = "En bevakning har slagit till.";

    /// <summary>The line under it.</summary>
    public const string AlertDetail = "Öppna Chainsaw och lås upp för att se den.";

    /// <summary>
    /// Who fetched a response the background process fetched (principle 1): the background process itself, which
    /// knows no operator; the journal line that brings it into the case names the operator who opened it.
    /// </summary>
    public static OperatorProfile Fetcher { get; } =
        new("Chainsaw i bakgrunden", "Bevakning när Chainsaw är stängt", "Den här datorns Windows-användare");
}
