using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Registers the module Network: the exits (VPN) through which every request Chainsaw makes leaves the computer.
/// </summary>
public static class NetworkModule
{
    /// <summary>Adds the module's services to the container. The module has no services yet.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddNetworkModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
