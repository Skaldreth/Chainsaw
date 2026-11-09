using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// The endpoints Chainsaw asks each chain at, and how one is chosen (CLAUDE.md, <em>Källornas adresser hanteras per
/// kedja</em>; sketch KL1a). They are kept in the program's data, never in a case, and a key is only named here: it
/// stays in the vault. Every lookup and every probe goes through the exit (<c>IExitHttp</c>), and every raw response
/// sealed in a case says which endpoint it came from (principle 1). The members are thread-safe. A change applies
/// at once in memory; the returned task completes when it is written to disk.
/// </summary>
public interface IChainSources
{
    /// <summary>Gets the chains Chainsaw has a sort of API for, in the order of <see cref="Chain"/>.</summary>
    IReadOnlyList<Chain> Chains { get; }

    /// <summary>
    /// Raised after a chain's sources or their probes have changed, with the chain, on the thread that changed them.
    /// A handler that throws does not affect the change.
    /// </summary>
    event EventHandler<Chain>? Changed;

    /// <summary>
    /// Raised when the user's own node shows another certificate than the one pinned for it (sketch KL3b), so that
    /// the toaster can say so; once per endpoint and pin, on the thread of the request. A handler that throws does not
    /// affect the lookup, which goes on to the other endpoints.
    /// </summary>
    event EventHandler<SourceEndpoint>? CertificateRefused;

    /// <summary>Gets the sorts of API Chainsaw can speak for a chain.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>The sorts; empty when the chain is not built yet.</returns>
    IReadOnlyList<SourceApiInfo> Apis(Chain chain);

    /// <summary>Gets a chain's sources as they are now.</summary>
    /// <param name="chain">The chain.</param>
    /// <returns>Its mode, endpoints and probes.</returns>
    /// <exception cref="ArgumentException">The chain is not built yet.</exception>
    ChainSourcesView Get(Chain chain);

    /// <summary>Chooses how a chain's endpoint is chosen.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="mode">The mode.</param>
    /// <returns>A task that completes when the change is written; it fails with an <see cref="IOException"/>.</returns>
    Task SetModeAsync(Chain chain, SourceMode mode);

    /// <summary>Adds an endpoint last in the chain's list, switched on.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="address">
    /// The address, checked by <see cref="SourceAddress.TryRead(string?, bool, out Uri?, out string?)"/> (only
    /// https; on the computer or a private network only when <paramref name="local"/>).
    /// </param>
    /// <param name="api">The sort of API, one of <see cref="Apis"/>.</param>
    /// <param name="apiKey">The vault's service for the key, or <see langword="null"/> for none.</param>
    /// <param name="local">
    /// Whether the user marked it as their own node on the computer or a private network, asked straight there and
    /// never through the exit.
    /// </param>
    /// <returns>The endpoint, and a task that completes when it is written.</returns>
    /// <exception cref="ArgumentException">The address, the sort or the key's name cannot be used.</exception>
    (SourceEndpoint Endpoint, Task Saved) Add(Chain chain, Uri address, string api, string? apiKey, bool local = false);

    /// <summary>Removes an endpoint. An endpoint that is not there is ignored.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The endpoint.</param>
    /// <returns>A task that completes when the change is written.</returns>
    Task RemoveAsync(Chain chain, Guid id);

    /// <summary>Switches an endpoint on or off.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The endpoint.</param>
    /// <param name="enabled">Whether it may be asked.</param>
    /// <returns>A task that completes when the change is written.</returns>
    Task SetEnabledAsync(Chain chain, Guid id, bool enabled);

    /// <summary>
    /// Switches on every endpoint that sends a key saved under a service, so that a key the user adds is used without
    /// looking for the endpoint (the user's wish 2026-11-09: that is for technical people). An endpoint that is on, or
    /// that sends another key, is left as it is.
    /// </summary>
    /// <param name="service">The key's service, as the vault names it (case is ignored).</param>
    /// <returns>A task that completes when the change is written.</returns>
    Task EnableForKeyAsync(string service) => Task.CompletedTask;

    /// <summary>
    /// Reads the certificate an endpoint marked local shows, without trusting it and without sending a request, so
    /// that the user can compare it with what the node itself shows and pin it (sketch KL3b). The request goes
    /// straight to the node in the user's own network, never through the way out.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The endpoint.</param>
    /// <param name="cancellationToken">Stops the reading.</param>
    /// <returns>The certificate.</returns>
    /// <exception cref="ArgumentException">The endpoint is not there or not marked local.</exception>
    /// <exception cref="Network.Contracts.ExitException">The node did not answer or could not be reached.</exception>
    Task<Network.Contracts.NodeCertificate> ReadCertificateAsync(
        Chain chain, Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pins a certificate for an endpoint marked local (sketch KL3b): the node must then show exactly it. A pin it had
    /// is kept among the former ones with when and by whom it was replaced.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The endpoint.</param>
    /// <param name="fingerprint">The certificate's fingerprint (<c>CertificatePin</c>).</param>
    /// <param name="by">Who pins it: the operator's name.</param>
    /// <returns>A task that completes when the change is written.</returns>
    /// <exception cref="ArgumentException">
    /// The endpoint is not there or not marked local, or the fingerprint is not one.
    /// </exception>
    Task PinAsync(Chain chain, Guid id, string fingerprint, string by);

    /// <summary>
    /// Removes an endpoint's pin; it is kept among the former ones with when and by whom. Without a pin the node's
    /// certificate is checked against the roots installed on the computer again. An endpoint without a pin is left.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The endpoint.</param>
    /// <param name="by">Who removes it: the operator's name.</param>
    /// <returns>A task that completes when the change is written.</returns>
    Task UnpinAsync(Chain chain, Guid id, string by);

    /// <summary>Moves an endpoint up (negative) or down (positive) in the user's order, within the list.</summary>
    /// <param name="chain">The chain.</param>
    /// <param name="id">The endpoint.</param>
    /// <param name="steps">How many places, up when negative.</param>
    /// <returns>A task that completes when the change is written.</returns>
    Task MoveAsync(Chain chain, Guid id, int steps);

    /// <summary>
    /// Tries every endpoint of the chain that is on, through the exit, by asking only for the chain's height; the
    /// user's own node is tried straight in the own network. Nothing is sent when the chain is off or the vault is
    /// locked. When the exit does not allow lookups now, only the own nodes are tried and kept, unmeasured against the
    /// chain (<see cref="ProbeRun.OwnNodesOnly"/>, <see cref="EndpointProbe.Unreferenced"/>).
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="cancellationToken">Stops the round; its results are then not kept.</param>
    /// <returns>How the round ended.</returns>
    Task<ProbeRun> ProbeAsync(Chain chain, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries an address before it is added: the chain's height, the hash of the block at that height and how long the
    /// height took, through the exit, or straight to the user's own node when it is marked local. Nothing is kept.
    /// </summary>
    /// <param name="chain">The chain.</param>
    /// <param name="address">
    /// The address, checked by <see cref="SourceAddress.TryRead(string?, bool, out Uri?, out string?)"/>.
    /// </param>
    /// <param name="api">The sort of API.</param>
    /// <param name="apiKey">The vault's service for the key, or <see langword="null"/>.</param>
    /// <param name="local">Whether it is marked as the user's own node on the computer or a private network.</param>
    /// <param name="cancellationToken">Stops the attempt.</param>
    /// <returns>What it answered.</returns>
    /// <exception cref="ArgumentException">The address or the sort cannot be used.</exception>
    Task<EndpointProbe> TryAsync(
        Chain chain, Uri address, string api, string? apiKey, bool local = false,
        CancellationToken cancellationToken = default);
}
