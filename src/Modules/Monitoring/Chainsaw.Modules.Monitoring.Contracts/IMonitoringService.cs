using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Monitoring.Contracts;

/// <summary>Where a watch stands.</summary>
public enum WatchState
{
    /// <summary>It is checked.</summary>
    Active,

    /// <summary>It is kept but not checked until it is resumed.</summary>
    Paused,

    /// <summary>It was taken back; it stays sealed in the case, struck through.</summary>
    Removed,
}

/// <summary>A watch in the case, as its latest sealed record leaves it.</summary>
/// <param name="Id">The watch's stable id.</param>
/// <param name="Subject">The node watched.</param>
/// <param name="Filter">What it looks for.</param>
/// <param name="State">Active, paused or taken back.</param>
/// <param name="StartHeight">
/// The block it started from: money in blocks above it counts. <see langword="null"/> until the first check knew the
/// chain's height.
/// </param>
/// <param name="CreatedBy">Who made it.</param>
/// <param name="CreatedUtc">When.</param>
/// <param name="ChangedUtc">When its latest record was sealed.</param>
public sealed record Watch(
    string Id,
    WatchSubject Subject,
    WatchFilter Filter,
    WatchState State,
    long? StartHeight,
    OperatorProfile CreatedBy,
    DateTimeOffset CreatedUtc,
    DateTimeOffset ChangedUtc)
{
    /// <summary>Gets the watch's chain.</summary>
    public Chain Chain => Filter.Chain;
}

/// <summary>A hit sealed in the case: money moved the way a watch looks for.</summary>
/// <param name="Sequence">The hit's record in the sealed chain <c>monitoring.hits</c>.</param>
/// <param name="WatchId">The watch.</param>
/// <param name="Chain">The chain.</param>
/// <param name="Match">What moved, with its transaction and block.</param>
/// <param name="Origins">The sealed raw responses it rests on (principle 1).</param>
/// <param name="JournalSequence">The journal entry of the check that found it.</param>
/// <param name="RecordedUtc">When it was sealed.</param>
public sealed record WatchHit(
    long Sequence,
    string WatchId,
    Chain Chain,
    WatchMatch Match,
    IReadOnlyList<long> Origins,
    long JournalSequence,
    DateTimeOffset RecordedUtc);

/// <summary>What a sealed record of a watch did.</summary>
public enum WatchAction
{
    /// <summary>The watch was made.</summary>
    Added,

    /// <summary>What it looks for was changed.</summary>
    Changed,

    /// <summary>The first check set the block it starts from.</summary>
    Started,

    /// <summary>It was paused.</summary>
    Paused,

    /// <summary>It was resumed.</summary>
    Resumed,

    /// <summary>It was taken back.</summary>
    Removed,
}

/// <summary>One sealed record of a watch, for its history (sketch AN1b).</summary>
/// <param name="Sequence">The record in the sealed chain <c>monitoring.watches</c>.</param>
/// <param name="Action">What it did.</param>
/// <param name="Watch">The watch as the record left it.</param>
/// <param name="Reason">Why, for a removal, when one was given.</param>
/// <param name="By">Who.</param>
/// <param name="Utc">When.</param>
public sealed record WatchRecord(
    long Sequence, WatchAction Action, Watch Watch, string? Reason, OperatorProfile By, DateTimeOffset Utc);

/// <summary>Whether lookups for watches may go now (principles 4 and 9).</summary>
public enum WatchGate
{
    /// <summary>They may.</summary>
    Open,

    /// <summary>Chainsaw is locked; nothing is looked up.</summary>
    Locked,

    /// <summary>VPN is chosen and no exit is connected; nothing is looked up.</summary>
    NoExit,

    /// <summary>The chain's sources are off in the settings.</summary>
    ChainOff,
}

/// <summary>Says whether lookups for watches may go now.</summary>
public interface IWatchGate
{
    /// <summary>Gives whether a chain's watches may be checked now.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns><see cref="WatchGate.Open"/>, or why not.</returns>
    WatchGate For(Chain chain);
}

/// <summary>How one watch fared in a check.</summary>
/// <param name="WatchId">The watch.</param>
/// <param name="Status">How its addresses were read.</param>
/// <param name="Hits">The hits sealed now.</param>
/// <param name="Problems">What went wrong or was left out, in Swedish.</param>
public sealed record WatchOutcome(
    string WatchId, WatchFeedStatus Status, IReadOnlyList<WatchHit> Hits, IReadOnlyList<string> Problems);

/// <summary>What a check of a chain's watches did.</summary>
/// <param name="Chain">The chain.</param>
/// <param name="Gate">Whether lookups could go; when not, nothing was asked.</param>
/// <param name="Height">The chain's height the check used, or <see langword="null"/> when it was not known.</param>
/// <param name="Outcomes">One per active watch that was checked.</param>
/// <param name="CheckedUtc">When.</param>
public sealed record WatchRun(
    Chain Chain, WatchGate Gate, long? Height, IReadOnlyList<WatchOutcome> Outcomes, DateTimeOffset CheckedUtc)
{
    /// <summary>Gets every hit sealed in the run.</summary>
    public IEnumerable<WatchHit> Hits => Outcomes.SelectMany(outcome => outcome.Hits);
}

/// <summary>When a watch was last checked, kept beside the sealed records as plain data.</summary>
/// <param name="CheckedUtc">When.</param>
/// <param name="Height">
/// The block the watch is read through: the next check reads back to it (less <see cref="WatchRules.Lookback"/>).
/// </param>
/// <param name="Status">How it went.</param>
/// <param name="Problem">What went wrong, in Swedish, or <see langword="null"/>.</param>
public sealed record WatchChecked(DateTimeOffset CheckedUtc, long Height, WatchFeedStatus Status, string? Problem)
{
    /// <summary>
    /// Gets the chain's height when the watch was checked, so that a check that could not read through to it waits
    /// for a new block instead of reading the same pages every minute; the read-through height when not stored.
    /// </summary>
    public long Tip { get; init; } = Height;
}

/// <summary>
/// The watches of a case (CLAUDE.md, <em>Bevaka</em>, sketches BV1a and BV2a): made, changed, paused and taken back as
/// sealed records with who and when, checked through the chain's sources with <c>LookupMode.InStep</c> (only an
/// endpoint in step with the chain, and never when the exit allows no lookups or the chain is off), and every hit
/// sealed with its transaction, block and hash and a line in the journal.
/// </summary>
/// <remarks>The methods read or write the case; call them off the UI thread (principle 6).</remarks>
public interface IMonitoringService
{
    /// <summary>Reads the watches that are not taken back, oldest first.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The watches.</returns>
    IReadOnlyList<Watch> GetWatches(ICase @case);

    /// <summary>Reads every sealed record of the watches, oldest first, for the history (sketch AN1b).</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The records, also of watches taken back.</returns>
    IReadOnlyList<WatchRecord> GetRecords(ICase @case);

    /// <summary>Reads the hits, oldest first.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>The hits of every watch, also of one taken back.</returns>
    IReadOnlyList<WatchHit> GetHits(ICase @case);

    /// <summary>Reads when each watch was last checked.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>Per watch id; a watch never checked is missing.</returns>
    IReadOnlyDictionary<string, WatchChecked> GetChecked(ICase @case);

    /// <summary>Reads, per watch, the last hit the user has seen.</summary>
    /// <param name="case">The open case.</param>
    /// <returns>Per watch id, the hit's sequence; a watch whose hits are all unseen is missing.</returns>
    IReadOnlyDictionary<string, long> GetSeen(ICase @case);

    /// <summary>Notes that the user has seen a watch's hits up to one, so that they are no longer new.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="watchId">The watch.</param>
    /// <param name="through">The newest hit seen.</param>
    void MarkSeen(ICase @case, string watchId, long through);

    /// <summary>Seals a new watch, with who and when and a line in the journal.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="subject">The node.</param>
    /// <param name="filter">What it looks for.</param>
    /// <returns>The sealed watch.</returns>
    /// <exception cref="ArgumentException">The node or the filter is wrong.</exception>
    Watch Add(ICase @case, WatchSubject subject, WatchFilter filter);

    /// <summary>Seals a change of what a watch looks for; it keeps the block it started from.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The watch.</param>
    /// <param name="filter">What it looks for now, on the same chain.</param>
    /// <returns>The watch, or <see langword="null"/> when there is no such watch in force.</returns>
    /// <exception cref="ArgumentException">The filter is wrong.</exception>
    Watch? Change(ICase @case, string id, WatchFilter filter);

    /// <summary>Pauses or resumes a watch, sealed with a line in the journal.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The watch.</param>
    /// <param name="paused">Whether to pause it.</param>
    /// <returns>The watch, or <see langword="null"/> when there is no such watch in force.</returns>
    Watch? SetPaused(ICase @case, string id, bool paused);

    /// <summary>
    /// Takes a watch back as a new sealed record (sketch AN1b): it is no longer checked, and it and its hits stay in
    /// the case.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The watch.</param>
    /// <param name="reason">Why, or <see langword="null"/>; the reason is optional.</param>
    /// <returns><see langword="false"/> when there is no such watch in force.</returns>
    bool Remove(ICase @case, string id, string? reason);

    /// <summary>Gets whether a chain's watches may be checked now.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The gate.</returns>
    WatchGate Gate(Chain chain);

    /// <summary>
    /// Asks the chain's height, so that a check runs only when a new block has come; nothing is asked when the gate is
    /// closed.
    /// </summary>
    /// <param name="case">The open case the response is sealed in.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The height, or <see langword="null"/> when it could not be had.</returns>
    Task<long?> GetHeightAsync(ICase @case, Chain chain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks every active watch on a chain that is due: a watch without a start gets the height as its start; the
    /// others are due when a new block has come since they were read through and their pace (<see cref="WatchPace"/>)
    /// allows, and then read their addresses through the source with <c>LookupMode.InStep</c>; each new hit is sealed,
    /// with one line in the journal for the check. Nothing is asked when the gate is closed.
    /// </summary>
    /// <param name="case">The open case.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="height">The chain's height, from <see cref="GetHeightAsync"/>.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <returns>What the check did.</returns>
    Task<WatchRun> CheckAsync(ICase @case, Chain chain, long height, CancellationToken cancellationToken = default);
}

/// <summary>The journal codes the Monitoring module writes.</summary>
public static class MonitoringJournalKinds
{
    /// <summary>A watch was made.</summary>
    public const string Added = "monitoring.added";

    /// <summary>A watch was changed.</summary>
    public const string Changed = "monitoring.changed";

    /// <summary>A watch got the block it starts from.</summary>
    public const string Started = "monitoring.started";

    /// <summary>A watch was paused.</summary>
    public const string Paused = "monitoring.paused";

    /// <summary>A watch was resumed.</summary>
    public const string Resumed = "monitoring.resumed";

    /// <summary>A watch was taken back.</summary>
    public const string Removed = "monitoring.removed";

    /// <summary>A check found hits and sealed them.</summary>
    public const string Hit = "monitoring.hit";

    /// <summary>What the background process saw was brought into the case.</summary>
    public const string Imported = "monitoring.imported";
}
