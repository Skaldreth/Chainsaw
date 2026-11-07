using Chainsaw.Modules.Network.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Registers the module Network: the exits (VPN) through which every request Chainsaw makes leaves the computer, and
/// <see cref="IExitHttp"/>, the only way out for the other modules. Needs the Security module (the exits are kept in
/// the vault) and an <c>IClock</c>.
/// </summary>
public static class NetworkModule
{
    /// <summary>Adds the module's services to the container.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="configure">Changes the limits on requests.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A limit is zero, negative or too long.</exception>
    public static IServiceCollection AddNetworkModule(
        this IServiceCollection services, Action<NetworkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new NetworkOptions();
        configure?.Invoke(options);
        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<ExitRecords>();
        services.AddSingleton<IExitHandlerFactory, SocksHandlerFactory>();
        services.AddSingleton<ExitConnection>();
        services.AddSingleton<IExitConnection>(provider => provider.GetRequiredService<ExitConnection>());
        services.AddSingleton<IExitStore, ExitStore>();
        services.AddSingleton<IExitHttp, ExitHttp>();

        // Chainsaw's own WireGuard helper; without it next to the program, connecting fails honestly.
        services.TryAddSingleton<ITunnelProcessStarter, HelperProcessStarter>();
        services.TryAddSingleton<IExitConnector, WireGuardConnector>();
        return services;
    }
}
