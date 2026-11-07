using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// The open case's watches as the window shows them: the view Bevakning (BV1a), the graph's eyes and watch lines
/// (BV2a) and the dot on the pill's eye all read this one snapshot, so that they never disagree. Writes go through
/// the Monitoring module and are read back. Changes are told on the UI thread. It knows nothing of WPF.
/// </summary>
public sealed class WatchBoard
{
    /// <summary>What is said when the case cannot be read or written.</summary>
    public const string FailedText = "Bevakningen kunde inte läsas eller sparas i ärendet.";

    /// <summary>The open case.</summary>
    private readonly CaseSession _session;

    /// <summary>The Monitoring module.</summary>
    private readonly IWatchSource _source;

    /// <summary>The UI thread.</summary>
    private readonly IUiThread _ui;

    /// <summary>Stops a read when the case changes.</summary>
    private CancellationTokenSource _reading = new();

    /// <summary>Counts reads, so that only the latest is shown.</summary>
    private int _reads;

    /// <summary>Creates the board for the window's open case and reads it.</summary>
    /// <param name="session">The open case.</param>
    /// <param name="source">The Monitoring module.</param>
    /// <param name="ui">The UI thread.</param>
    /// <param name="exit">The way out; when it changes, the gate may have, and the graph turns grey or back.</param>
    public WatchBoard(CaseSession session, IWatchSource source, IUiThread ui, ExitWay? exit = null)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(ui);
        _session = session;
        _source = source;
        _ui = ui;
        session.Changed += (_, _) => _ = ReloadAsync();
        if (exit is not null)
        {
            exit.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        }

        _ = ReloadAsync();
    }

    /// <summary>Raised on the UI thread when the snapshot or the gate may have changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised on the UI thread when a check sealed new hits, with them.</summary>
    public event EventHandler<IReadOnlyList<WatchHit>>? HitsFound;

    /// <summary>Gets the snapshot of the open case, or an empty one.</summary>
    public WatchSnapshot Snapshot { get; private set; } = WatchSnapshot.Empty;

    /// <summary>
    /// Gets the chains whose height could not be had at the last ask, so that the view says lookups fail.
    /// </summary>
    public IReadOnlySet<Chain> HeightFailing => _heightFailing;

    /// <summary>The chains whose height could not be had at the last ask.</summary>
    private readonly HashSet<Chain> _heightFailing = [];

    /// <summary>Gets the problem of the last read or write, or an empty text.</summary>
    public string Problem { get; private set; } = string.Empty;

    /// <summary>Gets whether a case is open, so that watches can be made.</summary>
    public bool HasCase => _session.Current is not null;

    /// <summary>Gives whether a chain's watches may be checked now.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The gate.</returns>
    public WatchGate Gate(Chain chain) => _source.Gate(chain);

    /// <summary>
    /// Reads the open case's watches anew, on the UI thread; a case closed meanwhile shows nothing.
    /// </summary>
    /// <returns>A task that completes when the snapshot is shown.</returns>
    public async Task ReloadAsync()
    {
        _reading.Cancel();
        _reading = new CancellationTokenSource();
        var token = _reading.Token;
        var read = ++_reads;
        var openCase = _session.Current;
        if (openCase is null)
        {
            Show(WatchSnapshot.Empty, string.Empty);
            return;
        }

        try
        {
            var snapshot = await _source.ReadAsync(openCase, token).ConfigureAwait(false);
            _ui.Post(() =>
            {
                if (read == _reads && ReferenceEquals(openCase, _session.Current))
                {
                    Show(snapshot, string.Empty);
                }
            });
        }
        catch (OperationCanceledException)
        {
            // A newer read or another case took over.
        }
        catch (Exception exception) when (exception is CaseException or InvalidDataException)
        {
            _ui.Post(() =>
            {
                if (read == _reads)
                {
                    Show(WatchSnapshot.Empty, FailedText);
                }
            });
        }
    }

    /// <summary>
    /// Tells, from any thread, that a check ran: the case is read anew on the UI thread, and new hits are told.
    /// </summary>
    /// <param name="checkedCase">The case the check ran in; when another case is open by then, nothing is told.</param>
    /// <param name="hits">The hits the check sealed.</param>
    public void Checked(ICase checkedCase, IReadOnlyList<WatchHit> hits)
    {
        ArgumentNullException.ThrowIfNull(hits);
        _ui.Post(() =>
        {
            if (!ReferenceEquals(checkedCase, _session.Current))
            {
                return;
            }

            _ = ReloadAsync();
            if (hits.Count > 0)
            {
                HitsFound?.Invoke(this, hits);
            }
        });
    }

    /// <summary>
    /// Tells, from any thread, whether a chain's height could be had for the open case, so that a failing source is
    /// said rather than hidden behind <em>Bevakar</em>.
    /// </summary>
    /// <param name="checkedCase">The case the runner asked for.</param>
    /// <param name="chain">The chain.</param>
    /// <param name="failed">Whether the height could not be had.</param>
    public void HeightAsked(ICase checkedCase, Chain chain, bool failed) => _ui.Post(() =>
    {
        if (!ReferenceEquals(checkedCase, _session.Current))
        {
            return;
        }

        var changed = failed ? _heightFailing.Add(chain) : _heightFailing.Remove(chain);
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    });

    /// <summary>Notes that the user has seen a watch's hits, when it has new ones.</summary>
    /// <param name="watchId">The watch.</param>
    /// <returns>A task that completes when it is noted and read back.</returns>
    public async Task MarkSeenAsync(string watchId)
    {
        var newest = Snapshot.HitsOf(watchId).FirstOrDefault();
        if (_session.Current is not { } openCase || newest is null || !Snapshot.IsNew(newest))
        {
            return;
        }

        await WriteAsync(() => _source.MarkSeenAsync(openCase, watchId, newest.Sequence));
    }

    /// <summary>
    /// Runs a write against the case, on the UI thread, and reads it back; a failure is told in <see cref="Problem"/>.
    /// </summary>
    /// <param name="write">The write.</param>
    /// <returns><see langword="true"/> when it went through.</returns>
    public async Task<bool> WriteAsync(Func<Task> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        try
        {
            await write();
        }
        catch (Exception exception) when (exception is CaseException or InvalidDataException)
        {
            Problem = FailedText;
            Changed?.Invoke(this, EventArgs.Empty);
            return false;
        }

        await ReloadAsync();
        return true;
    }

    /// <summary>Gets the Monitoring module, for the form and the runner.</summary>
    internal IWatchSource Source => _source;

    /// <summary>Gets the open case, or <see langword="null"/>.</summary>
    internal ICase? Case => _session.Current;

    /// <summary>Shows a snapshot and tells it.</summary>
    /// <param name="snapshot">The snapshot.</param>
    /// <param name="problem">The problem, or an empty text.</param>
    private void Show(WatchSnapshot snapshot, string problem)
    {
        if (_session.Current is null)
        {
            _heightFailing.Clear();
        }

        Snapshot = snapshot;
        Problem = problem;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
