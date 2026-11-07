using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Chains;

/// <summary>
/// Registers the module Chains: one contract per kind of chain, and the sources behind it (Esplora first for Bitcoin).
/// </summary>
public static class ChainsModule
{
    /// <summary>Adds the module's services to the container. The module has no services yet.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddChainsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
