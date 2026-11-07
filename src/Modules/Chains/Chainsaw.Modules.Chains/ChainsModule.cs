using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Registers the module Chains: one contract per kind of chain, and the sources behind it (Esplora first for Bitcoin).
/// </summary>
/// <remarks>
/// The module needs the Network module's <c>IExitHttp</c> (its only way out) and the Security module's
/// <c>IOperatorProfileStore</c>, and adds its tables to every case through an <see cref="ICaseSchemaContributor"/>.
/// </remarks>
public static class ChainsModule
{
    /// <summary>Adds the module's services to the container.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="options">The Bitcoin source's settings; by default Esplora at mempool.space.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddChainsModule(
        this IServiceCollection services, BitcoinSourceOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var settings = options ?? new BitcoinSourceOptions();
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICaseSchemaContributor, ChainsSchema>();
        services.AddSingleton<IBitcoinSource>(provider => new EsploraBitcoinSource(
            provider.GetRequiredService<IExitHttp>(),
            provider.GetRequiredService<IOperatorProfileStore>(),
            provider.GetRequiredService<IClock>(),
            settings,
            TimeZoneInfo.Local));
        return services;
    }
}
