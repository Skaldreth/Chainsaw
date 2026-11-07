using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains.Contracts;

/// <summary>
/// An address Chainsaw asks a chain at, kept in the program's data (never in a case). A key is only named here; the
/// key itself stays in the vault.
/// </summary>
/// <param name="Id">The endpoint's id, which every raw response from it is sealed with.</param>
/// <param name="Chain">The chain it answers for.</param>
/// <param name="Name">Its name in the settings and in the sealed responses, for example <c>mempool.space</c>.</param>
/// <param name="Address">
/// Its base address: absolute <c>https</c>, without user, query or fragment, ending in <c>/</c>.
/// </param>
/// <param name="Api">The sort of API it speaks, for example <c>esplora</c> (<see cref="SourceApiInfo.Code"/>).</param>
/// <param name="ApiKey">
/// The service the API key is stored under in the vault (<c>IApiKeyStore</c>), or <see langword="null"/> for none.
/// </param>
/// <param name="Enabled">Whether it may be asked.</param>
public sealed record SourceEndpoint(
    Guid Id, Chain Chain, string Name, Uri Address, string Api, string? ApiKey, bool Enabled)
{
    /// <summary>
    /// Gets whether the user marked it as their own node on the computer or a private network (decided 2026-11-07).
    /// Only such an endpoint may have an address there, and lookups to it go straight there and never through the
    /// exit; every other endpoint is asked through the exit.
    /// </summary>
    public bool Local { get; init; }

    /// <summary>
    /// Gets the shortest time between two requests to it, its own pace (<see cref="SourcePace"/>), so that Chainsaw
    /// never asks it faster; a source that asks Chainsaw to wait (<c>429</c>, <c>Retry-After</c>) is waited for too.
    /// </summary>
    public TimeSpan Interval { get; init; } = SourcePace.Public;
}

/// <summary>
/// The paces Chainsaw gives an endpoint when nothing else is said. They are Chainsaw's own careful choice, not limits
/// the sources have published: a source that wants Chainsaw to slow down says so with <c>429</c> and
/// <c>Retry-After</c>, and Chainsaw then waits as long as it asks.
/// </summary>
public static class SourcePace
{
    /// <summary>
    /// The pace of an endpoint on the internet: at most four requests a second. Chainsaw's own choice for
    /// mempool.space, Blockstream, Blockscout and an endpoint the user adds; several endpoints of a chain together
    /// take more, as the lookups are spread over them.
    /// </summary>
    public static readonly TimeSpan Public = TimeSpan.FromMilliseconds(250);

    /// <summary>The pace of the user's own node: no pause of Chainsaw's own; only a node that asks to wait slows
    /// it.</summary>
    public static readonly TimeSpan Local = TimeSpan.Zero;

    /// <summary>The longest pace that is kept; a longer one in the file is cut to it.</summary>
    public static readonly TimeSpan Maximum = TimeSpan.FromMinutes(1);
}
