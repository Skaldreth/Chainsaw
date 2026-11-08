using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Tracing.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Registers the module Tracing: the graph as a model, the tracing methods, ends of a trace and edges to other chains.
/// </summary>
/// <remarks>
/// The methods are registered as <see cref="ITracingMethod"/>, each for the model and chains in its
/// <see cref="ITracingMethod.Scope"/> (FIFO and haircut for every UTXO chain, and
/// for every account chain), and the recognisers of the trail's
/// ends as <see cref="ITrailEndClassifier"/>; another of either (Attribution's labels, a local model later) is added
/// by registering one more. The service needs <c>IBitcoinSource</c> (Chains), <c>IClusteringService</c>
/// (Clustering) and <c>IClock</c>.
/// </remarks>
public static class TracingModule
{
    /// <summary>Adds the module's services to the container.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddTracingModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton<ICaseSchemaContributor, TracingSchema>();
        services.AddSingleton<ITracingMethod, FifoMethod>();
        services.AddSingleton<ITracingMethod, HaircutMethod>();
        services.AddSingleton<ITracingMethod, AccountFifoMethod>();
        services.AddSingleton<ITracingMethod, AccountHaircutMethod>();
        services.AddSingleton<ITrailEndClassifier, CoinJoinTrailEnds>();
        services.AddSingleton<ITracingService, TracingService>();
        services.AddSingleton<ICaseSchemaContributor, TrashSchema>();
        services.AddSingleton<IGraphTrash, GraphTrashService>();
        services.AddSingleton<IGraphCommentService, GraphCommentService>();
        return services;
    }
}
