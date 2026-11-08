using Chainsaw.Modules.Tracing.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.App.Graph;

/// <summary>
/// How the graph is read into a scene: the tracing method, whether the outputs that can never be spent and carry
/// nothing are drawn, and which addresses are never folded into a pile. All of it goes into the reading, so that the
/// scene is built once, off the UI thread (principle 6), instead of being built again on it.
/// </summary>
/// <param name="Method">The tracing method.</param>
/// <param name="ShowUnspendable">Whether the outputs that can never be spent and carry nothing are drawn.</param>
public sealed record GraphReading(TracingMethodKind Method, bool ShowUnspendable = false)
{
    /// <summary>Gets a set of no addresses, shared since it is never changed.</summary>
    public static IReadOnlySet<ChainAddress> NoAddresses { get; } = new HashSet<ChainAddress>();

    /// <summary>
    /// Gets the addresses never folded into a pile: the watched and the watches' counterparties
    /// (<see cref="GraphWatches.KeptOutOfPiles"/>), so that the watch line and its rings stay in the graph.
    /// </summary>
    public IReadOnlySet<ChainAddress> KeptOutOfPiles { get; init; } = NoAddresses;

    /// <summary>Reads by a method, with the outputs that can never be spent hidden, as the graph starts.</summary>
    /// <param name="method">The tracing method.</param>
    public static implicit operator GraphReading(TracingMethodKind method) => FromTracingMethodKind(method);

    /// <summary>Reads by a method, with the outputs that can never be spent hidden, as the graph starts.</summary>
    /// <param name="method">The tracing method.</param>
    /// <returns>The reading.</returns>
    public static GraphReading FromTracingMethodKind(TracingMethodKind method) => new(method);
}
