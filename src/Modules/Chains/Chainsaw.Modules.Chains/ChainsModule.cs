using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Registers the module Chains: one contract per kind of chain, and the sources behind it (Esplora for Bitcoin,
/// Blockscout for
/// Ethereum).
/// </summary>
/// <remarks>
/// The module needs the Network module's <c>IExitHttp</c> (its only way out) and the Security module's
/// <c>IOperatorProfileStore</c>, and adds its tables to every case through an <see cref="ICaseSchemaContributor"/>.
/// </remarks>
public static class ChainsModule
{
    /// <summary>Adds the module's services to the container.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="options">
    /// The Bitcoin source's settings; by default Esplora at mempool.space and Blockstream as Bitcoin's endpoints until
    /// the user changes them.
    /// </param>
    /// <param name="sources">
    /// Sets where the endpoints are kept (sketch KL1a); by default in memory only. The application keeps them in its
    /// data.
    /// </param>
    /// <param name="ethereum">
    /// The Ethereum source's settings; by default Blockscout as Ethereum's endpoint until the user changes it.
    /// </param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddChainsModule(
        this IServiceCollection services, BitcoinSourceOptions? options = null,
        Action<ChainSourcesOptions>? sources = null, EthereumSourceOptions? ethereum = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var settings = options ?? new BitcoinSourceOptions();
        var ethereumSettings = ethereum ?? new EthereumSourceOptions();
        var sourcesOptions = new ChainSourcesOptions();
        sources?.Invoke(sourcesOptions);
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICaseSchemaContributor, ChainsSchema>();

        // Each sort of API is registered here; another chain (Ethereum, TRON) only adds its own.
        services.AddSingleton<ISourceApi>(new EsploraApi(settings));
        services.AddSingleton<ISourceApi>(new BlockscoutApi(ethereumSettings));
        services.AddSingleton(provider => new ChainSources(
            provider.GetServices<ISourceApi>(),
            provider.GetRequiredService<IExitHttp>(),
            provider.GetService<IApiKeyStore>(),
            provider.GetService<IVault>(),
            provider.GetRequiredService<IClock>(),
            sourcesOptions));
        services.AddSingleton<IChainSources>(provider => provider.GetRequiredService<ChainSources>());
        services.AddSingleton<IBitcoinSource>(provider => new EsploraBitcoinSource(
            provider.GetRequiredService<IExitHttp>(),
            provider.GetRequiredService<IOperatorProfileStore>(),
            provider.GetRequiredService<IClock>(),
            settings,
            TimeZoneInfo.Local,
            provider.GetRequiredService<ChainSources>()));
        services.AddSingleton<IEthereumSource>(provider => new BlockscoutEthereumSource(
            provider.GetRequiredService<IExitHttp>(),
            provider.GetRequiredService<IOperatorProfileStore>(),
            provider.GetRequiredService<IClock>(),
            ethereumSettings,
            TimeZoneInfo.Local,
            provider.GetRequiredService<ChainSources>()));
        return services;
    }
}
