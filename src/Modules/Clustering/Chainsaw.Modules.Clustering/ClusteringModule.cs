using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Clustering.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Clustering;

/// <summary>
/// Registers the module Clustering: the heuristics, by name and version, and the clusters with their merges and splits.
/// </summary>
/// <remarks>
/// The heuristics are registered as <see cref="IClusteringHeuristic"/>, each for the model and chains in its
/// <see cref="IClusteringHeuristic.Scope"/> (today's five for Bitcoin, three of them also in an older version);
/// another analysis (for example a local model later, principle 5) is added by registering one more. The module adds
/// its sealed tables to every case through an
/// <see cref="ICaseSchemaContributor"/>.
/// </remarks>
public static class ClusteringModule
{
    /// <summary>Adds the module's services to the container.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddClusteringModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ICaseSchemaContributor, ClusteringSchema>();
        // The older versions stay registered, so that a case that draws a newer one back runs the older instead
        // (IClusteringService.Withdraw); only the highest version not drawn back runs.
        services.AddSingleton<IClusteringHeuristic>(new CoinJoinHeuristic(1));
        services.AddSingleton<IClusteringHeuristic>(new CoinJoinHeuristic(2));
        services.AddSingleton<IClusteringHeuristic, CommonInputOwnershipHeuristic>();
        services.AddSingleton<IClusteringHeuristic>(new ChangeAddressHeuristic(1));
        services.AddSingleton<IClusteringHeuristic>(new ChangeAddressHeuristic(2));
        services.AddSingleton<IClusteringHeuristic>(new PeelChainHeuristic(1));
        services.AddSingleton<IClusteringHeuristic>(new PeelChainHeuristic(2));
        services.AddSingleton<IClusteringHeuristic, PayJoinHeuristic>();
        services.AddSingleton<IClusteringService, ClusteringService>();
        return services;
    }
}
