using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Registers the module Chains: one contract per kind of chain, and the sources behind it (Esplora, and the user's own
/// Bitcoin Core, for Bitcoin, Esplora for Litecoin, Blockscout for Ethereum and BSC, TronGrid for TRON).
/// </summary>
/// <remarks>
/// The module needs the Network module's <c>IExitHttp</c> (its only way out to the sources), and its
/// <c>ILocalHttp</c> when an endpoint is marked as the user's own node (without it such an endpoint fails and nothing
/// goes another way), and the Security module's <c>IOperatorProfileStore</c>, and adds its tables to every case through
/// an <see cref="ICaseSchemaContributor"/>.
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
    /// <param name="tron">
    /// The TRON source's settings; by default TronGrid as TRON's endpoint until the user changes it.
    /// </param>
    /// <param name="litecoin">
    /// The Litecoin source's settings; by default litecoinspace.org as Litecoin's endpoint until the user changes it
    /// (<see cref="BitcoinSourceOptions.ForLitecoin"/>). Their network must be Litecoin's.
    /// </param>
    /// <param name="bsc">The BSC source's settings; by default no endpoint until the user adds one.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddChainsModule(
        this IServiceCollection services, BitcoinSourceOptions? options = null,
        Action<ChainSourcesOptions>? sources = null, EthereumSourceOptions? ethereum = null,
        TronSourceOptions? tron = null, BitcoinSourceOptions? litecoin = null, BscSourceOptions? bsc = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var settings = options ?? new BitcoinSourceOptions();
        var ethereumSettings = ethereum ?? new EthereumSourceOptions();
        var tronSettings = tron ?? new TronSourceOptions();
        var litecoinSettings = litecoin ?? BitcoinSourceOptions.ForLitecoin();
        var bscSettings = bsc ?? new BscSourceOptions();
        var sourcesOptions = new ChainSourcesOptions();
        sources?.Invoke(sourcesOptions);
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICaseSchemaContributor, ChainsSchema>();
        services.AddSingleton<IResponseTransfer, ResponseTransfer>();
        services.AddSingleton<ICaseHeights>(new CaseHeights(settings));

        // Each sort of API is registered here, once per chain it is spoken for; another chain only adds its own.
        services.AddSingleton<ISourceApi>(new EsploraApi(settings));
        services.AddSingleton<ISourceApi>(new BitcoinCoreApi(settings));
        services.AddSingleton<ISourceApi>(new BlockscoutApi(ethereumSettings));
        services.AddSingleton<ISourceApi>(new TronGridApi(tronSettings));
        services.AddSingleton<ISourceApi>(new EsploraApi(litecoinSettings));
        services.AddSingleton<ISourceApi>(new BlockscoutApi(bscSettings));
        services.AddSingleton(provider => new ChainSources(
            provider.GetServices<ISourceApi>(),
            provider.GetRequiredService<IExitHttp>(),
            provider.GetService<IApiKeyStore>(),
            provider.GetService<IVault>(),
            provider.GetRequiredService<IClock>(),
            sourcesOptions,
            provider.GetService<ILocalHttp>(),
            sourcesOptions.Delay));
        services.AddSingleton<IChainSources>(provider => provider.GetRequiredService<ChainSources>());

        // News of new blocks by websocket where an endpoint has it; only a nudge for the watching.
        services.AddSingleton(provider => new BlockNews(
            provider.GetRequiredService<IChainSources>(),
            provider.GetService<IExitWebSocket>(),
            provider.GetRequiredService<IClock>(),
            network: settings.Network));
        services.AddSingleton<IBlockNews>(provider => provider.GetRequiredService<BlockNews>());
        services.AddSingleton<IBitcoinSource>(provider => new EsploraBitcoinSource(
            provider.GetRequiredService<ChainSources>(),
            provider.GetRequiredService<IOperatorProfileStore>(),
            provider.GetRequiredService<IClock>(),
            settings,
            TimeZoneInfo.Local));
        services.AddSingleton<IEthereumSource>(provider => new BlockscoutEthereumSource(
            provider.GetRequiredService<IOperatorProfileStore>(),
            provider.GetRequiredService<IClock>(),
            ethereumSettings,
            TimeZoneInfo.Local,
            provider.GetRequiredService<ChainSources>()));
        services.AddSingleton<ITronSource>(provider => new TronGridTronSource(
            provider.GetRequiredService<IOperatorProfileStore>(),
            provider.GetRequiredService<IClock>(),
            tronSettings,
            TimeZoneInfo.Local,
            provider.GetRequiredService<ChainSources>()));
        services.AddSingleton<ILitecoinSource>(provider => new EsploraLitecoinSource(
            provider.GetRequiredService<ChainSources>(),
            provider.GetRequiredService<IOperatorProfileStore>(),
            provider.GetRequiredService<IClock>(),
            litecoinSettings,
            TimeZoneInfo.Local));
        services.AddSingleton<IBscSource>(provider => new BlockscoutBscSource(
            provider.GetRequiredService<IOperatorProfileStore>(),
            provider.GetRequiredService<IClock>(),
            bscSettings,
            TimeZoneInfo.Local,
            provider.GetRequiredService<ChainSources>()));
        return services;
    }
}
