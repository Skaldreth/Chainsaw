using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Checks.Contracts;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chainsaw.Modules.Checks;

/// <summary>
/// Registers the module Checks: the checks and suggestions that recognise obfuscation (CLAUDE.md, <em>Förvirring
/// analyseras</em>), the user's decisions about them and the user's own areas in the graph.
/// </summary>
/// <remarks>The module needs <c>IClusteringService</c> (Clustering) and the Cases module.</remarks>
public static class ChecksModule
{
    /// <summary>Adds the module's services to the container.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddChecksModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICaseSchemaContributor, CheckSchema>();
        services.AddSingleton<ICheckAnalysis, MixerOutputsCheck>();
        services.AddSingleton<ICheckAnalysis, MixerPartsCheck>();
        services.AddSingleton<ICheckAnalysis, MixerChainCheck>();
        services.AddSingleton<ICheckAnalysis, MixerBehaviourCheck>();
        services.AddSingleton<ICheckAnalysis, CoinJoinCheck>();
        services.AddSingleton<ICheckAnalysis, PeelChainCheck>();
        services.AddSingleton<ICheckAnalysis, WalletCheck>();
        services.AddSingleton<ICheckAnalysis, CoinJoinMergeCheck>();
        services.AddSingleton<ICheckAnalysis, ServiceBehaviourCheck>();
        services.AddSingleton<ICheckAnalysis, NestedServiceCheck>();
        services.AddSingleton<ICheckAnalysis, SplitMergeCheck>();
        services.AddSingleton<ICheckAnalysis, PassThroughCheck>();
        services.AddSingleton<ICheckAnalysis, DustCheck>();
        services.AddSingleton<ICheckAnalysis, PayJoinCheck>();
        services.AddSingleton<ICheckAnalysis, ActiveHoursCheck>();
        services.AddSingleton<ICheckAnalysis, RoundFiatCheck>();
        services.AddSingleton<ICheckAnalysis, PatternCheck>();
        services.AddSingleton<ICheckAnalysis, CrossChainCheck>();
        services.AddSingleton<ICrossChainLookup>(provider =>
            provider.GetService<IBitcoinSource>() is { } bitcoin
            && provider.GetService<IEthereumSource>() is { } ethereum
                ? new CrossChainLookup(
                    bitcoin,
                    ethereum,
                    provider.GetService<IPriceService>(),
                    provider.GetService<ITronSource>(),
                    provider.GetService<ILitecoinSource>(),
                    provider.GetService<IBscSource>(),
                    provider.GetService<IClock>())
                : NoCrossChainLookup.Instance);
        services.AddSingleton<ICheckAnalysis, ShieldedPoolCheck>();
        services.AddSingleton<ICheckService, CheckService>();
        return services;
    }
}
