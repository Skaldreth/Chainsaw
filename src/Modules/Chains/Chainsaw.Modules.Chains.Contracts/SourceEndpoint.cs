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
    Guid Id, Chain Chain, string Name, Uri Address, string Api, string? ApiKey, bool Enabled);
