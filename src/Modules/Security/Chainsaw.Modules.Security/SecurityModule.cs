using Microsoft.Extensions.DependencyInjection;

namespace Chainsaw.Modules.Security;

/// <summary>
/// Registers the module Security: the master key, the vault (API keys) and the operator profile.
/// </summary>
public static class SecurityModule
{
    /// <summary>Adds the module's services to the container. The module has no services yet.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddSecurityModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services;
    }
}
