using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Tracing.Contracts;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// The cross-chain search where the chains' sources are not registered (a host without the Chains module): it finds
/// nothing, so the cross-chain analysis weighs only the payments the graph holds.
/// </summary>
internal sealed class NoCrossChainLookup : ICrossChainLookup
{
    /// <summary>Gets the one instance.</summary>
    public static NoCrossChainLookup Instance { get; } = new();

    /// <inheritdoc/>
    public Task<CrossChainArrivals> FindAsync(
        ICase @case, TraceGraph graph, LookupMode mode, CancellationToken cancellationToken = default) =>
        Task.FromResult(CrossChainArrivals.Empty);
}
