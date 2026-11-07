using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// Registers the module Cases: one encrypted .cut file per case, the journal, the command log and the hash chain.
/// </summary>
public static class CasesModule
{
    /// <summary>Adds the module's services to the container. The module has no services yet.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddCasesModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
