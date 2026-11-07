using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>An endpoint as the settings show it: its latest probe, whether it failed since and is used.</summary>
/// <param name="Endpoint">The endpoint.</param>
/// <param name="Probe">Its latest probe, or <see langword="null"/> when it has not been tried.</param>
/// <param name="FailedSince">
/// Whether a lookup through it failed since it was last tried, so that the others are asked before it.
/// </param>
/// <param name="InUse">Whether it is the one the next lookup asks first.</param>
public sealed record EndpointView(SourceEndpoint Endpoint, EndpointProbe? Probe, bool FailedSince, bool InUse);

/// <summary>A chain's sources: its mode, its endpoints in the user's order and when they were last tried.</summary>
/// <param name="Chain">The chain.</param>
/// <param name="Mode">How the endpoint is chosen.</param>
/// <param name="Endpoints">The endpoints, in the user's order.</param>
/// <param name="ProbedAt">When they were last tried, in UTC, or <see langword="null"/>.</param>
public sealed record ChainSourcesView(
    Chain Chain, SourceMode Mode, IReadOnlyList<EndpointView> Endpoints, DateTimeOffset? ProbedAt)
{
    /// <summary>Gets the endpoint the next lookup asks first, or <see langword="null"/> when none is asked.</summary>
    public EndpointView? InUse => Endpoints.FirstOrDefault(endpoint => endpoint.InUse);
}
