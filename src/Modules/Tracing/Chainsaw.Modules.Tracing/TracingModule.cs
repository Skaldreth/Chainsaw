using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Tracing;

/// <summary>
/// Registers the module Tracing: the graph as a model, the tracing methods, ends of a trace and edges to other chains.
/// </summary>
public static class TracingModule
{
    /// <summary>Adds the module's services to the container. The module has no services yet.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddTracingModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
