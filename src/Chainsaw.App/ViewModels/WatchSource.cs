using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>Everything the case holds about its watches, read at once.</summary>
/// <param name="Watches">The watches in force, oldest first.</param>
/// <param name="Records">Every record of them, also of those taken back, for the history (sketch AN1b).</param>
/// <param name="Hits">Every hit, oldest first.</param>
/// <param name="Checked">When each watch was last checked.</param>
/// <param name="Seen">Per watch, the last hit the user has seen.</param>
public sealed record WatchSnapshot(
    IReadOnlyList<Watch> Watches,
    IReadOnlyList<WatchRecord> Records,
    IReadOnlyList<WatchHit> Hits,
    IReadOnlyDictionary<string, WatchChecked> Checked,
    IReadOnlyDictionary<string, long> Seen)
{
    /// <summary>Gets a snapshot of a case without watches.</summary>
    public static WatchSnapshot Empty { get; } = new([], [], [], new Dictionary<string, WatchChecked>(),
        new Dictionary<string, long>());

    /// <summary>
    /// Gets the hits the background said it saw (<see cref="WatchTip"/>), oldest first: no facts until Chainsaw's own
    /// lookup has checked them, so they are drawn grey and dashed while they wait.
    /// </summary>
    public IReadOnlyList<WatchTip> Tips { get; init; } = [];

    /// <summary>Gives a watch's tips that wait for Chainsaw's own lookup, newest first.</summary>
    /// <param name="watchId">The watch.</param>
    /// <returns>The tips.</returns>
    public IReadOnlyList<WatchTip> PendingOf(string watchId) =>
        [.. Tips.Where(tip => tip.WatchId == watchId && tip.State == WatchTipState.Pending)
            .OrderByDescending(tip => tip.Sequence)];

    /// <summary>
    /// Gets when the user last looked at the hits, or <see langword="null"/> when never, so that the head can say
    /// <em>2 nya träffar sedan 09:10</em> (sketch BV1a).
    /// </summary>
    public DateTimeOffset? LastSeen { get; init; }

    /// <summary>Gives a watch's hits, newest first.</summary>
    /// <param name="watchId">The watch.</param>
    /// <returns>The hits.</returns>
    public IReadOnlyList<WatchHit> HitsOf(string watchId) =>
        [.. Hits.Where(hit => hit.WatchId == watchId).OrderByDescending(hit => hit.Sequence)];

    /// <summary>Says whether the user has not seen a hit yet.</summary>
    /// <param name="hit">The hit.</param>
    /// <returns><see langword="true"/> when it is new.</returns>
    public bool IsNew(WatchHit hit) =>
        hit is not null && hit.Sequence > Seen.GetValueOrDefault(hit.WatchId, 0);

    /// <summary>Gets how many hits of the watches in force the user has not seen.</summary>
    public int Unseen => Hits.Count(hit => IsNew(hit) && Watches.Any(watch => watch.Id == hit.WatchId));

    /// <summary>Gives a watch in force by its id.</summary>
    /// <param name="watchId">The watch.</param>
    /// <returns>The watch, or <see langword="null"/>.</returns>
    public Watch? Find(string? watchId) => Watches.FirstOrDefault(watch => watch.Id == watchId);
}

/// <summary>
/// Where the app reads and writes the watches, through the Monitoring module's contract. Every method that touches the
/// case does its work off the UI thread (principle 6).
/// </summary>
public interface IWatchSource
{
    /// <summary>Reads everything about the case's watches.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the work before it starts.</param>
    /// <returns>The snapshot.</returns>
    Task<WatchSnapshot> ReadAsync(ICase @case, CancellationToken cancellationToken);

    /// <summary>Seals a new watch.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="subject">The node.</param>
    /// <param name="filter">What it looks for.</param>
    /// <returns>The watch.</returns>
    Task<Watch> AddAsync(ICase @case, WatchSubject subject, WatchFilter filter);

    /// <summary>Seals a change of what a watch looks for.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The watch.</param>
    /// <param name="filter">What it looks for now.</param>
    /// <returns>The watch, or <see langword="null"/> when it is gone.</returns>
    Task<Watch?> ChangeAsync(ICase @case, string id, WatchFilter filter);

    /// <summary>Pauses or resumes a watch.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The watch.</param>
    /// <param name="paused">Whether to pause it.</param>
    /// <returns>The watch, or <see langword="null"/> when it is gone.</returns>
    Task<Watch?> SetPausedAsync(ICase @case, string id, bool paused);

    /// <summary>Takes a watch back, sealed.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The watch.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when it is gone already.</returns>
    Task<bool> RemoveAsync(ICase @case, string id, string? reason);

    /// <summary>Notes that the user has seen a watch's hits.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The watch.</param>
    /// <param name="through">The newest hit seen.</param>
    /// <returns>A task that completes when it is noted.</returns>
    Task MarkSeenAsync(ICase @case, string id, long through);

    /// <summary>Gives whether a chain's watches may be checked now.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The gate.</returns>
    WatchGate Gate(Chain chain);

    /// <summary>Asks the chain's height.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="cancellationToken">Stops the lookup.</param>
    /// <returns>The height, or <see langword="null"/>.</returns>
    Task<long?> GetHeightAsync(ICase @case, Chain chain, CancellationToken cancellationToken);

    /// <summary>Checks a chain's active watches.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="height">The chain's height.</param>
    /// <param name="cancellationToken">Stops the check.</param>
    /// <returns>What the check did.</returns>
    Task<WatchRun> CheckAsync(ICase @case, Chain chain, long height, CancellationToken cancellationToken);
}

/// <summary>The app's way into the Monitoring module (<see cref="IWatchSource"/>).</summary>
/// <param name="monitoring">The watches.</param>
public sealed class WatchSource(IMonitoringService monitoring) : IWatchSource
{
    /// <inheritdoc/>
    public Task<WatchSnapshot> ReadAsync(ICase @case, CancellationToken cancellationToken) => Task.Run(
        () => new WatchSnapshot(
            monitoring.GetWatches(@case),
            monitoring.GetRecords(@case),
            monitoring.GetHits(@case),
            monitoring.GetChecked(@case),
            monitoring.GetSeen(@case))
        {
            LastSeen = monitoring.GetLastSeen(@case),
            Tips = monitoring.GetTips(@case),
        },
        cancellationToken);

    /// <inheritdoc/>
    public Task<Watch> AddAsync(ICase @case, WatchSubject subject, WatchFilter filter) =>
        Task.Run(() => monitoring.Add(@case, subject, filter));

    /// <inheritdoc/>
    public Task<Watch?> ChangeAsync(ICase @case, string id, WatchFilter filter) =>
        Task.Run(() => monitoring.Change(@case, id, filter));

    /// <inheritdoc/>
    public Task<Watch?> SetPausedAsync(ICase @case, string id, bool paused) =>
        Task.Run(() => monitoring.SetPaused(@case, id, paused));

    /// <inheritdoc/>
    public Task<bool> RemoveAsync(ICase @case, string id, string? reason) =>
        Task.Run(() => monitoring.Remove(@case, id, reason));

    /// <inheritdoc/>
    public Task MarkSeenAsync(ICase @case, string id, long through) =>
        Task.Run(() => monitoring.MarkSeen(@case, id, through));

    /// <inheritdoc/>
    public WatchGate Gate(Chain chain) => monitoring.Gate(chain);

    /// <inheritdoc/>
    public Task<long?> GetHeightAsync(ICase @case, Chain chain, CancellationToken cancellationToken) =>
        Task.Run(() => monitoring.GetHeightAsync(@case, chain, cancellationToken), cancellationToken);

    /// <inheritdoc/>
    public Task<WatchRun> CheckAsync(ICase @case, Chain chain, long height, CancellationToken cancellationToken) =>
        Task.Run(() => monitoring.CheckAsync(@case, chain, height, cancellationToken), cancellationToken);
}
