using Chainsaw.Modules.Network.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chainsaw.Modules.Network;

/// <summary>
/// Registers the module Network: the exits (VPN) and the way out chosen (<see cref="IExitRoute"/>, VPN or Windows)
/// through which every request Chainsaw makes leaves the computer, <see cref="INetworkPresence"/>, whether the computer
/// reaches the internet for the way through Windows (probed only while Windows is chosen), <see cref="IExitReconnect"/>, which connects a fallen tunnel again, and
/// <see cref="IExitHttp"/>, the only way out for
/// the other modules; and <see cref="ILocalHttp"/>, which reaches only the user's own node on the computer or a private
/// network. Needs the Security module (the exits are kept in the vault) and an <c>IClock</c>.
/// </summary>
public static class NetworkModule
{
    /// <summary>Adds the module's services to the container.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="configure">Changes the limits on requests and the way out at the start.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A limit is zero, negative or too long, or the way is unknown.
    /// </exception>
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
        services.AddSingleton<IWindowsHandlerFactory, WindowsHandlerFactory>();
        services.AddSingleton<ExitRouteChoice>();
        services.AddSingleton<IExitRoute>(provider => provider.GetRequiredService<ExitRouteChoice>());
        services.TryAddSingleton<INetworkSignals, WindowsNetworkSignals>();
        services.TryAddSingleton<IInternetProbe>(provider => new InternetProbe(
            provider.GetRequiredService<IWindowsHandlerFactory>(), TimeProvider.System));
        services.TryAddSingleton<INetworkPresence>(provider => new NetworkPresence(
            provider.GetRequiredService<INetworkSignals>(),
            provider.GetRequiredService<IInternetProbe>(),
            provider.GetRequiredService<IExitRoute>(),
            TimeProvider.System));
        services.AddSingleton<IExitHttp, ExitHttp>();
        services.AddSingleton<IExitCheck, ExitCheck>();

        // A tunnel that falls, for example after the computer has slept, is connected again by itself.
        services.AddSingleton(provider => new ExitReconnector(
            provider.GetRequiredService<IExitConnection>(),
            provider.GetRequiredService<INetworkSignals>(),
            TimeProvider.System));
        services.AddSingleton<IExitReconnect>(provider => provider.GetRequiredService<ExitReconnector>());

        // The user's own node on the computer or a private network: straight there, never through the way out.
        services.TryAddSingleton<ILocalHandlerFactory, LocalHandlerFactory>();
        services.AddSingleton<LocalHttp>();
        services.AddSingleton<ILocalHttp>(provider => provider.GetRequiredService<LocalHttp>());

        // Chainsaw's own WireGuard helper; without it next to the program, connecting fails honestly.
        services.TryAddSingleton<ITunnelProcessStarter, HelperProcessStarter>();
        services.TryAddSingleton<IExitConnector, WireGuardConnector>();
        return services;
    }
}
