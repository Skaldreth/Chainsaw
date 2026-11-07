using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Registers the module Clustering: the heuristics, by name and version, and the clusters with their merges and splits.
/// </summary>
public static class ClusteringModule
{
    /// <summary>Adds the module's services to the container. The module has no services yet.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddClusteringModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
