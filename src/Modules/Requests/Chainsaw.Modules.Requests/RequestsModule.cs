using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Requests;

/// <summary>
/// Registers the module Requests: requests for information to exchanges, built from templates.
/// </summary>
public static class RequestsModule
{
    /// <summary>Adds the module's services to the container. The module has no services yet.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddRequestsModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
