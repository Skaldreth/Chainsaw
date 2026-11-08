using System.Diagnostics;
using System.Globalization;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.ViewModels;

/// <summary>
/// Fetches the start points that are transactions the open case does not hold yet, through the exit, so that each
/// becomes a node in the graph rather than only a line among its problems. A fetch runs off the UI thread, one after
/// the other; it is tried when the start point is added, when the case is opened and when the exit connects, and is
/// stopped when the case is closed or Chainsaw locks. A start point the case holds is never fetched again (the data
/// layer never fetches what it has), and one that cannot be fetched now is said (principle 9), never swallowed.
/// </summary>
/// <param name="tracing">The graph and its expansions.</param>
public sealed class StartTransactionFetches(ITracingService tracing)
{
    /// <summary>What is said after the reason when a start point could not be fetched.</summary>
    public const string TryAgainText = "Chainsaw försöker igen när ärendet öppnas och när utgången ansluts.";

    /// <summary>Guards the running work and its cancellation.</summary>
    private readonly Lock _gate = new();

    /// <summary>The work under way, which never fails; each new run waits for the one before.</summary>
    private Task _running = Task.CompletedTask;

    /// <summary>Stops the work for the case it runs for; replaced when it is cancelled.</summary>
    private CancellationTokenSource _cancel = new();

    /// <summary>Raised off the UI thread when a start point came into the case, so that the graph reads it again.
    /// </summary>
    public event EventHandler? Fetched;

    /// <summary>
    /// Raised off the UI thread, once per run, when start points could not be fetched: the reason in Swedish and that
    /// Chainsaw tries again. Never carries more of the case than the start point itself.
    /// </summary>
    public event EventHandler<string>? Missed;

    /// <summary>
    /// Gets the work under way, which never fails; an expansion of the same transaction waits for it, so that it is not
    /// fetched twice.
    /// </summary>
    public Task Settled
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    /// <summary>
    /// Starts fetching the start points that are transactions the case lacks, after the work already under way.
    /// </summary>
    /// <param name="openCase">The open case.</param>
    /// <param name="startPoints">
    /// The start points to try, or <see langword="null"/> for every start point of the case.
    /// </param>
    /// <remarks>The run is kept in <see cref="Settled"/>, which the case's closing waits for.</remarks>
    public void Start(ICase openCase, IReadOnlyList<TraceStartPoint>? startPoints = null)
    {
        ArgumentNullException.ThrowIfNull(openCase);
        lock (_gate)
        {
            var before = _running;
            var token = _cancel.Token;
            _running = Task.Run(() => RunAsync(before, openCase, startPoints, token), CancellationToken.None);
        }
    }

    /// <summary>Stops the work under way, as when Chainsaw locks; what is left is tried again later.</summary>
    public void Cancel()
    {
        lock (_gate)
        {
            _cancel.Cancel();
            _cancel.Dispose();
            _cancel = new CancellationTokenSource();
        }
    }

    /// <summary>Stops the work under way and waits until it has stopped, as before the case is closed.</summary>
    /// <returns>A task that completes when no fetch runs.</returns>
    public Task StopAsync()
    {
        Cancel();
        return Settled;
    }

    /// <summary>Waits for the run before, then fetches what this run should; never fails.</summary>
    /// <param name="before">The run before.</param>
    /// <param name="openCase">The open case.</param>
    /// <param name="startPoints">The start points to try, or <see langword="null"/> for every one.</param>
    /// <param name="cancellationToken">Stops the run.</param>
    /// <returns>A task that completes when the run is done.</returns>
    private async Task RunAsync(
        Task before,
        ICase openCase,
        IReadOnlyList<TraceStartPoint>? startPoints,
        CancellationToken cancellationToken)
    {
        await before.ConfigureAwait(false);
        var missed = new List<string>();
        try
        {
            await FetchAsync(openCase, startPoints, missed, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The case closed or Chainsaw locked: the rest is tried when the case is opened again.
            return;
        }
        catch (CaseException exception)
        {
            missed.Add(CaseTexts.For(exception.Error));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // Only the type is logged: the message could name the case's data. The graph still says that the start
            // point is not in the case, and it is tried again when the case opens or the exit connects.
            Trace.TraceWarning("Fetching a start transaction failed: {0}", exception.GetType().FullName);
        }

        if (missed.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            Missed?.Invoke(this, $"{string.Join(" ", missed.Distinct(StringComparer.Ordinal))} {TryAgainText}");
        }
    }

    /// <summary>Fetches each start point that is a transaction the case lacks, one after the other.</summary>
    /// <param name="openCase">The open case.</param>
    /// <param name="startPoints">The start points to try, or <see langword="null"/> for every one.</param>
    /// <param name="missed">Receives why a start point could not be fetched.</param>
    /// <param name="cancellationToken">Stops the work.</param>
    /// <returns>A task that completes when every one has been tried.</returns>
    private async Task FetchAsync(
        ICase openCase,
        IReadOnlyList<TraceStartPoint>? startPoints,
        List<string> missed,
        CancellationToken cancellationToken)
    {
        if (startPoints is not null && !startPoints.Any(IsTransaction))
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var graph = await tracing.LoadAsync(openCase, cancellationToken).ConfigureAwait(false);
        var wanted = (startPoints ?? graph.StartPoints).Select(RequestFor).OfType<ExpansionRequest>()
            .Where(request => !Holds(graph, request))
            .Distinct()
            .ToList();
        foreach (var request in wanted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = await tracing.ExpandAsync(openCase, request, cancellationToken).ConfigureAwait(false);
            if (result.Outcome == ExpansionOutcome.NotExpanded)
            {
                missed.Add(MissedText(request, result.Problem));
                continue;
            }

            Fetched?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Says why a start point could not be fetched.</summary>
    /// <param name="request">The lookup.</param>
    /// <param name="problem">What the lookup said, or <see langword="null"/>.</param>
    /// <returns>For example <c>Startpunkten 0xb1b1…b1b1 kunde inte hämtas: Offline.</c></returns>
    private static string MissedText(ExpansionRequest request, string? problem)
    {
        var id = request.EthereumTransaction?.Value ?? request.Txid!.Value;
        var why = string.IsNullOrWhiteSpace(problem) ? "källan svarade inte" : problem.TrimEnd('.');
        return string.Format(
            CultureInfo.InvariantCulture, "Startpunkten {0} kunde inte hämtas: {1}.", CaseTexts.Shorten(id), why);
    }

    /// <summary>Says whether a start point is a transaction.</summary>
    /// <param name="startPoint">The start point.</param>
    /// <returns><see langword="true"/> for a Bitcoin or Ethereum transaction.</returns>
    private static bool IsTransaction(TraceStartPoint startPoint) => RequestFor(startPoint) is not null;

    /// <summary>
    /// Gives the lookup that fetches a start point that is a transaction: the Ethereum transaction with every movement
    /// in it, or the Bitcoin transaction followed forward as E does (its receivers, at most 50).
    /// </summary>
    /// <param name="startPoint">The start point.</param>
    /// <returns>The lookup, or <see langword="null"/> for a start point that is an address.</returns>
    private static ExpansionRequest? RequestFor(TraceStartPoint startPoint)
    {
        if (startPoint.EthereumTransaction is { } hash)
        {
            return ExpansionRequest.ForEthereumTransaction(hash);
        }

        return startPoint.Txid is { } txid ? ExpansionRequest.ForTransaction(txid) : null;
    }

    /// <summary>Says whether the graph already holds the transaction a lookup would fetch.</summary>
    /// <param name="graph">The graph as the case holds it.</param>
    /// <param name="request">The lookup.</param>
    /// <returns><see langword="true"/> when the transaction is in the case.</returns>
    private static bool Holds(TraceGraph graph, ExpansionRequest request)
    {
        if (request.EthereumTransaction is { } hash)
        {
            return graph.Account.Transactions.ContainsKey(hash);
        }

        return request.Txid is { } txid && graph.Transactions.ContainsKey(txid);
    }
}
