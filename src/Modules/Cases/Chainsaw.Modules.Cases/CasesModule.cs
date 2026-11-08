using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chainsaw.Modules.Cases;

/// <summary>
/// Registers the module Cases: one encrypted .cut file per case, schema steps per module, the journal and the hash
/// chain that seals it and every other sealed table, and the verification behind <c>/verify</c>.
/// </summary>
/// <remarks>
/// The module needs the Security module's <c>IVault</c>, <c>IPurposeKeys</c> and <c>IOperatorProfileStore</c>, and an
/// <see cref="IClock"/>. Other modules add their tables by registering an <see cref="ICaseSchemaContributor"/>.
/// </remarks>
public static class CasesModule
{
    /// <summary>Adds the module's services to the container.</summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="configure">
    /// Changes the options, for example the cases folder; by default the cases live in
    /// <c>Dokument\Chainsaw\Ärenden</c>.
    /// </param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddCasesModule(
        this IServiceCollection services, Action<CasesOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new CasesOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICaseSchemaContributor, CasesSchema>();
        services.AddSingleton(provider => new CaseStore(
            provider.GetRequiredService<Security.Contracts.IVault>(),
            provider.GetRequiredService<Security.Contracts.IPurposeKeys>(),
            provider.GetRequiredService<Security.Contracts.IOperatorProfileStore>(),
            provider.GetService<Security.Contracts.IVaultSecrets>(),
            provider.GetRequiredService<IClock>(),
            options,
            provider.GetServices<ICaseSchemaContributor>()));
        services.AddSingleton<ICaseStore>(provider => provider.GetRequiredService<CaseStore>());
        services.AddSingleton<ICaseVerifier, CaseVerifier>();
        return services.AddMemoryCases();
    }

    /// <summary>
    /// Adds only the cases in memory (<see cref="IMemoryCases"/>), for a process that never opens a real case, such
    /// as the background watching. The modules' tables come from the registered <see cref="ICaseSchemaContributor"/>s.
    /// </summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddMemoryCases(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IMemoryCases>(provider =>
            new MemoryCases(provider.GetServices<ICaseSchemaContributor>()));
        return services;
    }
}
