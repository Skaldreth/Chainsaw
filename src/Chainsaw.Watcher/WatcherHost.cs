using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Cases.Contracts;
using Chainsaw.Modules.Chains;
using Chainsaw.Modules.Chains.Contracts;
using Chainsaw.Modules.Monitoring;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Network;
using Chainsaw.Modules.Network.Contracts;
using Chainsaw.Modules.Security;
using Chainsaw.Modules.Security.Contracts;
using Chainsaw.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Chainsaw.Watcher;

/// <summary>How a run of the loop ended.</summary>
internal enum RunEnd
{
    /// <summary>There is nothing to watch, or it was asked to stop.</summary>
    Stopped,

    /// <summary>The chains' endpoints changed: the services are made anew from the settings.</summary>
    SourcesChanged,
}

/// <summary>
/// Runs rounds until there is nothing to watch or it is asked to stop, waiting <see cref="Tick"/> between them, and
/// ends when the chains' endpoints change in Chainsaw's settings, so that the services are made anew from them.
/// </summary>
/// <param name="loop">The rounds.</param>
/// <param name="sourcesFile">The endpoints' file (<c>sources.json</c>), or <see langword="null"/>.</param>
/// <param name="delay">Waits between rounds.</param>
internal sealed class WatcherRun(
    WatcherLoop loop, string? sourcesFile, Func<TimeSpan, CancellationToken, Task> delay)
{
    /// <summary>How long between rounds.</summary>
    public static readonly TimeSpan Tick = TimeSpan.FromSeconds(15);

    /// <summary>Runs until it ends.</summary>
    /// <param name="cancellationToken">Asks it to stop.</param>
    /// <returns>How it ended.</returns>
    public async Task<RunEnd> RunAsync(CancellationToken cancellationToken)
    {
        var sources = Stamp();
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                if (!await loop.RunOnceAsync(cancellationToken).ConfigureAwait(false))
                {
                    return RunEnd.Stopped;
                }

                await delay(Tick, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return RunEnd.Stopped;
            }
            catch (Exception exception) when (exception is not (OperationCanceledException or OutOfMemoryException))
            {
                // Nothing may take the quiet process down (a file held, a disk full): it waits a round and goes on.
                try
                {
                    await delay(Tick, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return RunEnd.Stopped;
                }
            }

            if (Stamp() != sources)
            {
                return RunEnd.SourcesChanged;
            }
        }

        return RunEnd.Stopped;
    }

    /// <summary>Gives when the endpoints' file was last written.</summary>
    /// <returns>The time, or <see langword="null"/>.</returns>
    private DateTime? Stamp() =>
        sourcesFile is not null && File.Exists(sourcesFile) ? File.GetLastWriteTimeUtc(sourcesFile) : null;
}

/// <summary>Puts the background process together: the modules it needs, and nothing of the vault or the cases.</summary>
internal static class WatcherServices
{
    /// <summary>Chainsaw's folder under the user's local application data.</summary>
    public static string AppData { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Chainsaw");

    /// <summary>
    /// Builds the services: the chains' sources from Chainsaw's endpoints, the way out (Windows, or the exit the user
    /// allowed in the background, read from its file under DPAPI and never from the vault), the check and feeds of
    /// Monitoring, the shared files under DPAPI and the ways of telling.
    /// </summary>
    /// <param name="appData">Chainsaw's folder.</param>
    /// <param name="configure">Lets a test put its own services in place (protector, clock, ways, network).</param>
    /// <returns>The services.</returns>
    public static ServiceProvider Build(string appData, Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.TryAddSingleton<IClock, SystemClock>();
        services.AddSingleton<IOperatorProfileStore, BackgroundOperator>();
        services.AddSingleton<IVaultSecrets, LockedSecrets>();
        services.AddUserDataProtection();
        services.AddNetworkModule(options =>
        {
            options.Route = ExitRoute.Vpn;
            options.BackgroundExitsFile =
                Path.Combine(appData, BackgroundNames.Folder, NetworkOptions.BackgroundExitsFileName);
            options.ExitsFromBackground = true;
        });
        services.AddChainsModule(sources: options => options.Directory = appData);
        services.AddSingleton<WatcherGate>();
        services.AddSingleton<IWatchGate>(provider => provider.GetRequiredService<WatcherGate>());
        services.AddMonitoringModule();
        services.AddBackgroundWatchFiles(Path.Combine(appData, BackgroundNames.Folder));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAlertChannel, WindowsNotification>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAlertChannel, CoinSound>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAlertChannel, ScreenMessage>());
        services.AddSingleton<IAlerts, Alerts>();
        services.AddSingleton(provider => new WatcherParts(
            provider.GetRequiredService<IBackgroundPlan>(),
            provider.GetRequiredService<IBackgroundProgressFile>(),
            provider.GetRequiredService<IWatchQueue>(),
            provider.GetRequiredService<ICaseHolds>(),
            provider.GetRequiredService<IBackgroundStatusFile>(),
            provider.GetRequiredService<IWatchCheck>(),
            provider.GetRequiredService<IWatchFeeds>(),
            provider.GetRequiredService<IChainSources>(),
            provider.GetRequiredService<IBitcoinSource>(),
            provider.GetRequiredService<IEthereumSource>(),
            provider.GetRequiredService<IResponseTransfer>(),
            provider.GetRequiredService<IAlerts>(),
            provider.GetRequiredService<IClock>())
        {
            Exit = provider.GetRequiredService<IExitConnection>(),
            Exits = provider.GetRequiredService<IBackgroundExits>(),
        });
        services.AddMemoryCases();
        services.AddSingleton<Func<IMemoryCase>>(provider =>
        {
            // Only the chains' tables: the round's case in memory holds the raw responses and nothing else.
            var cases = provider.GetRequiredService<IMemoryCases>();
            return () => cases.Create(SealedResponses.Chain);
        });
        services.AddSingleton(provider => new WatcherLoop(
            provider.GetRequiredService<WatcherParts>(),
            provider.GetRequiredService<IExitRoute>(),
            provider.GetRequiredService<WatcherGate>(),
            provider.GetRequiredService<Func<IMemoryCase>>()));

        // Last, so that what a test puts in place wins.
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
