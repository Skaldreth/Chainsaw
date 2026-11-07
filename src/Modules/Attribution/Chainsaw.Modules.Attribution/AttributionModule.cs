using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Attribution;

/// <summary>
/// Registers the module Attribution: labels with their source: sanction lists, open sources, own labels and behaviour.
/// </summary>
public static class AttributionModule
{
    /// <summary>Adds the module's services to the container. The module has no services yet.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddAttributionModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
