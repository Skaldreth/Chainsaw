using Chainsaw.Modules.Security.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chainsaw.Modules.Security;

/// <summary>
/// Registers the module Security: the vault with the master key, the API keys, the operator profile, other modules'
/// named secrets and the keys derived for them.
/// </summary>
public static class SecurityModule
{
    /// <summary>Adds the module's services to the container.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="configure">
    /// Changes the options, for example the vault folder; by default the vault lives in
    /// <c>%LOCALAPPDATA%\Chainsaw\</c>.
    /// </param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddSecurityModule(
        this IServiceCollection services, Action<VaultOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new VaultOptions();
        configure?.Invoke(options);

        // A factory, not an instance, so that the container disposes (locks) the vault when the application ends.
        services.AddSingleton(_ => new Vault(options));
        services.AddSingleton<IVault>(provider => provider.GetRequiredService<Vault>());
        services.AddSingleton<IApiKeyStore, ApiKeyStore>();
        services.AddSingleton<IOperatorProfileStore, OperatorProfileStore>();
        services.AddSingleton<IPurposeKeys, PurposeKeys>();
        services.AddSingleton<IVaultSecrets, VaultSecrets>();
        return services.AddUserDataProtection();
    }

    /// <summary>
    /// Adds only the protection for the signed-in Windows user (DPAPI), without the vault: what the background watching
    /// needs. A protector already registered (a test's) is kept.
    /// </summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddUserDataProtection(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IUserDataProtector, DpapiProtector>();
        return services;
    }
}
