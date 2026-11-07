using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Pricing;

/// <summary>
/// Registers the module Pricing: exchange rates at a point in time, with their source, kept in the case.
/// </summary>
public static class PricingModule
{
    /// <summary>Adds the module's services to the container. The module has no services yet.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddPricingModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
