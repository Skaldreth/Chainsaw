using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.App.Graph;

/// <summary>
/// Where the graph's checks, suggestions and own areas (sketches KF1a and KF1b) read and write the case, through the
/// Checks module's contract. Every method does its work off the UI thread (principle 6).
/// </summary>
public interface ICheckSource
{
    /// <summary>Runs the checks over the graph and seals what is new.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="graph">The graph.</param>
    /// <param name="cancellationToken">Stops the work before it starts.</param>
    /// <returns>What the run gave.</returns>
    Task<CheckRun> RunAsync(ICase @case, TraceGraph graph, CancellationToken cancellationToken);

    /// <summary>Seals the user's decisions about a suggestion, all in one transaction or none.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="requests">The decisions.</param>
    /// <returns>The sealed decisions, in order.</returns>
    Task<IReadOnlyList<CheckDecision>> DecideAsync(ICase @case, IReadOnlyList<DecisionRequest> requests);

    /// <summary>Reads the own areas.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="cancellationToken">Stops the work before it starts.</param>
    /// <returns>The areas.</returns>
    Task<IReadOnlyList<OwnArea>> GetAreasAsync(ICase @case, CancellationToken cancellationToken);

    /// <summary>Seals a new own area.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="draft">The area.</param>
    /// <returns>The sealed area.</returns>
    Task<OwnArea> AddAreaAsync(ICase @case, OwnAreaDraft draft);

    /// <summary>Renames an own area.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The area.</param>
    /// <param name="name">The new name.</param>
    /// <returns>The area, or <see langword="null"/> when it is gone.</returns>
    Task<OwnArea?> RenameAreaAsync(ICase @case, string id, string name);

    /// <summary>Removes an own area.</summary>
    /// <param name="case">The open case.</param>
    /// <param name="id">The area.</param>
    /// <param name="reason">Why, or <see langword="null"/>.</param>
    /// <returns><see langword="false"/> when it is gone already.</returns>
    Task<bool> RemoveAreaAsync(ICase @case, string id, string? reason);
}

/// <summary>The graph's way into the Checks module (<see cref="ICheckSource"/>).</summary>
/// <param name="checks">The checks and own areas.</param>
public sealed class CheckSource(ICheckService checks) : ICheckSource
{
    /// <inheritdoc/>
    public Task<CheckRun> RunAsync(ICase @case, TraceGraph graph, CancellationToken cancellationToken) =>
        Task.Run(() => checks.Run(@case, graph), cancellationToken);

    /// <inheritdoc/>
    public Task<IReadOnlyList<CheckDecision>> DecideAsync(ICase @case, IReadOnlyList<DecisionRequest> requests) =>
        Task.Run(() => checks.DecideAll(@case, requests));

    /// <inheritdoc/>
    public Task<IReadOnlyList<OwnArea>> GetAreasAsync(ICase @case, CancellationToken cancellationToken) =>
        Task.Run(() => checks.GetAreas(@case), cancellationToken);

    /// <inheritdoc/>
    public Task<OwnArea> AddAreaAsync(ICase @case, OwnAreaDraft draft) =>
        Task.Run(() => checks.AddArea(@case, draft));

    /// <inheritdoc/>
    public Task<OwnArea?> RenameAreaAsync(ICase @case, string id, string name) =>
        Task.Run(() => checks.RenameArea(@case, id, name));

    /// <inheritdoc/>
    public Task<bool> RemoveAreaAsync(ICase @case, string id, string? reason) =>
        Task.Run(() => checks.RemoveArea(@case, id, reason));
}

/// <summary>
/// A source with no checks, for a graph built without the Checks module (the tests of the graph alone): every run is
/// empty, and nothing can be stored.
/// </summary>
public sealed class NoCheckSource : ICheckSource
{
    /// <summary>Gets the one instance.</summary>
    public static NoCheckSource Instance { get; } = new();

    /// <inheritdoc/>
    public Task<CheckRun> RunAsync(ICase @case, TraceGraph graph, CancellationToken cancellationToken) =>
        Task.FromResult(new CheckRun([], 0, 0, DateTimeOffset.UtcNow, []));

    /// <inheritdoc/>
    public Task<IReadOnlyList<CheckDecision>> DecideAsync(ICase @case, IReadOnlyList<DecisionRequest> requests) =>
        Task.FromException<IReadOnlyList<CheckDecision>>(new InvalidOperationException("Inga kontroller."));

    /// <inheritdoc/>
    public Task<IReadOnlyList<OwnArea>> GetAreasAsync(ICase @case, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<OwnArea>>([]);

    /// <inheritdoc/>
    public Task<OwnArea> AddAreaAsync(ICase @case, OwnAreaDraft draft) =>
        Task.FromException<OwnArea>(new InvalidOperationException("Inga kontroller."));

    /// <inheritdoc/>
    public Task<OwnArea?> RenameAreaAsync(ICase @case, string id, string name) => Task.FromResult<OwnArea?>(null);

    /// <inheritdoc/>
    public Task<bool> RemoveAreaAsync(ICase @case, string id, string? reason) => Task.FromResult(false);
}
