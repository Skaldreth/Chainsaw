using System.Windows;
using Chainsaw.Modules.Cases;
using Chainsaw.Modules.Security;
using Chainsaw.Modules.Network;
using Chainsaw.Modules.Chains;
using Chainsaw.Modules.Clustering;
using Chainsaw.Modules.Attribution;
using Chainsaw.Modules.Tracing;
using Chainsaw.Modules.Checks;
using Chainsaw.Modules.Pricing;
using Chainsaw.Modules.Pricing.Contracts;
using Chainsaw.Modules.Monitoring;
using Chainsaw.Modules.Monitoring.Contracts;
using Chainsaw.Modules.Requests;
using Chainsaw.App.Commands;
using Chainsaw.App.Controls;
using Chainsaw.App.Graph;
using Chainsaw.App.Guide;
using Chainsaw.App.Interop;
using Chainsaw.App.Settings;
using Chainsaw.App.ViewModels;
using Chainsaw.SharedKernel;
using Chainsaw.SharedKernel.Commands;
#if DEBUG
using Chainsaw.App.TestData;
#endif
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Chainsaw.App;

/// <summary>
/// The application: one per Windows user, builds the host with every module and shows the main window, which starts
/// with the first-run setup when there is no vault (sketch FS1a) and with the lock screen otherwise (sketch ID7).
/// </summary>
public partial class App : Application
{
    /// <summary>How long the exit waits for the modules to stop, after the case has been saved.</summary>
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The host with every module, once built.</summary>
    private IHost? _host;

    /// <summary>The hold on being the one Chainsaw of this user, and the pipe that links arrive on.</summary>
    private SingleInstance? _instance;

    /// <summary>
    /// Becomes the one Chainsaw of this user, or hands the link to the one that runs and ends. Then builds and starts
    /// the host, shows the main window and takes the link Chainsaw was started with, which waits for the unlock.
    /// </summary>
    /// <param name="e">The arguments Chainsaw was started with, for example a <c>chainsaw:</c> link.</param>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        CrashGuard.Guard(this);
        var argument = e.Args.Length > 0 ? e.Args[0] : string.Empty;
        _instance = SingleInstance.TryAcquire();
        if (_instance is null)
        {
            SingleInstance.Send(argument);
            Shutdown();
            return;
        }

        _host = BuildHost();
        await _host.StartAsync();
        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        var links = _host.Services.GetRequiredService<LinkInbox>();
        links.Receive(argument);
        _instance.Listen(text => Dispatcher.BeginInvoke(() =>
        {
            links.Receive(text);
            window.BringForward();
        }));
        window.Show();
    }

    /// <summary>
    /// Saves and closes the open case first and waits for it, so that Chainsaw never ends before the case is saved;
    /// then stops the host so that every module can close what it holds.
    /// </summary>
    /// <param name="e">The exit arguments.</param>
    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            if (_host is not null)
            {
                _host.Services.GetRequiredService<CaseSession>().CloseNow();
                StopModules(_host);
            }
        }
        finally
        {
            // Disposing the host disposes the case store, which closes every case still open, also one that a lock
            // or a creation left closing in the background.
            try
            {
                _host?.Dispose();
            }
            finally
            {
                _instance?.Dispose();
                base.OnExit(e);
            }
        }
    }

    /// <summary>
    /// Stops the modules on the thread pool, since a handler that posts to this thread must never wait for it; a module
    /// that fails to stop does not keep the others, or the host's disposal, from running.
    /// </summary>
    /// <param name="host">The host.</param>
    private static void StopModules(IHost host)
    {
        try
        {
            Task.Run(() => host.StopAsync()).Wait(StopTimeout);
        }
        catch (AggregateException)
        {
            // A module failed to stop; the host's disposal still closes what is held.
        }
    }

    /// <summary>
    /// Registers the clock, every module, the settings, the gate, the shell and the main window in one container. The
    /// settings' file is read once, here, so that the way out, the currency and the lock hold from the start.
    /// </summary>
    /// <returns>The host, not yet started.</returns>
    private IHost BuildHost()
    {
        var store = new AppSettingsStore();
        var settings = store.Load();
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services => services
                .AddSingleton<IClock, SystemClock>()
                .AddSingleton<IUiThread>(new WpfUiThread(Dispatcher))
                .AddSingleton(store)
                .AddSingleton(settings)
                .AddSingleton(new AttributionSwitches(settings.AttributionProviders))
                .AddSingleton(BuildInfo.From(typeof(App).Assembly))
                .AddSingleton(provider => StartAutoLock(provider.GetRequiredService<IClock>(), settings))
                .AddCasesModule()
                .AddSecurityModule()
                .AddNetworkModule(options => options.Route = settings.ExitRoute)
                .AddChainsModule(sources: options => options.Directory = Path.GetDirectoryName(store.FilePath))
                .AddClusteringModule()
                .AddAttributionModule()
                .AddTracingModule()
                .AddChecksModule()
                .AddPricingModule(new PricingOptions
                {
                    Currency = settings.Currency,
                    Resolution = settings.Resolution,
                })
                .AddMonitoringModule()
                .AddBackgroundWatchFiles()
                .AddRequestsModule()
                .AddSingleton<CaseSession>()
                .AddSingleton<CaseWorkflow>()
                .AddSingleton<LockSignal>()
                .AddSingleton<IStartPointPrefetch, StartPointPrefetch>()
                .AddSingleton<NewCaseViewModel>()
                .AddSingleton<StartPointInput>()
                .AddSingleton<IShellCommand, NewCaseCommand>()
                .AddSingleton<IShellCommand, OpenCaseCommand>()
                .AddSingleton<IShellCommand, CloseCaseCommand>()
                .AddSingleton<IShellCommand, VerifyCommand>()
                .AddSingleton<IShellCommand, LockCommand>()
                .AddSingleton<CommandRegistry>()
                .AddSingleton<IFilePicker, WpfFilePicker>()
                .AddSingleton<SanctionListImport>()
                .AddSingleton<ExitWay>()
                .AddSingleton<ExitPanelViewModel>()
                .AddSingleton<CasePickerViewModel>()
                .AddSingleton<SourcesViewModel>()
                .AddSingleton<SettingsViewModel>()
                .AddSingleton<IGraphSource, GraphSource>()
                .AddSingleton<IGraphEditor, GraphEditor>()
                .AddSingleton<ICheckSource, CheckSource>()
                .AddSingleton<Toaster>()
                .AddSingleton<ITrashSource, TrashSource>()
                .AddSingleton<CaseTrash>()
                .AddSingleton<GraphViewModel>()
                .AddSingleton<IWatchSource, WatchSource>()
                .AddSingleton<WatchBoard>()
                .AddSingleton<WatchFormViewModel>()
                .AddSingleton<MonitoringViewModel>()
                .AddSingleton<IBackgroundProcess, WindowsBackgroundProcess>()
                .AddSingleton<IAutostart, RunKeyAutostart>()
                .AddSingleton(provider => new BackgroundParts(
                    provider.GetRequiredService<IBackgroundWatches>(),
                    provider.GetRequiredService<IBackgroundPlan>(),
                    provider.GetRequiredService<ICaseHolds>(),
                    provider.GetRequiredService<IWatchQueue>(),
                    provider.GetRequiredService<IBackgroundStatusFile>(),
                    provider.GetRequiredService<IBackgroundProcess>(),
                    provider.GetRequiredService<IAutostart>(),
                    provider.GetRequiredService<IClock>()))
                .AddSingleton(provider => new BackgroundLink(
                    provider.GetRequiredService<BackgroundParts>(),
                    provider.GetRequiredService<CaseSession>(),
                    provider.GetRequiredService<WatchBoard>(),
                    provider.GetRequiredService<IUiThread>(),
                    settings))
                .AddSingleton(provider => new WatchRunner(
                    provider.GetRequiredService<WatchBoard>(),
                    provider.GetRequiredService<IClock>(),
                    import: provider.GetRequiredService<BackgroundLink>().ImportAsync))
                .AddSingleton(_ => GuideLibrary.Load())
                .AddSingleton<GuidePanelViewModel>()
                .AddSingleton<GuideViewModel>()
                .AddSingleton<ShellViewModel>()
                .AddSingleton<LinkInbox>()
                .AddSingleton<LockViewModel>()
                .AddSingleton<FirstRunViewModel>()
                .AddSingleton<GateViewModel>()
                .AddSingleton<MainWindow>()
#if DEBUG
                // /testdata and its made-up source exist only in a Debug build; they wrap the modules' services, so
                // they come last.
                .AddTestData()
#endif
                )
            .Build();
    }

    /// <summary>Creates the automatic lock with the time and the lock with Windows from the settings.</summary>
    /// <param name="clock">The clock.</param>
    /// <param name="settings">The settings read at the start.</param>
    /// <returns>The automatic lock.</returns>
    private static AutoLock StartAutoLock(IClock clock, AppSettings settings)
    {
        var autoLock = new AutoLock(clock, settings.AutoLockTimeout);
        autoLock.LockWithWindows(settings.LockWithWindows);
        return autoLock;
    }
}
