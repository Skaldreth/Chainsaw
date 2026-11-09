using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>An endpoint as the settings show it: its latest probe, whether it failed since and is used.</summary>
/// <param name="Endpoint">The endpoint.</param>
/// <param name="Probe">Its latest probe, or <see langword="null"/> when it has not been tried.</param>
/// <param name="FailedSince">
/// Whether a lookup through it failed since it was last tried, so that the others are asked before it.
/// </param>
/// <param name="InUse">Whether it is the one the next lookup asks first.</param>
public sealed record EndpointView(SourceEndpoint Endpoint, EndpointProbe? Probe, bool FailedSince, bool InUse)
{
    /// <summary>
    /// Gets until when it asked Chainsaw to wait (<c>429</c> or <c>Retry-After</c>), in UTC, or
    /// <see langword="null"/>. Until then the others are asked, and it comes back by itself.
    /// </summary>
    public DateTimeOffset? WaitUntil { get; init; }

    /// <summary>
    /// Gets whether the own node showed another certificate than the one pinned for it (sketch KL3b), so that it is
    /// refused until the user trusts the new one; never for an endpoint without a pin.
    /// </summary>
    public bool CertificateRefused { get; init; }
}

/// <summary>A chain's sources: its mode, its endpoints in the user's order and when they were last tried.</summary>
/// <param name="Chain">The chain.</param>
/// <param name="Mode">How the endpoint is chosen.</param>
/// <param name="Endpoints">The endpoints, in the user's order.</param>
/// <param name="ProbedAt">When they were last tried, in UTC, or <see langword="null"/>.</param>
public sealed record ChainSourcesView(
    Chain Chain, SourceMode Mode, IReadOnlyList<EndpointView> Endpoints, DateTimeOffset? ProbedAt)
{
    /// <summary>
    /// Gets until when a lookup on the chain waits for an endpoint now (its pace or its <c>Retry-After</c>), in UTC,
    /// or <see langword="null"/> when none waits noticeably; a short wait for the pace is not told.
    /// </summary>
    public DateTimeOffset? WaitingUntil { get; init; }

    /// <summary>Gets the endpoint the next lookup asks first, or <see langword="null"/> when none is asked.</summary>
    public EndpointView? InUse => Endpoints.FirstOrDefault(endpoint => endpoint.InUse);
}
