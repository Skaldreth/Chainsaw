using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// BSC (BNB Smart Chain) from an endpoint that speaks Blockscout's API, asked only through <see cref="IExitHttp"/>:
/// the same lookups as Ethereum's, with BNB as the chain's own coin and BSC's addresses, sealed under their own chain
/// name. No endpoint is open by default; the user adds one (<see cref="BscSourceOptions.DefaultEndpoints"/>).
/// </summary>
/// <param name="operators">The operator profile.</param>
/// <param name="clock">The clock.</param>
/// <param name="options">The settings.</param>
/// <param name="timeZone">The time zone local times are recorded in.</param>
/// <param name="sources">The chains' endpoints, of which BSC's are asked.</param>
internal sealed class BlockscoutBscSource(
    IOperatorProfileStore operators, IClock clock, BscSourceOptions options, TimeZoneInfo timeZone,
    ChainSources sources)
    : BlockscoutSource(
        new BlockscoutChain(Chain.Bsc, ChainName, "BSC", Settings(options).SafeConfirmations,
            Settings(options).PageSize),
        operators, clock, timeZone, sources), IBscSource
{
    /// <summary>The chain and network in the sealed table.</summary>
    public const string ChainName = "bsc-mainnet";

    /// <summary>Checks that the settings are given.</summary>
    /// <param name="options">The settings.</param>
    /// <returns>The same settings.</returns>
    private static BscSourceOptions Settings(BscSourceOptions options) =>
        options ?? throw new ArgumentNullException(nameof(options));
}
