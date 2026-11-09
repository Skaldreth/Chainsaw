using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chainsaw.Modules.Monitoring;

/// <summary>
/// Registers the module Monitoring: watching addresses and clusters, and telling when the money moves (CLAUDE.md,
/// <em>Bevaka</em>).
/// </summary>
/// <remarks>
/// The module needs the Cases, Chains (<c>IBitcoinSource</c>, <c>IEthereumSource</c>, <c>IChainSources</c>),
/// Clustering, Network (<c>IExitRoute</c>, <c>IExitConnection</c>) and Security (<c>IVault</c>) modules. The gate is
/// added only when none is registered, so that a test or the background process may bring its own.
/// </remarks>
public static class MonitoringModule
{
    /// <summary>Adds the module's services to the container.</summary>
    /// <param name="services">The container to add to.</param>
    /// <returns>The same container, so that the registrations can be chained.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddMonitoringModule(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICaseSchemaContributor, MonitoringSchema>();
        services.TryAddSingleton<IWatchCheck, WatchCheck>();
        services.TryAddSingleton<IWatchGate, MonitoringGate>();
        services.TryAddSingleton(provider => new WatchSources(
            provider.GetRequiredService<IBitcoinSource>(),
            provider.GetRequiredService<IEthereumSource>(),
            provider.GetService<ITronSource>(),
            provider.GetService<ILitecoinSource>(),
            provider.GetService<IBscSource>(),
            provider.GetService<IZcashSource>()));
        services.TryAddSingleton<IWatchFeeds, WatchFeeds>();
        services.AddSingleton<MonitoringService>();
        services.AddSingleton<IMonitoringService>(provider => provider.GetRequiredService<MonitoringService>());
        services.AddSingleton<IBackgroundWatches, BackgroundWatches>();
        return services;
    }

    /// <summary>
    /// Adds the files the open Chainsaw and the background process share: the plan, the queue, the holds, the status
    /// and the background's progress, under <c>%LOCALAPPDATA%\Chainsaw\Bevakning</c> unless another folder is given.
    /// Needs an <c>IUserDataProtector</c> (the Security module's <c>AddUserDataProtection</c>).
    /// </summary>
    /// <param name="services">The container to add to.</param>
    /// <param name="directory">The folder, or <see langword="null"/> for the default.</param>
    /// <returns>The same container.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is null.</exception>
    public static IServiceCollection AddBackgroundWatchFiles(this IServiceCollection services, string? directory = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var folder = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chainsaw",
            BackgroundNames.Folder);
        services.AddSingleton(provider => new BackgroundFolder(
            folder, provider.GetRequiredService<IUserDataProtector>()));
        services.AddSingleton<IBackgroundPlan, BackgroundPlanFile>();
        services.AddSingleton<IWatchQueue, WatchQueue>();
        services.AddSingleton<ICaseHolds, CaseHolds>();
        services.AddSingleton<IBackgroundStatusFile, BackgroundStatusFile>();
        services.AddSingleton<IBackgroundProgressFile, BackgroundProgressFile>();
        return services;
    }
}
