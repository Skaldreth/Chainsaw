using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// Registers the module Monitoring: watching addresses and clusters, and telling when the money moves (CLAUDE.md,
/// <em>Bevaka</em>).
/// </summary>
/// <remarks>
/// The module needs the Cases, Chains (<c>IBitcoinSource</c>, <c>IEthereumSource</c>, <c>IChainSources</c>),
/// Clustering, Network (<c>IExitRoute</c>, <c>IExitConnection</c>) and Security (<c>IVault</c>) modules. The gate is
/// added only when none is registered, so that a test or the background process may bring its own.
/// </remarks>
public static class MonitoringModule
{
    /// <summary>Adds the module's services to the container.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddMonitoringModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICaseSchemaContributor, MonitoringSchema>();
        services.TryAddSingleton<IWatchCheck, WatchCheck>();
        services.TryAddSingleton<IWatchGate, MonitoringGate>();
        services.AddSingleton<IMonitoringService, MonitoringService>();
        return services;
    }
}
