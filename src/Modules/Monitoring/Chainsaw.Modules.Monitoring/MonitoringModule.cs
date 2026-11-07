using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// Registers the module Monitoring: watching addresses and clusters, and telling when the money moves.
/// </summary>
public static class MonitoringModule
{
    /// <summary>Adds the module's services to the container. The module has no services yet.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddMonitoringModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
